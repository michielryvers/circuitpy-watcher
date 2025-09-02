using System.Net.WebSockets;
using System.Text;
using Watcher.Config;

namespace Watcher.Serial;

public interface IClientWebSocket
{
    WebSocketState State { get; }
    WebSocketCloseStatus? CloseStatus { get; }
    Task ConnectAsync(Uri uri, CancellationToken ct);
    Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken ct);
    Task SendAsync(
        ReadOnlyMemory<byte> payload,
        WebSocketMessageType messageType,
        bool endOfMessage,
        CancellationToken ct
    );
    void Dispose();
}

public interface IClientWebSocketFactory
{
    IClientWebSocket Create();
}

internal sealed class DefaultWebSocket : IClientWebSocket, IDisposable
{
    private readonly ClientWebSocket _ws;

    public DefaultWebSocket(string? authHeader)
    {
        _ws = new ClientWebSocket();
        if (!string.IsNullOrEmpty(authHeader))
        {
            _ws.Options.SetRequestHeader("Authorization", authHeader);
        }
    }

    public WebSocketState State => _ws.State;
    public WebSocketCloseStatus? CloseStatus => _ws.CloseStatus;

    public Task ConnectAsync(Uri uri, CancellationToken ct) => _ws.ConnectAsync(uri, ct);

    public Task<WebSocketReceiveResult> ReceiveAsync(
        ArraySegment<byte> buffer,
        CancellationToken ct
    ) => _ws.ReceiveAsync(buffer, ct);

    public Task SendAsync(
        ReadOnlyMemory<byte> payload,
        WebSocketMessageType messageType,
        bool endOfMessage,
        CancellationToken ct
    ) => _ws.SendAsync(payload, messageType, endOfMessage, ct).AsTask();

    public void Dispose() => _ws.Dispose();
}

internal sealed class DefaultWebSocketFactory : IClientWebSocketFactory
{
    private readonly string? _authHeader;

    public DefaultWebSocketFactory(string? authHeader)
    {
        _authHeader = authHeader;
    }

    public IClientWebSocket Create() => new DefaultWebSocket(_authHeader);
}

public sealed class SerialWebSocketClient : IAsyncDisposable
{
    private readonly AppConfig _cfg;
    private IClientWebSocket? _ws;
    private readonly Uri[] _candidates;
    private readonly IClientWebSocketFactory _factory;

    public event Action<ReadOnlyMemory<byte>>? DataReceived;
    public event Action<string>? StateChanged;

    public SerialWebSocketClient(
        AppConfig cfg,
        IClientWebSocketFactory? factory = null,
        IEnumerable<string>? candidatePathsOverride = null
    )
    {
        _cfg = cfg;
        var authHeader =
            $"Basic {Convert.ToBase64String(Encoding.UTF8.GetBytes($":{cfg.Password}"))}";
        _factory = factory ?? new DefaultWebSocketFactory(authHeader);
        _candidates = BuildCandidates(cfg.Address, candidatePathsOverride);
    }

    public async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await ConnectAsync(ct);
                await ReceiveLoopAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                StateChanged?.Invoke($"ws error: {ex.Message}");
                await Task.Delay(1000, ct);
            }
        }
    }

    public async Task SendAsync(ReadOnlyMemory<byte> payload, CancellationToken ct)
    {
        var ws = _ws;
        if (ws == null || ws.State != WebSocketState.Open)
            return;
        await ws.SendAsync(payload, WebSocketMessageType.Binary, true, ct);
    }

    private async Task ConnectAsync(CancellationToken ct)
    {
        DisposeSocket();
        var lastEx = default(Exception);
        foreach (var uri in _candidates)
        {
            try
            {
                var ws = _factory.Create();
                await ws.ConnectAsync(uri, ct);
                _ws = ws;
                StateChanged?.Invoke($"connected {uri}");
                return;
            }
            catch (Exception ex)
            {
                lastEx = ex;
                StateChanged?.Invoke($"connect failed {uri} ({ex.Message})");
            }
        }
        throw lastEx ?? new InvalidOperationException("No serial WebSocket endpoint reachable");
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var ws = _ws!;
        var buffer = new byte[4096];
        while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                StateChanged?.Invoke("closed by remote");
                break;
            }
            if (result.Count > 0)
            {
                DataReceived?.Invoke(new ReadOnlyMemory<byte>(buffer, 0, result.Count));
            }
        }
    }

    private static Uri[] BuildCandidates(string address, IEnumerable<string>? candidatePaths)
    {
        var addr = address.Trim();
        if (
            !addr.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            && !addr.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
        )
        {
            addr = "http://" + addr;
        }
        var baseUri = new Uri(addr, UriKind.Absolute);
        var scheme = baseUri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase)
            ? "wss"
            : "ws";
        var builder = new UriBuilder(baseUri) { Scheme = scheme };

        var paths =
            candidatePaths?.ToArray() ?? Watcher.Core.ApiConstants.SerialWebSocketCandidates;
        return paths
            .Select(p =>
            {
                builder.Path = p.TrimStart('/');
                return builder.Uri;
            })
            .ToArray();
    }

    private void DisposeSocket()
    {
        try
        {
            _ws?.Dispose();
        }
        catch { }
        _ws = null;
    }

    public ValueTask DisposeAsync()
    {
        DisposeSocket();
        return ValueTask.CompletedTask;
    }
}
