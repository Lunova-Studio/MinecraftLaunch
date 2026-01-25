using System.Text.Json.Serialization;

namespace MinecraftLaunch.Base.Models.Network;

public record ModrinthModpackInstallEntry
{
    [JsonPropertyName("name")] public string Name { get; set; }
    [JsonPropertyName("summary")] public string Summary { get; set; }
    [JsonPropertyName("versionId")] public string VersionId { get; set; }
    [JsonPropertyName("formatVersion")] public int FormatVersion { get; set; }
    [JsonPropertyName("files")] public IEnumerable<ModrinthModpackFileEntry> Files { get; set; }
    [JsonPropertyName("dependencies")] public IReadOnlyDictionary<string, string> Dependencies { get; set; }

    [JsonIgnore] public string McVersion => Dependencies["minecraft"];
}

public record ModrinthModpackFileEntry
{
    [JsonPropertyName("path")] public string Path { get; set; }
    [JsonPropertyName("fileSize")] public long Size { get; set; }
    [JsonPropertyName("downloads")] public IEnumerable<string> Downloads { get; set; }
    [JsonPropertyName("hashes")] public IReadOnlyDictionary<string, string> Hashes { get; set; }
}

[JsonSerializable(typeof(ModrinthModpackInstallEntry))]
public partial class ModrinthModpackInstallEntryContext : JsonSerializerContext;