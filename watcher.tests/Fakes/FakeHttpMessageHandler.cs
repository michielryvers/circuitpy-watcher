using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace Watcher.Tests.Fakes;

public sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    public delegate HttpResponseMessage Responder(HttpRequestMessage req);

    private readonly List<(Func<HttpRequestMessage, bool> Match, Responder Respond)> _routes = new();

    public void When(Func<HttpRequestMessage, bool> match, Responder responder) => _routes.Add((match, responder));

    public void When(HttpMethod method, string path, HttpStatusCode status, object? jsonBody = null, string? contentType = null)
    {
        When(
            r => r.Method == method && r.RequestUri != null && r.RequestUri.AbsolutePath == path,
            _ => CreateResponse(status, jsonBody, contentType)
        );
    }

    public void WhenPrefix(HttpMethod method, string prefix, Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        When(
            r => r.Method == method && r.RequestUri != null && r.RequestUri.AbsolutePath.StartsWith(prefix, StringComparison.Ordinal),
            new Responder(responder)
        );
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        foreach (var (match, responder) in _routes)
        {
            if (match(request))
            {
                return Task.FromResult(responder(request));
            }
        }
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound) { RequestMessage = request });
    }

    public static HttpResponseMessage CreateResponse(HttpStatusCode status, object? jsonBody, string? contentType = null)
    {
        var resp = new HttpResponseMessage(status);
        if (jsonBody != null)
        {
            var json = JsonSerializer.Serialize(jsonBody, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            resp.Content = new StringContent(json, Encoding.UTF8, contentType ?? "application/json");
        }
        else
        {
            resp.Content = new StringContent(string.Empty);
        }
        return resp;
    }
}

