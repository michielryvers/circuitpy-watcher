using System.Net;
using System.Text.Json;
using Watcher.Config;
using Watcher.Http;
using Watcher.Remote;
using Watcher.Tests.Fakes;
using Xunit;
using AwesomeAssertions;

namespace Watcher.Tests.Specs;

public class ApiClientTests
{
    [Fact]
    public async Task Version_Parses_Response()
    {
        var handler = new FakeHttpMessageHandler();
        var addr = "http://device.local";
        handler.When(HttpMethod.Get, "/cp/version.json", HttpStatusCode.OK, new
        {
            web_api_version = 4,
            hostname = "cpx",
            version = "9.2.0"
        });
        var cfg = new AppConfig { Address = addr, Password = "pwd" };
        using var client = new WebWorkflowClient(cfg, handler);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var result = await client.GetVersionAsync(cts.Token);

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(HttpStatusCode.OK);
        result.Body.Should().NotBeNull();
        result.Body!.WebApiVersion.Should().Be(4);
        result.Body!.Hostname.Should().Be("cpx");
    }
}
