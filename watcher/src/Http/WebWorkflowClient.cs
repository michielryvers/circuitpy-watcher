using System;
using System.Buffers;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Polly;
using Polly.Retry;
using Watcher.Config;
using Watcher.Core;
using Watcher.Remote;

namespace Watcher.Http;

public sealed class WebWorkflowClient : IDisposable
{
	private readonly HttpClient _http;
	private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);
	private readonly AsyncRetryPolicy<HttpResponseMessage> _retry;
	private readonly Uri _baseUri;

	public WebWorkflowClient(AppConfig config)
	{
		config.Validate();
		_baseUri = new Uri($"http://{config.Address.TrimEnd('/')}\u002f");
		_http = new HttpClient { BaseAddress = _baseUri };
		var authValue = Convert.ToBase64String(Encoding.UTF8.GetBytes($":{config.Password}"));
		_http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authValue);

		_retry = Policy
			.Handle<HttpRequestException>()
			.OrResult<HttpResponseMessage>(r => (int)r.StatusCode >= 500)
			.WaitAndRetryAsync(3, attempt => TimeSpan.FromMilliseconds(500 * Math.Pow(2, attempt - 1)));
	}

	public void Dispose() => _http.Dispose();

	public async Task<ApiResult<VersionInfo>> GetVersionAsync(CancellationToken ct)
	{
		var resp = await _retry.ExecuteAsync(() => _http.GetAsync("/cp/version.json", ct));
		return await ReadJson<VersionInfo>(resp, ct);
	}

	public async Task<ApiResult<DiskInfo[]>> GetDiskInfoAsync(CancellationToken ct)
	{
		var resp = await _retry.ExecuteAsync(() => _http.GetAsync("/cp/diskinfo.json", ct));
		return await ReadJson<DiskInfo[]>(resp, ct);
	}

	public async Task<ApiResult<DirectoryListing>> GetDirectoryAsync(string dirPath, CancellationToken ct)
	{
		if (!dirPath.EndsWith('/')) dirPath += "/";
		var req = new HttpRequestMessage(HttpMethod.Get, $"{ApiConstants.FsBase}{dirPath}");
		req.Headers.TryAddWithoutValidation(ApiConstants.HeaderAccept, ApiConstants.AcceptJson);
		var resp = await _retry.ExecuteAsync(() => _http.SendAsync(req, ct));
		return await ReadJson<DirectoryListing>(resp, ct);
	}

	public async Task<ApiResult<byte[]>> GetFileAsync(string filePath, CancellationToken ct)
	{
		if (filePath.EndsWith('/')) throw new ArgumentException("File path must not end with /", nameof(filePath));
		var resp = await _retry.ExecuteAsync(() => _http.GetAsync($"{ApiConstants.FsBase}{filePath}", ct));
		var status = resp.StatusCode;
		if (!resp.IsSuccessStatusCode)
		{
			return new ApiResult<byte[]>(status, null, false, resp.ReasonPhrase);
		}
		var bytes = await resp.Content.ReadAsByteArrayAsync(ct);
		return new ApiResult<byte[]>(status, bytes, true);
	}

	public async Task<HttpStatusCode> PutDirectoryAsync(string dirPath, DateTimeOffset? timestamp, CancellationToken ct)
	{
		if (!dirPath.EndsWith('/')) dirPath += "/";
		var req = new HttpRequestMessage(HttpMethod.Put, $"{ApiConstants.FsBase}{dirPath}");
		if (timestamp.HasValue)
		{
			var ms = timestamp.Value.ToUnixTimeMilliseconds().ToString();
			req.Headers.TryAddWithoutValidation(ApiConstants.HeaderXTimestamp, ms);
		}
		var resp = await _retry.ExecuteAsync(() => _http.SendAsync(req, ct));
		return resp.StatusCode;
	}

	public async Task<HttpStatusCode> PutFileAsync(string filePath, ReadOnlyMemory<byte> content, DateTimeOffset? timestamp, CancellationToken ct)
	{
		if (filePath.EndsWith('/')) throw new ArgumentException("File path must not end with /", nameof(filePath));
		var req = new HttpRequestMessage(HttpMethod.Put, $"{ApiConstants.FsBase}{filePath}");
		req.Headers.ExpectContinue = true; // Expect: 100-continue
		if (timestamp.HasValue)
		{
			var ms = timestamp.Value.ToUnixTimeMilliseconds().ToString();
			req.Headers.TryAddWithoutValidation(ApiConstants.HeaderXTimestamp, ms);
		}
		req.Content = new ByteArrayContent(content.ToArray());
		var resp = await _retry.ExecuteAsync(() => _http.SendAsync(req, ct));
		return resp.StatusCode;
	}

	public async Task<HttpStatusCode> MoveAsync(string sourcePath, string destinationPath, bool isDirectory, CancellationToken ct)
	{
		// Ensure trailing slash on directories
		if (isDirectory)
		{
			if (!sourcePath.EndsWith('/')) sourcePath += "/";
			if (!destinationPath.EndsWith('/')) destinationPath += "/";
		}
		var method = new HttpMethod("MOVE");
		var req = new HttpRequestMessage(method, $"{ApiConstants.FsBase}{sourcePath}");
		req.Headers.TryAddWithoutValidation(ApiConstants.HeaderXDestination, $"{ApiConstants.FsBase}{destinationPath}");
		var resp = await _retry.ExecuteAsync(() => _http.SendAsync(req, ct));
		return resp.StatusCode;
	}

	public async Task<HttpStatusCode> DeleteAsync(string path, bool isDirectory, CancellationToken ct)
	{
		if (isDirectory && !path.EndsWith('/')) path += "/";
		var req = new HttpRequestMessage(HttpMethod.Delete, $"{ApiConstants.FsBase}{path}");
		var resp = await _retry.ExecuteAsync(() => _http.SendAsync(req, ct));
		return resp.StatusCode;
	}

	private async Task<ApiResult<T>> ReadJson<T>(HttpResponseMessage resp, CancellationToken ct)
	{
		var status = resp.StatusCode;
		if (!resp.IsSuccessStatusCode)
		{
			return new ApiResult<T>(status, default, false, resp.ReasonPhrase);
		}
		await using var s = await resp.Content.ReadAsStreamAsync(ct);
		var body = await JsonSerializer.DeserializeAsync<T>(s, _json, ct);
		return new ApiResult<T>(status, body, true);
	}
}
