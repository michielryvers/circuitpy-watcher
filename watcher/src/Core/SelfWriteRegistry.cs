using System.Collections.Concurrent;

namespace Watcher.Core;

public static class SelfWriteRegistry
{
    private static readonly ConcurrentDictionary<string, DateTime> _map = new();
    private static readonly TimeSpan _window = TimeSpan.FromSeconds(2);

    public static void Register(string fullPath)
    {
        _map[fullPath] = DateTime.UtcNow;
    }

    public static bool IsRecent(string fullPath)
    {
        if (_map.TryGetValue(fullPath, out var when))
        {
            return DateTime.UtcNow - when < _window;
        }
        return false;
    }
}
