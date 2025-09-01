namespace Watcher.Core;

public static class FileTimes
{
    public static void SetFileMTimeFromNs(string filePath, long modifiedNs)
    {
        if (modifiedNs <= 0)
            return; // skip if unknown
        var ms = modifiedNs / 1_000_000L;
        var dt = DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime;
        File.SetLastWriteTimeUtc(filePath, dt);
    }

    public static void SetDirectoryMTimeFromNs(string dirPath, long modifiedNs)
    {
        if (modifiedNs <= 0)
            return;
        var ms = modifiedNs / 1_000_000L;
        var dt = DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime;
        Directory.SetLastWriteTimeUtc(dirPath, dt);
    }
}
