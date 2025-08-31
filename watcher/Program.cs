using Watcher.Config;
using Watcher.Http;
using Watcher.Sync;
using System.IO;

static int PrintUsage()
{
	Console.WriteLine("Usage: watcher --address <host[:port]> --password <password>");
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
		Console.Error.WriteLine("Authentication failed (401/403). Exiting.");
		return 1;
	}
	Console.Error.WriteLine($"Failed to reach device: {(int)version.StatusCode} {version.StatusCode}");
	return 1;
}

Console.WriteLine($"Connected to {version.Body?.Hostname ?? cfg.Address} (Web API v{version.Body?.WebApiVersion})");

// Task 5: Full pull startup behavior - delete local folder then fresh pull
if (Directory.Exists(cfg.LocalRoot))
{
	Console.WriteLine($"Deleting existing local mirror: {cfg.LocalRoot}");
	Directory.Delete(cfg.LocalRoot, recursive: true);
}

Directory.CreateDirectory(cfg.LocalRoot);

using var client2 = new WebWorkflowClient(cfg);
var puller = new FullPuller(cfg, client2);
await puller.RunAsync(CancellationToken.None);

Console.WriteLine("Initial full pull completed.");

// Start local watcher (sequential processing)
using var watcherSvc = new LocalWatcher(cfg, client2);
Console.WriteLine("Watching for local changes. Press Ctrl+C to exit.");
await watcherSvc.RunAsync(CancellationToken.None);

return 0;
