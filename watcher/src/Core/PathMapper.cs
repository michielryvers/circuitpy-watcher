using System;
using System.IO;

namespace Watcher.Core;

public static class PathMapper
{
	public static string ToRemoteFilePath(string localRoot, string localFullPath)
	{
		var rel = GetRelative(localRoot, localFullPath);
		var remote = "/" + rel.Replace(Path.DirectorySeparatorChar, '/');
		if (remote.EndsWith('/'))
			throw new ArgumentException("Expected file path, got directory path", nameof(localFullPath));
		return remote;
	}

	public static string ToRemoteDirectoryPath(string localRoot, string localFullPath)
	{
		var rel = GetRelative(localRoot, localFullPath);
		var remote = "/" + rel.Replace(Path.DirectorySeparatorChar, '/');
		if (!remote.EndsWith('/')) remote += "/";
		return remote;
	}

	public static string ToLocalPathFromRemote(string localRoot, string remotePath)
	{
		var trimmed = remotePath.StartsWith('/') ? remotePath[1..] : remotePath;
		var local = Path.Combine(localRoot, trimmed.Replace('/', Path.DirectorySeparatorChar));
		return local;
	}

	public static bool IsSymlink(string path)
	{
		var attr = File.GetAttributes(path);
		return (attr & FileAttributes.ReparsePoint) != 0;
	}

	private static string GetRelative(string localRoot, string localFullPath)
	{
		localRoot = Path.GetFullPath(localRoot);
		localFullPath = Path.GetFullPath(localFullPath);
		if (!localFullPath.StartsWith(localRoot, StringComparison.Ordinal))
			throw new ArgumentException("Path is outside of local root", nameof(localFullPath));
		var rel = localFullPath.Substring(localRoot.Length).TrimStart(Path.DirectorySeparatorChar);
		return rel;
	}
}
