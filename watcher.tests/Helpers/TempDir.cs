namespace Watcher.Tests.Helpers;

public sealed class TempDir : IDisposable
{
    public string Path { get; }
    public TempDir()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "watcher-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }
    public override string ToString() => Path;
    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); } catch { }
    }
}

