using System.Net;
using System.Text;
using System.Text.Json;
using Watcher.Remote;

namespace Watcher.Http;

internal sealed class MockHttpHandler : HttpMessageHandler
{
    private readonly byte[] _codeBytes;
    private readonly long _modifiedNs;

    public MockHttpHandler()
    {
        _codeBytes = Encoding.UTF8.GetBytes("print('hello from mock')\n");
        _modifiedNs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1_000_000L;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        var path = request.RequestUri?.AbsolutePath ?? string.Empty;
        var method = request.Method.Method.ToUpperInvariant();

        if (method == "GET" && path == "/cp/version.json")
        {
            return Json(
                new VersionInfo
                {
                    WebApiVersion = 4,
                    Hostname = "mock-device",
                    Version = "9.x",
                }
            );
        }

        if (method == "GET" && path == "/cp/diskinfo.json")
        {
            var disks = new[]
            {
                new DiskInfo
                {
                    Root = "/",
                    Free = 1024,
                    Total = 2048,
                    BlockSize = 512,
                    Writable = true,
                },
            };
            return Json(disks);
        }

        if (method == "GET" && (path == "/fs" || path == "/fs/"))
        {
            var listing = new DirectoryListing
            {
                Free = 1024,
                Total = 2048,
                BlockSize = 512,
                Writable = true,
                Files = new[]
                {
                    new FileEntry
                    {
                        Name = "code.py",
                        IsDirectory = false,
                        ModifiedNs = _modifiedNs,
                        FileSize = _codeBytes.Length,
                    },
                },
            };
            return Json(listing);
        }

        if (method == "GET" && path == "/fs/code.py")
        {
            var resp = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(_codeBytes),
            };
            return Task.FromResult(resp);
        }

        if (method == "PUT" && path.StartsWith("/fs/"))
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created));
        }

        if (method == "MOVE" && path.StartsWith("/fs/"))
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created));
        }

        if (method == "DELETE" && path.StartsWith("/fs/"))
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    private static Task<HttpResponseMessage> Json<T>(T body)
    {
        var ti = (System.Text.Json.Serialization.Metadata.JsonTypeInfo<T>?)
            AppJsonContext.Default.GetTypeInfo(typeof(T));
        var json = ti is null
            ? JsonSerializer.Serialize(
                body,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)
                {
                    TypeInfoResolver = AppJsonContext.Default,
                }
            )
            : JsonSerializer.Serialize(body, ti);
        var resp = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        return Task.FromResult(resp);
    }
}
