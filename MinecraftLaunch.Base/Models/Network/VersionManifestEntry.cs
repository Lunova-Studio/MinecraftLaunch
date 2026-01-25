using MinecraftLaunch.Base.Enums;
using MinecraftLaunch.Base.Interfaces;
using System.Text.Json.Serialization;

namespace MinecraftLaunch.Base.Models.Network;

public record VersionManifestEntry : IInstallEntry
{
    [JsonPropertyName("id")] public string Id { get; set; }
    [JsonPropertyName("url")] public string Url { get; set; }
    [JsonPropertyName("type")] public string Type { get; set; }
    [JsonPropertyName("time")] public DateTime Time { get; set; }
    [JsonPropertyName("releaseTime")] public DateTime ReleaseTime { get; set; }

    [JsonIgnore] public string McVersion => Id;
    [JsonIgnore] public string Description => throw new NotImplementedException();
    [JsonIgnore] public string DisplayVersion => throw new NotImplementedException();
    [JsonIgnore] public ModLoaderType ModLoaderType => throw new NotImplementedException();
}

[JsonSerializable(typeof(VersionManifestEntry))]
[JsonSerializable(typeof(IEnumerable<VersionManifestEntry>))]
public sealed partial class VersionManifestEntryContext : JsonSerializerContext;