using System.Net.WebSockets;
using System.Threading.Channels;

namespace Watcher.Tests.Fakes;

public sealed class FakeWebSocket : Watcher.Serial.IClientWebSocket, IDisposable
{
    private readonly Channel<ArraySegment<byte>> _incoming = Channel.CreateUnbounded<ArraySegment<byte>>();
    private readonly Channel<ArraySegment<byte>> _sent = Channel.CreateUnbounded<ArraySegment<byte>>();
    private volatile WebSocketState _state = WebSocketState.None;

    public WebSocketState State => _state;
    public WebSocketCloseStatus? CloseStatus => null;

    public List<byte[]> SentMessages { get; } = new();

    public Task ConnectAsync(Uri uri, CancellationToken ct)
    {
        _state = WebSocketState.Open;
        return Task.CompletedTask;
    }

    public void EnqueueIncoming(byte[] data)
    {
        _incoming.Writer.TryWrite(new ArraySegment<byte>(data));
    }

    public async Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken ct)
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

    public async Task SendAsync(ReadOnlyMemory<byte> payload, WebSocketMessageType messageType, bool endOfMessage, CancellationToken ct)
    {
        var bytes = payload.ToArray();
        SentMessages.Add(bytes);
        await _sent.Writer.WriteAsync(new ArraySegment<byte>(bytes), ct);
    }

    public void Dispose()
    {
        _state = WebSocketState.Closed;
    }
}

public sealed class FakeWebSocketFactory : Watcher.Serial.IClientWebSocketFactory
{
    public FakeWebSocket Instance { get; } = new FakeWebSocket();
    public Watcher.Serial.IClientWebSocket Create() => Instance;
}

