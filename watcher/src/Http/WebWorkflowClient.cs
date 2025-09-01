using System.Net;
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
        // Normalize address: allow with or without scheme, ensure trailing slash
        var addr = config.Address.Trim();
        if (
            !addr.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            && !addr.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
        )
        {
            addr = "http://" + addr;
        }
        var uri = new Uri(addr, UriKind.Absolute);
        var baseStr = uri.AbsoluteUri.EndsWith("/") ? uri.AbsoluteUri : uri.AbsoluteUri + "/";
        _baseUri = new Uri(baseStr, UriKind.Absolute);
        _http = new HttpClient { BaseAddress = _baseUri };
        var authValue = Convert.ToBase64String(Encoding.UTF8.GetBytes($":{config.Password}"));
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic",
            authValue
        );

        _retry = Policy
            .Handle<HttpRequestException>()
            .OrResult<HttpResponseMessage>(r => (int)r.StatusCode >= 500)
            .WaitAndRetryAsync(
                3,
                attempt => TimeSpan.FromMilliseconds(500 * Math.Pow(2, attempt - 1))
            );
    }

    public void Dispose() => _http.Dispose();

    public async Task<ApiResult<VersionInfo>> GetVersionAsync(CancellationToken ct)
    {
        try
        {
            var resp = await _retry.ExecuteAsync(() => _http.GetAsync("/cp/version.json", ct));
            return await ReadJson<VersionInfo>(resp, ct);
        }
        catch (HttpRequestException hre)
        {
            return new ApiResult<VersionInfo>(
                HttpStatusCode.ServiceUnavailable,
                default,
                false,
                hre.Message
            );
        }
        catch (OperationCanceledException oce) when (!ct.IsCancellationRequested)
        {
            return new ApiResult<VersionInfo>(
                HttpStatusCode.RequestTimeout,
                default,
                false,
                oce.Message
            );
        }
    }

    public async Task<ApiResult<DiskInfo[]>> GetDiskInfoAsync(CancellationToken ct)
    {
        try
        {
            var resp = await _retry.ExecuteAsync(() => _http.GetAsync("/cp/diskinfo.json", ct));
            return await ReadJson<DiskInfo[]>(resp, ct);
        }
        catch (HttpRequestException hre)
        {
            return new ApiResult<DiskInfo[]>(
                HttpStatusCode.ServiceUnavailable,
                default,
                false,
                hre.Message
            );
        }
        catch (OperationCanceledException oce) when (!ct.IsCancellationRequested)
        {
            return new ApiResult<DiskInfo[]>(
                HttpStatusCode.RequestTimeout,
                default,
                false,
                oce.Message
            );
        }
    }

    public async Task<ApiResult<DirectoryListing>> GetDirectoryAsync(
        string dirPath,
        CancellationToken ct
    )
    {
        if (!dirPath.EndsWith('/'))
            dirPath += "/";
        try
        {
            var req = new HttpRequestMessage(HttpMethod.Get, $"{ApiConstants.FsBase}{dirPath}");
            req.Headers.TryAddWithoutValidation(ApiConstants.HeaderAccept, ApiConstants.AcceptJson);
            var resp = await _retry.ExecuteAsync(() => _http.SendAsync(req, ct));
            return await ReadJson<DirectoryListing>(resp, ct);
        }
        catch (HttpRequestException hre)
        {
            return new ApiResult<DirectoryListing>(
                HttpStatusCode.ServiceUnavailable,
                default,
                false,
                hre.Message
            );
        }
        catch (OperationCanceledException oce) when (!ct.IsCancellationRequested)
        {
            return new ApiResult<DirectoryListing>(
                HttpStatusCode.RequestTimeout,
                default,
                false,
                oce.Message
            );
        }
    }

    public async Task<ApiResult<byte[]>> GetFileAsync(string filePath, CancellationToken ct)
    {
        if (filePath.EndsWith('/'))
            throw new ArgumentException("File path must not end with /", nameof(filePath));
        try
        {
            var resp = await _retry.ExecuteAsync(() =>
                _http.GetAsync($"{ApiConstants.FsBase}{filePath}", ct)
            );
            var status = resp.StatusCode;
            if (!resp.IsSuccessStatusCode)
            {
                return new ApiResult<byte[]>(status, null, false, resp.ReasonPhrase);
            }
            var bytes = await resp.Content.ReadAsByteArrayAsync(ct);
            return new ApiResult<byte[]>(status, bytes, true);
        }
        catch (HttpRequestException hre)
        {
            return new ApiResult<byte[]>(
                HttpStatusCode.ServiceUnavailable,
                null,
                false,
                hre.Message
            );
        }
        catch (OperationCanceledException oce) when (!ct.IsCancellationRequested)
        {
            return new ApiResult<byte[]>(HttpStatusCode.RequestTimeout, null, false, oce.Message);
        }
    }

    public async Task<HttpStatusCode> PutDirectoryAsync(
        string dirPath,
        DateTimeOffset? timestamp,
        CancellationToken ct
    )
    {
        if (!dirPath.EndsWith('/'))
            dirPath += "/";
        var req = new HttpRequestMessage(HttpMethod.Put, $"{ApiConstants.FsBase}{dirPath}");
        if (timestamp.HasValue)
        {
            var ms = timestamp.Value.ToUnixTimeMilliseconds().ToString();
            req.Headers.TryAddWithoutValidation(ApiConstants.HeaderXTimestamp, ms);
        }
        var resp = await _retry.ExecuteAsync(() => _http.SendAsync(req, ct));
        return resp.StatusCode;
    }

    public async Task<HttpStatusCode> PutFileAsync(
        string filePath,
        ReadOnlyMemory<byte> content,
        DateTimeOffset? timestamp,
        CancellationToken ct
    )
    {
        if (filePath.EndsWith('/'))
            throw new ArgumentException("File path must not end with /", nameof(filePath));
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

    public async Task<HttpStatusCode> MoveAsync(
        string sourcePath,
        string destinationPath,
        bool isDirectory,
        CancellationToken ct
    )
    {
        // Ensure trailing slash on directories
        if (isDirectory)
        {
            if (!sourcePath.EndsWith('/'))
                sourcePath += "/";
            if (!destinationPath.EndsWith('/'))
                destinationPath += "/";
        }
        var method = new HttpMethod("MOVE");
        var req = new HttpRequestMessage(method, $"{ApiConstants.FsBase}{sourcePath}");
        req.Headers.TryAddWithoutValidation(
            ApiConstants.HeaderXDestination,
            $"{ApiConstants.FsBase}{destinationPath}"
        );
        var resp = await _retry.ExecuteAsync(() => _http.SendAsync(req, ct));
        return resp.StatusCode;
    }

    public async Task<HttpStatusCode> DeleteAsync(
        string path,
        bool isDirectory,
        CancellationToken ct
    )
    {
        if (isDirectory && !path.EndsWith('/'))
            path += "/";
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
