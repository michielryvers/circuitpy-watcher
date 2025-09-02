using System.Net.WebSockets;
using System.Threading.Channels;

namespace Watcher.Serial;

internal sealed class MockWebSocket : IClientWebSocket, IDisposable
{
    private readonly Channel<ArraySegment<byte>> _incoming = Channel.CreateUnbounded<
        ArraySegment<byte>
    >();
    private volatile WebSocketState _state = WebSocketState.None;

    public WebSocketState State => _state;
    public WebSocketCloseStatus? CloseStatus => null;

    public Task ConnectAsync(Uri uri, CancellationToken ct)
    {
        _state = WebSocketState.Open;
        // Initial prompt for UX
        _incoming.Writer.TryWrite(
            new ArraySegment<byte>(System.Text.Encoding.UTF8.GetBytes(">>> "))
        );
        return Task.CompletedTask;
    }

    public async Task<WebSocketReceiveResult> ReceiveAsync(
        ArraySegment<byte> buffer,
        CancellationToken ct
    )
    {
        if (!await _incoming.Reader.WaitToReadAsync(ct))
        {
            return new WebSocketReceiveResult(0, WebSocketMessageType.Close, true);
        }
        if (_incoming.Reader.TryRead(out var segment))
        {
            var len = Math.Min(buffer.Count, segment.Count);
            segment.Slice(0, len).CopyTo(buffer);
            return new WebSocketReceiveResult(len, WebSocketMessageType.Text, true);
        }
        return new WebSocketReceiveResult(0, WebSocketMessageType.Close, true);
    }

    public Task SendAsync(
        ReadOnlyMemory<byte> payload,
        WebSocketMessageType messageType,
        bool endOfMessage,
        CancellationToken ct
    )
    {
        // Every send triggers a "Hello" response
        var hello = System.Text.Encoding.UTF8.GetBytes("Hello\n");
        _incoming.Writer.TryWrite(new ArraySegment<byte>(hello));
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _state = WebSocketState.Closed;
        _incoming.Writer.TryComplete();
    }
}

internal sealed class MockWebSocketFactory : IClientWebSocketFactory
{
    public IClientWebSocket Create() => new MockWebSocket();
}
