using System.Net.Sockets;

namespace PayloadDrop;

public record PayloadFile(string Path, long Bytes, DateTime Modified);
public record TransferProgress(long Sent, long Total);
public static class Transfer
{
    public static PayloadFile Inspect(string path)
    {
        using var file = Open(path);
        return new(Path.GetFullPath(path), file.Length, File.GetLastWriteTimeUtc(path));
    }

    static FileStream Open(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext is not ".elf" and not ".bin") throw new IOException("Choose a .elf or .bin payload. Game plugins are loaded inside their game.");
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 131072, FileOptions.Asynchronous | FileOptions.SequentialScan);
        try
        {
            if (stream.Length == 0) throw new IOException("The payload is empty.");
            if (ext == ".elf")
            {
                Span<byte> header = stackalloc byte[20];
                if (stream.Read(header) != header.Length || !header[..7].SequenceEqual(new byte[] { 127, 69, 76, 70, 2, 1, 1 }) || header[18] != 62 || header[19] != 0)
                    throw new IOException("This file is not a little-endian x86-64 ELF payload.");
            }
            stream.Position = 0;
            return stream;
        }
        catch { stream.Dispose(); throw; }
    }

    public static async Task Send(PayloadFile file, string host, int port, Action<TransferProgress> progress, CancellationToken cancel)
    {
        cancel.ThrowIfCancellationRequested();
        await using var source = Open(file.Path);
        if (source.Length != file.Bytes || File.GetLastWriteTimeUtc(file.Path) != file.Modified)
            throw new IOException("The file changed after the queue started. Check it before sending again.");
        using var client = new TcpClient { NoDelay = true };
        using (var connect = CancellationTokenSource.CreateLinkedTokenSource(cancel))
        {
            connect.CancelAfter(TimeSpan.FromSeconds(8));
            try { await client.ConnectAsync(host, port, connect.Token); }
            catch (OperationCanceledException) when (!cancel.IsCancellationRequested) { throw new IOException("Connection timed out. Check the console IP, port and payload loader."); }
        }
        await using var target = client.GetStream();
        byte[] buffer = new byte[131072]; long sent = 0;
        for (;;)
        {
            using var idle = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            idle.CancelAfter(TimeSpan.FromSeconds(30));
            try
            {
                int count = await source.ReadAsync(buffer, idle.Token);
                if (count == 0) break;
                await target.WriteAsync(buffer.AsMemory(0, count), idle.Token);
                sent += count; progress(new(sent, file.Bytes));
            }
            catch (OperationCanceledException) when (!cancel.IsCancellationRequested) { throw new IOException("Transfer stalled. Some bytes may have reached the console; no retry was made."); }
        }
        cancel.ThrowIfCancellationRequested();
        client.Client.Shutdown(SocketShutdown.Send);
        // Raw ELF loaders have no common execution acknowledgement. Complete
        // means bytes handed to TCP, never that the payload initialized.
    }
}
