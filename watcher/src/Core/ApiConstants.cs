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
}
