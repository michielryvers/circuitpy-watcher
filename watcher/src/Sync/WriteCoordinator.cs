using System;
using System.Collections.Concurrent;
using System.Net;
using System.Threading.Channels;
using Watcher.Config;
using Watcher.Http;

namespace Watcher.Sync;

public sealed class WriteCoordinator
{
	private readonly AppConfig _cfg;
	private readonly WebWorkflowClient _client;
	private readonly Channel<Func<CancellationToken, Task<HttpStatusCode>>> _queue = Channel.CreateUnbounded<Func<CancellationToken, Task<HttpStatusCode>>>();
	private readonly object _stateLock = new();
	private bool _paused;
	private Task? _monitorTask;

	public WriteCoordinator(AppConfig cfg, WebWorkflowClient client)
	{
		_cfg = cfg;
		_client = client;
	}

	public async Task<HttpStatusCode> PutDirectoryAsync(string dirPath, DateTimeOffset? timestamp, CancellationToken ct)
	{
		return await ExecuteOrQueueAsync(ct, () => _client.PutDirectoryAsync(dirPath, timestamp, ct));
	}

	public async Task<HttpStatusCode> PutFileAsync(string filePath, byte[] content, DateTimeOffset? timestamp, CancellationToken ct)
	{
		return await ExecuteOrQueueAsync(ct, () => _client.PutFileAsync(filePath, content, timestamp, ct));
	}

	public async Task<HttpStatusCode> MoveAsync(string sourcePath, string destinationPath, bool isDirectory, CancellationToken ct)
	{
		return await ExecuteOrQueueAsync(ct, () => _client.MoveAsync(sourcePath, destinationPath, isDirectory, ct));
	}

	public async Task<HttpStatusCode> DeleteAsync(string path, bool isDirectory, CancellationToken ct)
	{
		return await ExecuteOrQueueAsync(ct, () => _client.DeleteAsync(path, isDirectory, ct));
	}

	private async Task<HttpStatusCode> ExecuteOrQueueAsync(CancellationToken ct, Func<Task<HttpStatusCode>> op)
	{
		if (IsPaused)
		{
			await EnqueueAsync(op);
			return HttpStatusCode.Accepted;
		}

		var status = await op();
		if (status == HttpStatusCode.Conflict)
		{
			EnterPaused();
			await EnqueueAsync(op);
		}
		return status;
	}

	private bool IsPaused
	{
		get { lock (_stateLock) return _paused; }
	}

	private void EnterPaused()
	{
		lock (_stateLock)
		{
			if (_paused) return;
			_paused = true;
			Console.WriteLine("PAUSE (USB MSC active) – waiting for writable…");
			EnsureMonitor();
		}
	}

	private void ExitPaused()
	{
		lock (_stateLock)
		{
			if (!_paused) return;
			_paused = false;
		}
		Console.WriteLine("RESUME (writable)");
	}

	private void EnsureMonitor()
	{
		if (_monitorTask != null && !_monitorTask.IsCompleted) return;
		_monitorTask = Task.Run(async () => await MonitorAsync());
	}

	private async Task MonitorAsync()
	{
		while (IsPaused)
		{
			try
			{
				var disks = await _client.GetDiskInfoAsync(CancellationToken.None);
				if (disks.IsSuccess && disks.Body != null)
				{
					var writable = Array.Exists(disks.Body, d => d.Writable);
					if (writable)
					{
						ExitPaused();
						await DrainQueueAsync();
						break;
					}
				}
			}
			catch
			{
				// ignore and keep polling
			}
			await Task.Delay(TimeSpan.FromSeconds(_cfg.WritablePollIntervalSeconds));
		}
	}

	private async Task EnqueueAsync(Func<Task<HttpStatusCode>> op)
	{
		await _queue.Writer.WriteAsync(async ct => await op());
	}

	private async Task DrainQueueAsync()
	{
		while (_queue.Reader.TryRead(out var work))
		{
			var status = await work(CancellationToken.None);
			if (status == HttpStatusCode.Conflict)
			{
				EnterPaused();
				return; // will resume later
			}
		}
	}
}
