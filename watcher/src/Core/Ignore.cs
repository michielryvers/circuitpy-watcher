using System;
using System.IO;

namespace Watcher.Core;

public static class Ignore
{
	// Directory names to skip (case-sensitive by default on Linux)
	public static readonly string[] DirectoryNames = new[]
	{
		".git", ".vscode", "__pycache__", ".idea", "node_modules"
	};

	// File names or extensions to skip
	public static readonly string[] FileNames = new[]
	{
		".DS_Store", "Thumbs.db"
	};

	public static readonly string[] FileExtensions = new[]
	{
		".swp", ".tmp"
	};

	public static bool IsIgnored(string path)
	{
		var name = Path.GetFileName(path);
		if (string.IsNullOrEmpty(name)) return false;

		// directories
		foreach (var d in DirectoryNames)
		{
			if (string.Equals(name, d, StringComparison.Ordinal)) return true;
		}

		// exact file names
		foreach (var f in FileNames)
		{
			if (string.Equals(name, f, StringComparison.Ordinal)) return true;
		}

		// by extension
		var ext = Path.GetExtension(name);
		if (!string.IsNullOrEmpty(ext))
		{
			foreach (var e in FileExtensions)
			{
				if (string.Equals(ext, e, StringComparison.OrdinalIgnoreCase)) return true;
			}
		}

		return false;
	}
}
