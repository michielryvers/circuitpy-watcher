using System.Text;
using Watcher.Config;
using Watcher.Serial;
using Watcher.Tests.Fakes;
using Xunit;
using AwesomeAssertions;

namespace Watcher.Tests.Specs;

public class SerialWebSocketTests
{
    [Fact]
    public async Task Receives_Data_And_Sends_Bytes()
    {
        var cfg = new AppConfig { Address = "http://dev.local", Password = "pwd" };
        var factory = new FakeWebSocketFactory();
        var client = new SerialWebSocketClient(cfg, factory, new [] { "/cp/serial" });

        var received = new List<string>();
        client.DataReceived += data => received.Add(Encoding.UTF8.GetString(data.Span));

        using var cts = new CancellationTokenSource();
        var runTask = client.RunAsync(cts.Token);

        // Simulate prompt
        factory.Instance.EnqueueIncoming(Encoding.UTF8.GetBytes(">>> "));
        await Task.Delay(50);

        // Send a char
        await client.SendAsync(Encoding.UTF8.GetBytes("a"), CancellationToken.None);
        await Task.Delay(20);

        cts.Cancel();
        try { await runTask; } catch { }

        received.Should().Contain(r => r.Contains(">>> "));
        factory.Instance.SentMessages.Should().NotBeEmpty();
        Encoding.UTF8.GetString(factory.Instance.SentMessages.Last()).Should().Be("a");
    }
}

