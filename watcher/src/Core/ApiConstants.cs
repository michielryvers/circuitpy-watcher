namespace Watcher.Core;

public static class ApiConstants
{
    public const string FsBase = "/fs";
    public const string CpBase = "/cp";

    public const string AcceptJson = "application/json";
    public const string HeaderAccept = "Accept";
    public const string HeaderExpect = "Expect";
    public const string HeaderAuthorization = "Authorization";
    public const string HeaderXTimestamp = "X-Timestamp"; // ms since epoch
    public const string HeaderXDestination = "X-Destination"; // absolute /fs path

    // Candidate WebSocket Serial endpoints (exact path may vary by CP version)
    public static readonly string[] SerialWebSocketCandidates = new[]
    {
        "/cp/serial",
        "/cp/serial/websocket",
        "/serial",
        "/serial/websocket",
    };
}
