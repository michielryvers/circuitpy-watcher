using System.Text;
using Spectre.Console;
using Watcher.Config;
using Watcher.Http;
using Watcher.Serial;
using Watcher.Sync;
using Watcher.Tui;

namespace Watcher.Repl;

public static class InteractiveRunner
{
    public static async Task<int> RunAsync(AppConfig cfg, bool useMock = false, bool debug = false)
    {
        var logs = new LogBuffer(512);
        ConsoleEx.SetSink(new BufferLogSink(logs));

        var replBuffer = new ReplBuffer(4000);
        replBuffer.Append(
            "REPL ready. Keys: Ctrl-C interrupt • Ctrl-D reboot • Ctrl-] exit • q quit\n"
        );
        HttpMessageHandler? handler = useMock ? new MockHttpHandler() : null;
        using var client = handler is null
            ? new WebWorkflowClient(cfg)
            : new WebWorkflowClient(cfg, handler);

        if (debug)
            ConsoleEx.Info("[DEBUG] Starting preflight version check");
        using var preflightCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var version = await client.GetVersionAsync(preflightCts.Token);
        if (!version.IsSuccess)
        {
            var code = (int)version.StatusCode;
            if (code == 401 || code == 403)
            {
                ConsoleEx.Error("Authentication failed (401/403). Exiting.");
                return 1;
            }
            ConsoleEx.Error(
                $"Failed to reach device: {(int)version.StatusCode} {version.StatusCode}"
            );
            return 1;
        }

        ConsoleEx.Success(
            $"Connected to {version.Body?.Hostname ?? cfg.Address} (Web API v{version.Body?.WebApiVersion})"
        );

        var exitCts = new CancellationTokenSource();
        var appCts = CancellationTokenSource.CreateLinkedTokenSource(exitCts.Token);

        var wsFactory = useMock ? new MockWebSocketFactory() : null;
        var ws = new SerialWebSocketClient(
            cfg,
            wsFactory,
            candidatePathsOverride: new[] { "/cp/serial", "/cp/serial/websocket" }
        );
        if (debug)
            ConsoleEx.Info("[DEBUG] WebSocket client created");
        ws.DataReceived += data => replBuffer.Append(Encoding.UTF8.GetString(data.Span));
        ws.StateChanged += msg => ConsoleEx.Info(msg);

        Console.CancelKeyPress += (s, e) =>
        {
            e.Cancel = true;
            _ = ws.SendAsync(new byte[] { 0x03 }, CancellationToken.None);
        };

        var tasks = new List<Task>();
        tasks.Add(ws.RunAsync(appCts.Token));
        tasks.Add(StartSyncPipelinesAsync(cfg, appCts.Token, handler, debug));
        tasks.Add(ReadKeysAsync(ws, exitCts, debug));
        tasks.Add(RunUiAsync(replBuffer, logs, appCts.Token));

        var t = await Task.WhenAny(tasks);
        exitCts.Cancel();
        appCts.Cancel();
        try
        {
            await Task.WhenAll(tasks);
        }
        catch { }
        return 0;
    }

    private static async Task StartSyncPipelinesAsync(
        AppConfig cfg,
        CancellationToken ct,
        HttpMessageHandler? handler,
        bool debug
    )
    {
        if (Directory.Exists(cfg.LocalRoot))
        {
            ConsoleEx.Warn($"Deleting existing local mirror: {cfg.LocalRoot}");
            try
            {
                Directory.Delete(cfg.LocalRoot, recursive: true);
            }
            catch { }
        }
        Directory.CreateDirectory(cfg.LocalRoot);

        using var client2 = handler is null
            ? new WebWorkflowClient(cfg)
            : new WebWorkflowClient(cfg, handler);
        var puller = new FullPuller(cfg, client2);
        ConsoleEx.Info("Performing initial full pull…");
        try
        {
            await puller.RunAsync(CancellationToken.None);
            ConsoleEx.Success("Initial full pull completed.");
        }
        catch (Exception ex)
        {
            ConsoleEx.Error($"Full pull failed: {ex.Message}");
        }

        var wc = new WriteCoordinator(cfg, client2);
        var watcherSvc = new LocalWatcher(cfg, client2, wc);
        var poller = new RemotePoller(cfg, client2);
        if (debug)
            ConsoleEx.Info("[DEBUG] Starting watcher and poller loops");
        // Keep this method alive until cancellation by awaiting both loops.
        await Task.WhenAll(watcherSvc.RunAsync(ct), poller.RunAsync(ct));
    }

    private static async Task RunUiAsync(ReplBuffer repl, LogBuffer logs, CancellationToken ct)
    {
        var layout = new Layout("root").SplitRows(new Layout("top"), new Layout("bottom").Size(5));

        await AnsiConsole
            .Live(layout)
            .StartAsync(async ctx =>
            {
                while (!ct.IsCancellationRequested)
                {
                    var replText = repl.Snapshot();
                    var replPanel = new Panel(new Markup(replText))
                    {
                        Header = new PanelHeader("REPL / Serial", Justify.Center),
                    };
                    var logLines = string.Join("\n", logs.Snapshot(5));
                    var logPanel = new Panel(new Markup(logLines))
                    {
                        Header = new PanelHeader("Sync Logs", Justify.Center),
                    };
                    layout["top"].Update(replPanel);
                    layout["bottom"].Update(logPanel);
                    ctx.Refresh();
                    await Task.Delay(60, ct);
                }
            });
    }

    private static async Task ReadKeysAsync(
        SerialWebSocketClient ws,
        CancellationTokenSource exitCts,
        bool debug
    )
    {
        while (!exitCts.IsCancellationRequested)
        {
            if (!Console.KeyAvailable)
            {
                await Task.Delay(50);
                continue;
            }
            var key = Console.ReadKey(intercept: true);
            if (debug)
            {
                var kc = (int)key.KeyChar;
                ConsoleEx.Info(
                    $"[DEBUG] Key={key.Key} Char=0x{kc:X} ({(kc >= 32 ? key.KeyChar : ' ')}) Mods={key.Modifiers}"
                );
            }
            // Treat Ctrl-] even if Console does not set Control modifier
            if (key.KeyChar == (char)0x1D)
            {
                exitCts.Cancel();
                break;
            }
            if ((key.Modifiers & ConsoleModifiers.Control) != 0)
            {
                if (key.Key == ConsoleKey.C)
                {
                    await ws.SendAsync(new byte[] { 0x03 }, CancellationToken.None);
                    continue;
                }
                if (key.Key == ConsoleKey.D)
                {
                    await ws.SendAsync(new byte[] { 0x04 }, CancellationToken.None);
                    continue;
                }
                // Ctrl-] often arrives as ASCII 0x1D (GS) on some terminals
                if (key.Key == ConsoleKey.Oem6 || key.KeyChar == ']' || key.KeyChar == (char)0x1D)
                {
                    exitCts.Cancel();
                    break;
                }
            }

            if (key.Key == ConsoleKey.Enter)
            {
                await ws.SendAsync(new byte[] { (byte)'\r' }, CancellationToken.None);
            }
            else if (key.Key == ConsoleKey.Backspace)
            {
                await ws.SendAsync(new byte[] { 0x08 }, CancellationToken.None);
            }
            else if (key.Key == ConsoleKey.Escape || key.KeyChar == 'q' || key.KeyChar == 'Q')
            {
                exitCts.Cancel();
                break;
            }
            else if (!char.IsControl(key.KeyChar))
            {
                var b = Encoding.UTF8.GetBytes(new[] { key.KeyChar });
                await ws.SendAsync(b, CancellationToken.None);
            }
        }
    }
}

internal sealed class ReplBuffer
{
    private readonly int _maxChars;
    private readonly StringBuilder _sb = new();
    private readonly object _lock = new();

    public ReplBuffer(int maxChars)
    {
        _maxChars = Math.Max(200, maxChars);
    }

    public void Append(string text)
    {
        lock (_lock)
        {
            _sb.Append(text);
            if (_sb.Length > _maxChars)
            {
                _sb.Remove(0, _sb.Length - _maxChars);
            }
        }
    }

    public string Snapshot()
    {
        lock (_lock)
        {
            return Spectre.Console.Markup.Escape(_sb.ToString());
        }
    }

    // No line-capped snapshot; we keep fixed panel sizes
}
