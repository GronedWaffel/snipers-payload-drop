# Snipers Payload Drop

Portable Windows payload sender. No Neighborhood, browser, account or installation
required. The self-contained EXE includes its .NET runtime.

1. Start your console's payload loader and enter its IP and port (PS5 default: 9021).
2. Drop any number of `.elf` / `.bin` files into the queue, or choose **Add files**.
3. Drag rows to reorder them, or use **Move up / Move down** (Ctrl+Up/Down).
4. Edit **Wait (s)** for each payload. This is the pause after that payload before
   the next one; the default applies to newly added files. Allow initialization
   time for payloads such as etaHEN—byte transfer completion is not readiness.
5. Click **Send pending**. Files send sequentially through separate TCP connections.

Stop cancels the current transfer and queue. An error stops the queue without
retrying; a partially sent payload may already have reached the console. Sent or
failed items are skipped by Send pending. **Mark all ready** explicitly re-arms
the queue. Remove individual rows or clear the list to send a different set.
The queue is editable only while idle. Duplicate file paths are not added twice.

The app remembers your IP, port, delays and queue in
`%LOCALAPPDATA%\SnipersPayloadDrop\settings.json`. Reopening marks files ready but
never sends automatically. Closing during a transfer cancels it before exiting.

The sender streams files rather than loading them fully into memory. There is no
arbitrary sender size or queue-count limit; the console loader still has its own
limits. ELF headers receive a basic x86-64/little-endian check. BIN is raw data.
PRX/PLUGIN game modules must be loaded through a compatible plugin loader.

**Sent** means TCP transfer completed, not that the console executed or initialized
the payload. This utility does not jailbreak the console or probe its debugger.

## Build / check

Requires the .NET 9 SDK for development:

```
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o ../deliverables/Snipers-Payload-Drop
```

The EXE's `--self-test <absolute-report.json>` mode checks exact localhost transfer
bytes/order, cancellation, connection refusal and invalid/changed files. It never
connects to a console. `--preview <absolute.png>` renders a sample app window and
exits without touching saved settings or sending payloads.
