using System.Text.Json.Serialization;

namespace Watcher.Remote;

public sealed class DirectoryListing
{
	[JsonPropertyName("free")] public long Free { get; init; }
	[JsonPropertyName("total")] public long Total { get; init; }
	[JsonPropertyName("block_size")] public int BlockSize { get; init; }
	[JsonPropertyName("writable")] public bool Writable { get; init; }
	[JsonPropertyName("files")] public required FileEntry[] Files { get; init; }
}

public sealed class FileEntry
{
	[JsonPropertyName("name")] public required string Name { get; init; }
	[JsonPropertyName("directory")] public bool IsDirectory { get; init; }
	[JsonPropertyName("modified_ns")] public long ModifiedNs { get; init; }
	[JsonPropertyName("file_size")] public long FileSize { get; init; }
}

public sealed class DiskInfo
{
	[JsonPropertyName("root")] public required string Root { get; init; }
	[JsonPropertyName("free")] public long Free { get; init; }
	[JsonPropertyName("total")] public long Total { get; init; }
	[JsonPropertyName("block_size")] public int BlockSize { get; init; }
	[JsonPropertyName("writable")] public bool Writable { get; init; }
}

public sealed class VersionInfo
{
	[JsonPropertyName("web_api_version")] public int WebApiVersion { get; init; }
	[JsonPropertyName("version")] public string? Version { get; init; }
	[JsonPropertyName("board_name")] public string? BoardName { get; init; }
	[JsonPropertyName("hostname")] public string? Hostname { get; init; }
	[JsonPropertyName("port")] public int? Port { get; init; }
	[JsonPropertyName("ip")] public string? Ip { get; init; }
}
