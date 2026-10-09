using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;

namespace PayloadDrop;
static class SelfTest
{
    public static async Task<int> Run(string report)
    {
        var results=new List<string>();string root=Path.Combine(Path.GetTempPath(),"PayloadDrop-test-"+Guid.NewGuid());Directory.CreateDirectory(root);
        try
        {
            var paths=new List<string>();
            foreach(int size in new[]{256,1024*1024+17,65537})
            {
                var bytes=RandomNumberGenerator.GetBytes(size);new byte[]{127,69,76,70,2,1,1}.CopyTo(bytes,0);bytes[18]=62;bytes[19]=0;
                var path=Path.Combine(root,size+".elf");File.WriteAllBytes(path,bytes);paths.Add(path);
            }
            using var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();int port=((IPEndPoint)listener.LocalEndpoint).Port;
            var expected=paths.Select(p=>SHA256.HashData(File.ReadAllBytes(p))).ToArray();
            async Task Receive()
            {
                for(int i=0;i<3;i++)
                {
                    using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(15));using var client=await listener.AcceptTcpClientAsync(timeout.Token);
                    using var received=new MemoryStream();await client.GetStream().CopyToAsync(received,timeout.Token);
                    if(!SHA256.HashData(received.ToArray()).SequenceEqual(expected[i]))throw new Exception("Transfer bytes/order mismatch");
                }
            }
            var receiver=Receive();foreach(var path in paths)await Transfer.Send(Transfer.Inspect(path),"127.0.0.1",port,_=>{},CancellationToken.None);await receiver;
            results.Add("Three independent raw TCP transfers preserve exact bytes and queue order, including a multi-buffer payload.");
            var changed=Transfer.Inspect(paths[0]);File.AppendAllText(paths[0],"changed");
            try{await Transfer.Send(changed,"127.0.0.1",port,_=>{},CancellationToken.None);throw new Exception("Changed file accepted");}catch(IOException){results.Add("Changed files rejected before connection.");}
            var bad=Path.Combine(root,"bad.elf");File.WriteAllText(bad,"not an ELF");try{Transfer.Inspect(bad);throw new Exception("Invalid ELF accepted");}catch(IOException){results.Add("Invalid ELF rejected.");}
            var bin=Path.Combine(root,"payload.bin");File.WriteAllBytes(bin,[1,2,3]);if(Transfer.Inspect(bin).Bytes!=3)throw new Exception("BIN rejected");results.Add("Raw BIN payloads accepted.");
            using(var cancelled=new CancellationTokenSource()){cancelled.Cancel();try{await Transfer.Send(Transfer.Inspect(paths[1]),"127.0.0.1",port,_=>{},cancelled.Token);throw new Exception("Cancellation ignored");}catch(OperationCanceledException){results.Add("Cancelled transfer never connects.");}}
            var receiving=Task.Run(async()=>{using var timeout=new CancellationTokenSource(10000);using var client=await listener.AcceptTcpClientAsync(timeout.Token);using var sink=new MemoryStream();try{await client.GetStream().CopyToAsync(sink,timeout.Token);}catch(IOException){}return sink.Length;});
            using(var cancelled=new CancellationTokenSource()){try{await Transfer.Send(Transfer.Inspect(paths[1]),"127.0.0.1",port,_=>cancelled.Cancel(),cancelled.Token);throw new Exception("Mid-transfer cancellation ignored");}catch(OperationCanceledException){}}
            if(await receiving>=new FileInfo(paths[1]).Length)throw new Exception("Cancellation sent full file");results.Add("Mid-transfer stop terminates the transfer without an automatic retry.");
            listener.Stop();try{await Transfer.Send(Transfer.Inspect(paths[2]),"127.0.0.1",port,_=>{},CancellationToken.None);throw new Exception("Refused connection accepted");}catch(SocketException){results.Add("Connection refusal is reported.");}
            File.WriteAllText(report,JsonSerializer.Serialize(new{passed=true,checks=results},new JsonSerializerOptions{WriteIndented=true}));return 0;
        }
        catch(Exception e){File.WriteAllText(report,JsonSerializer.Serialize(new{passed=false,checks=results,error=e.ToString()},new JsonSerializerOptions{WriteIndented=true}));return 1;}
        finally{Directory.Delete(root,true);}
    }
}
