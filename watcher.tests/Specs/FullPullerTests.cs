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

public class FullPullerTests
{
    [Fact]
    public async Task FullPuller_Populates_LocalTree_And_Sets_MTimes()
    {
        using var tmp = new TempDir();
        var cfg = new AppConfig
        {
            Address = "http://dev.local",
            Password = "pwd",
            LocalRoot = System.IO.Path.Combine(tmp.Path, "CIRCUITPYTHON")
        };

        var now = DateTimeOffset.UtcNow;
        long ns1 = now.AddMinutes(-10).ToUnixTimeMilliseconds() * 1_000_000L;
        long ns2 = now.AddMinutes(-5).ToUnixTimeMilliseconds() * 1_000_000L;

        var handler = new FakeHttpMessageHandler();
        // Root listing
        handler.When(HttpMethod.Get, "/fs/", HttpStatusCode.OK, new DirectoryListing
        {
            Free = 0, Total = 0, BlockSize = 512, Writable = true,
            Files = new [] {
                new FileEntry { Name = "code.py", IsDirectory = false, ModifiedNs = ns1, FileSize = 14 },
                new FileEntry { Name = "lib", IsDirectory = true, ModifiedNs = ns2, FileSize = 0 },
            }
        });
        // lib listing
        handler.When(HttpMethod.Get, "/fs/lib/", HttpStatusCode.OK, new DirectoryListing
        {
            Free = 0, Total = 0, BlockSize = 512, Writable = true,
            Files = new [] {
                new FileEntry { Name = "mod.py", IsDirectory = false, ModifiedNs = ns2, FileSize = 6 },
            }
        });
        // file bytes
        handler.WhenPrefix(HttpMethod.Get, "/fs/code.py", req => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(Encoding.UTF8.GetBytes("print('hi')\n"))
        });
        handler.WhenPrefix(HttpMethod.Get, "/fs/lib/mod.py", req => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(Encoding.UTF8.GetBytes("x=1\n"))
        });

        using var client = new WebWorkflowClient(cfg, handler);
        var puller = new FullPuller(cfg, client);
        await puller.RunAsync(CancellationToken.None);

        var codePath = System.IO.Path.Combine(cfg.LocalRoot, "code.py");
        var modPath = System.IO.Path.Combine(cfg.LocalRoot, "lib", "mod.py");

        File.Exists(codePath).Should().BeTrue();
        File.Exists(modPath).Should().BeTrue();

        var codeTime = File.GetLastWriteTimeUtc(codePath);
        var modTime = File.GetLastWriteTimeUtc(modPath);
        var exp1 = DateTimeOffset.FromUnixTimeMilliseconds(ns1 / 1_000_000L).UtcDateTime;
        var exp2 = DateTimeOffset.FromUnixTimeMilliseconds(ns2 / 1_000_000L).UtcDateTime;

        (codeTime - exp1).Duration().Should().BeLessThan(TimeSpan.FromSeconds(2));
        (modTime - exp2).Duration().Should().BeLessThan(TimeSpan.FromSeconds(2));
    }
}
