using Watcher.Config;
using Watcher.Core;
using Watcher.Http;
using Watcher.Tui;

namespace Watcher.Sync;

public sealed class RemotePoller
{
    private readonly AppConfig _cfg;
    private readonly WebWorkflowClient _client;

    public RemotePoller(AppConfig cfg, WebWorkflowClient client)
    {
        _cfg = cfg;
        _client = client;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await PollOnceAsync(ct);
            }
            catch (Exception ex)
            {
                ConsoleEx.Error($"remote poll ({ex.Message})");
            }

            await Task.Delay(TimeSpan.FromSeconds(_cfg.RemotePollIntervalSeconds), ct);
        }
    }

    private async Task PollOnceAsync(CancellationToken ct)
    {
        await WalkAsync("/", _cfg.LocalRoot, ct);
    }

    private async Task WalkAsync(string remoteDir, string localDir, CancellationToken ct)
    {
        if (!remoteDir.EndsWith('/'))
            remoteDir += "/";
        var listing = await _client.GetDirectoryAsync(remoteDir, ct);
        if (!listing.IsSuccess || listing.Body is null)
        {
            return;
        }

        Directory.CreateDirectory(localDir);
        foreach (var entry in listing.Body.Files)
        {
            var remotePath = remoteDir + entry.Name + (entry.IsDirectory ? "/" : string.Empty);
            var localPath = Path.Combine(localDir, entry.Name);

            if (entry.IsDirectory)
            {
                await WalkAsync(remotePath, localPath, ct);
                continue;
            }

            var exists = File.Exists(localPath);
            if (!exists)
            {
                var resp = await _client.GetFileAsync(remoteDir + entry.Name, ct);
                if (resp.IsSuccess && resp.Body != null)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
                    await File.WriteAllBytesAsync(localPath, resp.Body, ct);
                    FileTimes.SetFileMTimeFromNs(localPath, entry.ModifiedNs);
                    SelfWriteRegistry.Register(localPath);
                    ConsoleEx.Action("PULL", Rel(localPath), "reason: missing-local");
                }
                continue;
            }

            var fi = new FileInfo(localPath);
            var localTime = fi.LastWriteTimeUtc;
            var localSize = fi.Length;
            var remoteMs = entry.ModifiedNs / 1_000_000L;
            var remoteTime = DateTimeOffset.FromUnixTimeMilliseconds(remoteMs).UtcDateTime;
            var remoteSize = entry.FileSize;

            if (remoteTime > localTime)
            {
                var resp = await _client.GetFileAsync(remoteDir + entry.Name, ct);
                if (resp.IsSuccess && resp.Body != null)
                {
                    await File.WriteAllBytesAsync(localPath, resp.Body, ct);
                    FileTimes.SetFileMTimeFromNs(localPath, entry.ModifiedNs);
                    SelfWriteRegistry.Register(localPath);
                    ConsoleEx.Action("PULL", Rel(localPath), "reason: remote-newer");
                }
            }
        }
    }

    private string Rel(string fullPath)
    {
        var root = Path.GetFullPath(_cfg.LocalRoot).TrimEnd(Path.DirectorySeparatorChar);
        var fp = Path.GetFullPath(fullPath);
        if (fp.StartsWith(root, StringComparison.Ordinal))
        {
            return fp.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar);
        }
        return fullPath;
    }
}
