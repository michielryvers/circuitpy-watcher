using System.IO;
using Spectre.Console;
using Watcher.Config;
using Watcher.Http;
using Watcher.Sync;
using Watcher.Tui;

static int PrintUsage()
{
    AnsiConsole.MarkupLine(
        "[yellow]Usage:[/] watcher --address [cyan]<host[:port]>[/] --password [cyan]<password>[/]"
    );
    return 2;
}

var address = string.Empty;
var password = string.Empty;

for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--address" && i + 1 < args.Length)
    {
        address = args[++i];
    }
    else if (args[i] == "--password" && i + 1 < args.Length)
    {
        password = args[++i];
    }
}

if (string.IsNullOrWhiteSpace(address) || string.IsNullOrWhiteSpace(password))
{
    return PrintUsage();
}

var cfg = new AppConfig { Address = address, Password = password };
using var client = new WebWorkflowClient(cfg);

var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
var version = await client.GetVersionAsync(cts.Token);

if (!version.IsSuccess)
{
    var code = (int)version.StatusCode;
    if (code == 401 || code == 403)
    {
        ConsoleEx.Error("Authentication failed (401/403). Exiting.");
        return 1;
    }
    ConsoleEx.Error($"Failed to reach device: {(int)version.StatusCode} {version.StatusCode}");
    return 1;
}

ConsoleEx.Banner("CircuitPy Watcher");
ConsoleEx.Success(
    $"Connected to {version.Body?.Hostname ?? cfg.Address} (Web API v{version.Body?.WebApiVersion})"
);

// Task 5: Full pull startup behavior - delete local folder then fresh pull
if (Directory.Exists(cfg.LocalRoot))
{
    ConsoleEx.Warn($"Deleting existing local mirror: {cfg.LocalRoot}");
    Directory.Delete(cfg.LocalRoot, recursive: true);
}

Directory.CreateDirectory(cfg.LocalRoot);

using var client2 = new WebWorkflowClient(cfg);
var puller = new FullPuller(cfg, client2);
ConsoleEx.Status(
    "Performing initial full pull…",
    ctx =>
    {
        puller.RunAsync(CancellationToken.None).GetAwaiter().GetResult();
    }
);

ConsoleEx.Success("Initial full pull completed.");

// Start local watcher (sequential processing)
var wc = new WriteCoordinator(cfg, client2);
using var watcherSvc = new LocalWatcher(cfg, client2, wc);
var poller = new RemotePoller(cfg, client2);

using var ctsMain = new CancellationTokenSource();
Console.CancelKeyPress += (s, e) =>
{
    e.Cancel = true;
    ctsMain.Cancel();
};

ConsoleEx.Info("Watching for local changes and polling remote. Press Ctrl+C to exit.");

await Task.WhenAll(watcherSvc.RunAsync(ctsMain.Token), poller.RunAsync(ctsMain.Token));

return 0;
