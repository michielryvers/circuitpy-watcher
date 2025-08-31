using System.Net;

namespace Watcher.Http;

public sealed class ApiResult<T>
{
	public HttpStatusCode StatusCode { get; }
	public T? Body { get; }
	public bool IsSuccess { get; }
	public string? Error { get; }

	public ApiResult(HttpStatusCode statusCode, T? body, bool success, string? error = null)
	{
		StatusCode = statusCode;
		Body = body;
		IsSuccess = success;
		Error = error;
	}
}

public static class ApiResult
{
	public static bool IsUnauthorized(HttpStatusCode code) => code == HttpStatusCode.Unauthorized;
	public static bool IsForbidden(HttpStatusCode code) => code == HttpStatusCode.Forbidden;
	public static bool IsNotFound(HttpStatusCode code) => code == HttpStatusCode.NotFound;
	public static bool IsConflict(HttpStatusCode code) => code == HttpStatusCode.Conflict;
	public static bool IsCreatedOrNoContent(HttpStatusCode code) => code is HttpStatusCode.Created or HttpStatusCode.NoContent;
}
