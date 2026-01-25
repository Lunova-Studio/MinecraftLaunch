using MinecraftLaunch.Base.Interfaces;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace MinecraftLaunch.Base.Models.Game;

public class AssstIndex : MinecraftDependency, IDownloadDependency, IVerifiableDependency
{
    long? IVerifiableDependency.Size => Size;

    [JsonPropertyName("id")] public string Id { get; set; }
    [JsonPropertyName("url")] public string Url { get; set; }
    [JsonPropertyName("sha1")] public string Sha1 { get; set; }
    [JsonPropertyName("size")] public long? Size { get; set; }

    [JsonIgnore] public override string FilePath => Path.Combine("assets", "indexes", $"{Id}.json");
}

public record MinecraftJsonEntry
{
    [JsonPropertyName("id")] public string Id { get; set; }
    [JsonPropertyName("type")] public string Type { get; set; }
    [JsonPropertyName("assets")] public string Assets { get; set; }
    [JsonPropertyName("mainClass")] public string MainClass { get; set; }
    [JsonPropertyName("arguments")] public JsonNode Arguments { get; set; }
    [JsonPropertyName("libraries")] public JsonArray Libraries { get; set; }
    [JsonPropertyName("inheritsFrom")] public string InheritsFrom { get; set; }
    [JsonPropertyName("javaVersion")] public JsonNode JavaVersion { get; set; }
    [JsonPropertyName("releaseTime")] public DateTime ReleaseTime { get; set; }
    [JsonPropertyName("assetIndex")] public AssstIndexJsonEntry AssetIndex { get; set; }
    [JsonPropertyName("minecraftArguments")] public string MinecraftArguments { get; set; }
}

public record AssstIndexJsonEntry
{
    [JsonPropertyName("id")] public string Id { get; set; }
    [JsonPropertyName("size")] public int Size { get; set; }
    [JsonPropertyName("url")] public string Url { get; set; }
    [JsonPropertyName("sha1")] public string Sha1 { get; set; }
    [JsonPropertyName("totalSize")] public int TotalSize { get; set; }
}

public record OptifineMinecraftEntry
{
    [JsonPropertyName("id")] public string Id { get; set; }
    [JsonPropertyName("time")] public string Time { get; set; }
    [JsonPropertyName("type")] public string Type { get; set; }
    [JsonPropertyName("mainClass")] public string MainClass { get; set; }
    [JsonPropertyName("releaseTime")] public string ReleaseTime { get; set; }
    [JsonPropertyName("inheritsFrom")] public string InheritsFrom { get; set; }
    [JsonPropertyName("libraries")] public IEnumerable<OptifineMinecraftLibrary> Libraries { get; set; }
    [JsonPropertyName("minecraftArguments")] public string MinecraftArguments { get; set; }
}

public record struct OptifineMinecraftLibrary
{
    [JsonPropertyName("name")] public string Name { get; set; }
}

[JsonSerializable(typeof(MinecraftJsonEntry))]
[JsonSerializable(typeof(OptifineMinecraftEntry))]
public sealed partial class MinecraftJsonEntryContext : JsonSerializerContext;