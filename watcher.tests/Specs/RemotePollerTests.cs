using System.Net;
using System.Text;
using Watcher.Config;
using Watcher.Http;
using Watcher.Remote;
using Watcher.Sync;
using Watcher.Tests.Fakes;
using Watcher.Tests.Helpers;
using Xunit;
using AwesomeAssertions;

namespace Watcher.Tests.Specs;

public class RemotePollerTests
{
    [Fact]
    public async Task Pulls_When_Remote_Newer()
    {
        using var tmp = new TempDir();
        var root = System.IO.Path.Combine(tmp.Path, "CIRCUITPYTHON");
        Directory.CreateDirectory(root);
        var file = System.IO.Path.Combine(root, "code.py");
        await File.WriteAllTextAsync(file, "old\n");
        File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddMinutes(-30));

        var cfg = new AppConfig
        {
            Address = "http://dev.local",
            Password = "pwd",
            LocalRoot = root,
            RemotePollIntervalSeconds = 1
        };

        var ns = DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeMilliseconds() * 1_000_000L;
        var handler = new FakeHttpMessageHandler();
        handler.When(HttpMethod.Get, "/fs/", HttpStatusCode.OK, new DirectoryListing
        {
            Free = 0, Total = 0, BlockSize = 512, Writable = true,
            Files = new [] {
                new FileEntry { Name = "code.py", IsDirectory = false, ModifiedNs = ns, FileSize = 8 }
            }
        });
        handler.WhenPrefix(HttpMethod.Get, "/fs/code.py", _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(Encoding.UTF8.GetBytes("new-data\n"))
        });

        using var client = new WebWorkflowClient(cfg, handler);
        var poller = new RemotePoller(cfg, client);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var task = poller.RunAsync(cts.Token);
        try { await task; } catch { }

        var content = await File.ReadAllTextAsync(file);
        content.Should().Be("new-data\n");
        var newTime = File.GetLastWriteTimeUtc(file);
        var exp = DateTimeOffset.FromUnixTimeMilliseconds(ns / 1_000_000L).UtcDateTime;
        (newTime - exp).Duration().Should().BeLessThan(TimeSpan.FromSeconds(2));
    }
}

