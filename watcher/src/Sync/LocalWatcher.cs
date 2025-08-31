using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Threading.Channels;
using Watcher.Config;
using Watcher.Core;
using Watcher.Http;
using Watcher.Remote;

namespace Watcher.Sync;

public sealed class LocalWatcher : IDisposable
{
	private readonly AppConfig _cfg;
	private readonly WebWorkflowClient _client;
	private readonly WriteCoordinator _wc;
	private readonly FileSystemWatcher _fsw;
	private readonly ConcurrentDictionary<string, System.Timers.Timer> _debouncers = new();
	private readonly Channel<string> _queue = Channel.CreateUnbounded<string>();
	private readonly ConcurrentDictionary<string, DateTime> _selfWrites = new();
	private readonly TimeSpan _selfWriteWindow = TimeSpan.FromSeconds(2);

	public LocalWatcher(AppConfig cfg, WebWorkflowClient client, WriteCoordinator wc)
	{
		_cfg = cfg;
		_client = client;
		_wc = wc;
		_fsw = new FileSystemWatcher(cfg.LocalRoot)
		{
			IncludeSubdirectories = true,
			NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size
		};
		_fsw.Changed += OnChangedOrCreated;
		_fsw.Created += OnChangedOrCreated;
		_fsw.Renamed += OnRenamed;
		_fsw.Deleted += OnDeleted; // policy: ignore
	}

	public Task RunAsync(CancellationToken ct)
	{
		_fsw.EnableRaisingEvents = true;
		return ProcessQueueAsync(ct);
	}

	private void OnChangedOrCreated(object sender, FileSystemEventArgs e)
	{
		var path = e.FullPath;
		if (IgnorePath(path)) return;
		if (SelfWriteRegistry.IsRecent(path))
		{
			// Suppress our own recent writes
			return;
		}
		var t = _debouncers.GetOrAdd(path, _ => new System.Timers.Timer(_cfg.DebounceMilliseconds) { AutoReset = false });
		t.Stop();
		t.Interval = _cfg.DebounceMilliseconds;
		t.Elapsed -= TimerElapsed;
		t.Elapsed += TimerElapsed;
		t.Start();

		void TimerElapsed(object? s, System.Timers.ElapsedEventArgs args)
		{
			_debouncers.TryRemove(path, out _);
			_queue.Writer.TryWrite(path);
		}
	}

	private void OnRenamed(object sender, RenamedEventArgs e)
	{
		if (IgnorePath(e.OldFullPath) && IgnorePath(e.FullPath)) return;
		_ = Task.Run(async () => await HandleRenameAsync(e.OldFullPath, e.FullPath));
	}

	private void OnDeleted(object sender, FileSystemEventArgs e)
	{
		// Policy: ignore deletions for now.
		Console.WriteLine($"DELETE {Rel(e.FullPath)} (skipped by policy)");
	}

	private async Task ProcessQueueAsync(CancellationToken ct)
	{
		while (await _queue.Reader.WaitToReadAsync(ct))
		{
			while (_queue.Reader.TryRead(out var path))
			{
				try
				{
					await HandleChangeAsync(path, ct);
				}
				catch (Exception ex)
				{
					Console.WriteLine($"ERROR {Rel(path)} ({ex.Message})");
				}
			}
		}
	}

	private async Task HandleChangeAsync(string path, CancellationToken ct)
	{
		var rel = Rel(path);
		if (Directory.Exists(path))
		{
			// Directory changes don't trigger immediate remote ops; file pushes will ensure dir exists.
			Console.WriteLine($"SKIP  {rel} (directory change)");
			return;
		}

		if (!File.Exists(path))
		{
			// Might be a transient state; skip.
			Console.WriteLine($"SKIP  {rel} (file missing)");
			return;
		}

		var remoteFile = PathMapper.ToRemoteFilePath(_cfg.LocalRoot, path);
		var remoteDir = Path.GetDirectoryName(remoteFile)!.Replace('\\', '/');
		if (!remoteDir.EndsWith('/')) remoteDir += "/";
		var name = Path.GetFileName(path);

		var listing = await _client.GetDirectoryAsync(remoteDir, ct);
		FileEntry? remoteEntry = null;
		if (listing.IsSuccess && listing.Body != null)
		{
			remoteEntry = Array.Find(listing.Body.Files, f => string.Equals(f.Name, name, StringComparison.Ordinal));
		}

	var fi = new FileInfo(path);
		var localMTimeUtc = fi.LastWriteTimeUtc;
		var localSize = fi.Length;

		if (remoteEntry == null)
		{
			await EnsureRemoteDirsAsync(remoteFile, ct);
			var status = await _wc.PutFileAsync(remoteFile, await File.ReadAllBytesAsync(path, ct), new DateTimeOffset(localMTimeUtc), ct);
			Console.WriteLine($"PUSH  {rel} (reason: missing-remote) [{(int)status}]");
			return;
		}

		var remoteMs = remoteEntry.ModifiedNs / 1_000_000L;
		var remoteTime = DateTimeOffset.FromUnixTimeMilliseconds(remoteMs).UtcDateTime;
		var remoteSize = remoteEntry.FileSize;

		if (localMTimeUtc > remoteTime || localSize != remoteSize)
		{
			await EnsureRemoteDirsAsync(remoteFile, ct);
			var status = await _wc.PutFileAsync(remoteFile, await File.ReadAllBytesAsync(path, ct), new DateTimeOffset(localMTimeUtc), ct);
			Console.WriteLine($"PUSH  {rel} (reason: local-newer|size-diff) [{(int)status}]");
		}
		else if (remoteTime > localMTimeUtc)
		{
			var resp = await _client.GetFileAsync(remoteFile, ct);
			if (resp.IsSuccess && resp.Body != null)
			{
				Directory.CreateDirectory(Path.GetDirectoryName(path)!);
				await File.WriteAllBytesAsync(path, resp.Body, ct);
				FileTimes.SetFileMTimeFromNs(path, remoteEntry.ModifiedNs);
				SelfWriteRegistry.Register(path);
				Console.WriteLine($"PULL  {rel} (reason: remote-newer)");
			}
			else
			{
				Console.WriteLine($"ERROR {rel} (failed to pull: {(int)resp.StatusCode} {resp.StatusCode})");
			}
		}
		else
		{
			Console.WriteLine($"SKIP  {rel} (equal)");
		}
	}

	private async Task HandleRenameAsync(string oldPath, string newPath)
	{
		if (IgnorePath(newPath)) return;
		var isDir = Directory.Exists(newPath);
		var fromRemote = isDir
			? PathMapper.ToRemoteDirectoryPath(_cfg.LocalRoot, oldPath)
			: PathMapper.ToRemoteFilePath(_cfg.LocalRoot, oldPath);
		var toRemote = isDir
			? PathMapper.ToRemoteDirectoryPath(_cfg.LocalRoot, newPath)
			: PathMapper.ToRemoteFilePath(_cfg.LocalRoot, newPath);

		var status = await _wc.MoveAsync(fromRemote, toRemote, isDir, CancellationToken.None);
		if (status == HttpStatusCode.Created)
		{
			Console.WriteLine($"MOVE  {Rel(oldPath)} -> {Rel(newPath)} (reason: local-rename)");
			return;
		}

		if (!isDir && File.Exists(newPath))
		{
			await EnsureRemoteDirsAsync(toRemote, CancellationToken.None);
			var fi = new FileInfo(newPath);
			await _wc.PutFileAsync(toRemote, await File.ReadAllBytesAsync(newPath), new DateTimeOffset(fi.LastWriteTimeUtc), CancellationToken.None);
			await _wc.DeleteAsync(fromRemote, isDirectory: false, CancellationToken.None);
			Console.WriteLine($"MOVE  {Rel(oldPath)} -> {Rel(newPath)} (fallback PUT+DELETE)");
		}
		else
		{
			Console.WriteLine($"ERROR MOVE {Rel(oldPath)} -> {Rel(newPath)} (status {(int)status})");
		}
	}

	private async Task EnsureRemoteDirsAsync(string remoteFilePath, CancellationToken ct)
	{
		// remoteFilePath like /lib/hello/world.txt; ensure /lib/ and /lib/hello/
		var parts = remoteFilePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
		if (parts.Length <= 1) return; // at root
		var current = "/";
		for (int i = 0; i < parts.Length - 1; i++)
		{
			current += parts[i] + "/";
			await _wc.PutDirectoryAsync(current, timestamp: null, ct);
		}
	}

	private bool IgnorePath(string fullPath)
	{
		try
		{
			if (PathMapper.IsSymlink(fullPath)) return true;
			// Check ignore by names on each segment
			var rel = Rel(fullPath);
			var segments = rel.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
			foreach (var seg in segments)
			{
				if (Ignore.IsIgnored(seg)) return true;
			}
			return Ignore.IsIgnored(fullPath);
		}
		catch
		{
			return false;
		}
	}

	private string Rel(string fullPath)
	{
		var root = Path.GetFullPath(_cfg.LocalRoot).TrimEnd(Path.DirectorySeparatorChar);
		var fp = Path.GetFullPath(fullPath);
		if (fp.StartsWith(root, StringComparison.Ordinal))
		{
			return fp.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar);
		}
		return fullPath;
	}

	public void Dispose()
	{
		_fsw.Dispose();
		foreach (var (_, timer) in _debouncers)
		{
			timer.Dispose();
		}
	}
}
