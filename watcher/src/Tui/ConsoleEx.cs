using System.Linq;
using Spectre.Console;

namespace Watcher.Tui;

internal interface ILogSink
{
    void Info(string message);
    void Success(string message);
    void Warn(string message);
    void Error(string message);
    void Action(string verb, string subject, string? meta = null);
    void Event(string name, string subject, string? meta = null);
    void Banner(string title);
    T Status<T>(string message, Func<StatusContext, T> action);
    void Status(string message, Action<StatusContext> action);
}

internal sealed class AnsiLogSink : ILogSink
{
    public void Info(string message) =>
        AnsiConsole.MarkupLine($"[deepskyblue2]{Escape(message)}[/]");

    public void Success(string message) => AnsiConsole.MarkupLine($"[green3]{Escape(message)}[/]");

    public void Warn(string message) => AnsiConsole.MarkupLine($"[yellow1]{Escape(message)}[/]");

    public void Error(string message) => AnsiConsole.MarkupLine($"[red1]{Escape(message)}[/]");

    public void Action(string verb, string subject, string? meta = null)
    {
        var v = $"[bold plum2]{Escape(verb).PadRight(5)}[/]";
        var s = $"[white]{Escape(subject)}[/]";
        var m = string.IsNullOrWhiteSpace(meta) ? string.Empty : $" [grey62]({Escape(meta!)})[/]";
        AnsiConsole.MarkupLine($"{v} {s}{m}");
    }

    public void Event(string name, string subject, string? meta = null)
    {
        var n = $"[bold mediumPurple3]{Escape(name).PadRight(5)}[/]";
        var s = $"[white]{Escape(subject)}[/]";
        var m = string.IsNullOrWhiteSpace(meta) ? string.Empty : $" [grey62]({Escape(meta!)})[/]";
        AnsiConsole.MarkupLine($"{n} {s}{m}");
    }

    public void Banner(string title)
    {
        var panel = new Panel(new Markup($"[bold deepskyblue1]{Escape(title)}[/]"))
        {
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(Color.BlueViolet),
            Padding = new Padding(1, 0, 1, 0),
        };
        AnsiConsole.Write(panel);
    }

    public T Status<T>(string message, Func<StatusContext, T> action)
    {
        return AnsiConsole
            .Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse("deepskyblue2"))
            .Start(message, action);
    }

    public void Status(string message, Action<StatusContext> action)
    {
        AnsiConsole
            .Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse("deepskyblue2"))
            .Start(message, action);
    }

    private static string Escape(string text) => Markup.Escape(text);
}

internal sealed class BufferLogSink : ILogSink
{
    private readonly LogBuffer _buffer;

    public BufferLogSink(LogBuffer buffer)
    {
        _buffer = buffer;
    }

    public void Info(string message) => _buffer.AddLine($"[deepskyblue2]{Escape(message)}[/]");

    public void Success(string message) => _buffer.AddLine($"[green3]{Escape(message)}[/]");

    public void Warn(string message) => _buffer.AddLine($"[yellow1]{Escape(message)}[/]");

    public void Error(string message) => _buffer.AddLine($"[red1]{Escape(message)}[/]");

    public void Action(string verb, string subject, string? meta = null)
    {
        var v = $"[bold plum2]{Escape(verb).PadRight(5)}[/]";
        var s = $"[white]{Escape(subject)}[/]";
        var m = string.IsNullOrWhiteSpace(meta) ? string.Empty : $" [grey62]({Escape(meta!)} )[/]";
        _buffer.AddLine($"{v} {s}{m}");
    }

    public void Event(string name, string subject, string? meta = null)
    {
        var n = $"[bold mediumPurple3]{Escape(name).PadRight(5)}[/]";
        var s = $"[white]{Escape(subject)}[/]";
        var m = string.IsNullOrWhiteSpace(meta) ? string.Empty : $" [grey62]({Escape(meta!)} )[/]";
        _buffer.AddLine($"{n} {s}{m}");
    }

    public void Banner(string title) => _buffer.AddLine($"[bold deepskyblue1]{Escape(title)}[/]");

    public T Status<T>(string message, Func<StatusContext, T> action) => action(default!);

    public void Status(string message, Action<StatusContext> action) => action(default!);

    private static string Escape(string text) => Markup.Escape(text);
}

internal sealed class FanoutLogSink : ILogSink
{
    private readonly ILogSink[] _sinks;

    public FanoutLogSink(params ILogSink[] sinks)
    {
        _sinks = sinks;
    }

    public void Info(string message)
    {
        foreach (var s in _sinks)
            s.Info(message);
    }

    public void Success(string message)
    {
        foreach (var s in _sinks)
            s.Success(message);
    }

    public void Warn(string message)
    {
        foreach (var s in _sinks)
            s.Warn(message);
    }

    public void Error(string message)
    {
        foreach (var s in _sinks)
            s.Error(message);
    }

    public void Action(string verb, string subject, string? meta = null)
    {
        foreach (var s in _sinks)
            s.Action(verb, subject, meta);
    }

    public void Event(string name, string subject, string? meta = null)
    {
        foreach (var s in _sinks)
            s.Event(name, subject, meta);
    }

    public void Banner(string title)
    {
        foreach (var s in _sinks)
            s.Banner(title);
    }

    public T Status<T>(string message, Func<StatusContext, T> action)
    {
        // Execute action once
        var result = action(default!);
        foreach (var s in _sinks)
            s.Info(message);
        return result;
    }

    public void Status(string message, Action<StatusContext> action)
    {
        action(default!);
        foreach (var s in _sinks)
            s.Info(message);
    }
}

internal sealed class LogBuffer
{
    private readonly object _lock = new();
    private readonly Queue<string> _lines = new();
    private readonly int _capacity;

    public LogBuffer(int capacity)
    {
        _capacity = Math.Max(1, capacity);
    }

    public void AddLine(string markup)
    {
        lock (_lock)
        {
            _lines.Enqueue(markup);
            while (_lines.Count > _capacity)
                _lines.Dequeue();
        }
    }

    public string[] Snapshot(int takeLast)
    {
        lock (_lock)
        {
            return _lines.Skip(Math.Max(0, _lines.Count - takeLast)).ToArray();
        }
    }
}

internal static class ConsoleEx
{
    private static ILogSink _sink = new AnsiLogSink();

    public static void SetSink(ILogSink sink) => _sink = sink;

    public static void Info(string message) => _sink.Info(message);

    public static void Success(string message) => _sink.Success(message);

    public static void Warn(string message) => _sink.Warn(message);

    public static void Error(string message) => _sink.Error(message);

    public static void Action(string verb, string subject, string? meta = null) =>
        _sink.Action(verb, subject, meta);

    public static void Event(string name, string subject, string? meta = null) =>
        _sink.Event(name, subject, meta);

    public static void Banner(string title) => _sink.Banner(title);

    public static T Status<T>(string message, Func<StatusContext, T> action) =>
        _sink.Status(message, action);

    public static void Status(string message, Action<StatusContext> action) =>
        _sink.Status(message, action);
}
