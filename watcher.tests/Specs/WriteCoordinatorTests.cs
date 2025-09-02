using System.Net;
using Watcher.Config;
using Watcher.Http;
using Watcher.Sync;
using Watcher.Tests.Fakes;
using Xunit;
using AwesomeAssertions;

namespace Watcher.Tests.Specs;

public class WriteCoordinatorTests
{
    [Fact]
    public async Task Queues_On_409_Then_Resumes_When_Writable()
    {
        var cfg = new AppConfig
        {
            Address = "http://dev.local",
            Password = "pwd",
            WritablePollIntervalSeconds = 1
        };

        var handler = new FakeHttpMessageHandler();
        int putCount = 0;
        bool writable = false;

        handler.WhenPrefix(HttpMethod.Put, "/fs/", req =>
        {
            Interlocked.Increment(ref putCount);
            if (Volatile.Read(ref putCount) == 1)
            {
                return new HttpResponseMessage(HttpStatusCode.Conflict);
            }
            return new HttpResponseMessage(HttpStatusCode.Created);
        });

        // Custom responder that flips writability based on a flag
        handler.When(req => req.Method == HttpMethod.Get && req.RequestUri!.AbsolutePath == "/cp/diskinfo.json",
            _ => FakeHttpMessageHandler.CreateResponse(HttpStatusCode.OK, new[]
            {
                new Watcher.Remote.DiskInfo { Root = "/", Free = 0, Total = 0, BlockSize = 512, Writable = writable }
            })
        );

        using var client = new WebWorkflowClient(cfg, handler);
        var wc = new WriteCoordinator(cfg, client);

        var status = await wc.PutFileAsync("/file.txt", new byte[] { 1 }, DateTimeOffset.UtcNow, CancellationToken.None);
        status.Should().Be(HttpStatusCode.Conflict);

        // After a short delay, mark writable; monitor should resume and drain queued op
        _ = Task.Run(async () => { await Task.Delay(1200); writable = true; });

        await WaitUntilAsync(() => Volatile.Read(ref putCount) >= 2, TimeSpan.FromSeconds(5));
        putCount.Should().Be(2);
    }

    private static async Task WaitUntilAsync(Func<bool> predicate, TimeSpan timeout)
    {
        var start = DateTime.UtcNow;
        while (!predicate())
        {
            if (DateTime.UtcNow - start > timeout) throw new TimeoutException("Condition not met in time");
            await Task.Delay(50);
        }
    }
}
