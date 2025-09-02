using System.Text.Json.Serialization;
using Watcher.Remote;

namespace Watcher.Http;

[JsonSourceGenerationOptions(
    GenerationMode = JsonSourceGenerationMode.Metadata,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase
)]
[JsonSerializable(typeof(VersionInfo))]
[JsonSerializable(typeof(DiskInfo[]))]
[JsonSerializable(typeof(DirectoryListing))]
public partial class AppJsonContext : JsonSerializerContext { }
