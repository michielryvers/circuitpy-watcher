using System.IO;
using Spectre.Console;
using Watcher.Config;
using Watcher.Repl;
using Watcher.Tui;

static int PrintUsage()
{
    AnsiConsole.MarkupLine(
        "[yellow]Usage:[/] watcher --address [cyan]<host[:port]>[/] --password [cyan]<password>[/]"
    );
    AnsiConsole.MarkupLine("[grey62]Keys: Ctrl-C interrupt • Ctrl-D reboot • Ctrl-] exit[/]");
    return 2;
}

var address = string.Empty;
var password = string.Empty;
var useMock = false;
var debug = false;

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
    else if (args[i] == "--mock")
    {
        useMock = true;
    }
    else if (args[i] == "--debug")
    {
        debug = true;
    }
}

if (string.IsNullOrWhiteSpace(address) || string.IsNullOrWhiteSpace(password))
{
    return PrintUsage();
}

var cfg = new AppConfig { Address = address, Password = password };
ConsoleEx.Banner(useMock ? "CircuitPy Watcher + REPL (Mock)" : "CircuitPy Watcher + REPL");
return await InteractiveRunner.RunAsync(cfg, useMock, debug);
