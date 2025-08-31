using System;
using System.IO;
using System.Net;
using System.Text;
using Watcher.Config;
using Watcher.Core;
using Watcher.Http;

namespace Watcher.Sync;

public sealed class FullPuller
{
	private readonly AppConfig _cfg;
	private readonly WebWorkflowClient _client;

	public FullPuller(AppConfig cfg, WebWorkflowClient client)
	{
		_cfg = cfg;
		_client = client;
	}

	public async Task RunAsync(CancellationToken ct)
	{
		// Ensure local root exists (caller may have deleted before calling us)
		Directory.CreateDirectory(_cfg.LocalRoot);
		await PullDirectoryAsync("/", _cfg.LocalRoot, ct);
	}

	private async Task PullDirectoryAsync(string remoteDir, string localDir, CancellationToken ct)
	{
		// Normalize trailing slash
		if (!remoteDir.EndsWith('/')) remoteDir += "/";
		Directory.CreateDirectory(localDir);

		var listing = await _client.GetDirectoryAsync(remoteDir, ct);
		if (!listing.IsSuccess || listing.Body is null)
		{
			throw new InvalidOperationException($"Failed to list {remoteDir}: {listing.StatusCode}");
		}

		foreach (var entry in listing.Body.Files)
		{
			var remotePath = remoteDir + entry.Name + (entry.IsDirectory ? "/" : string.Empty);
			var localPath = Path.Combine(localDir, entry.Name);

			if (entry.IsDirectory)
			{
				await PullDirectoryAsync(remotePath, localPath, ct);
				// Directories in API do not give modified_ns per-dir reliably; skip setting for dirs
				continue;
			}

			// File
			var fileResp = await _client.GetFileAsync(remoteDir + entry.Name, ct);
			if (!fileResp.IsSuccess || fileResp.Body is null)
			{
				if (fileResp.StatusCode == HttpStatusCode.NotFound)
					continue; // disappeared during traversal; skip
				throw new InvalidOperationException($"Failed to get file {remotePath}: {fileResp.StatusCode}");
			}

			Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
			await File.WriteAllBytesAsync(localPath, fileResp.Body, ct);
			FileTimes.SetFileMTimeFromNs(localPath, entry.ModifiedNs);
		}
	}
}
