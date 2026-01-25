using MinecraftLaunch.Base.Enums;
using MinecraftLaunch.Base.Interfaces;
using System.Text.Json.Serialization;

namespace MinecraftLaunch.Base.Models.Network;

public record FabricInstallEntry : IInstallEntry
{
    [JsonPropertyName("loader")] public FabricMavenItem Loader { get; set; }
    [JsonPropertyName("intermediary")] public FabricMavenItem Intermediary { get; set; }

    [JsonIgnore] public string BuildVersion => Loader.Version;
    [JsonIgnore] public string DisplayVersion => Loader.Version;
    [JsonIgnore] public string McVersion => Intermediary.Version;
    [JsonIgnore] public ModLoaderType ModLoaderType => ModLoaderType.Fabric;
    [JsonIgnore] public string Description => Loader.IsStable ? $"Release" : "Preview";
}

public record FabricMavenItem
{
    [JsonPropertyName("stable")] public bool IsStable { get; set; }

    [JsonPropertyName("maven")] public string Maven { get; set; }
    [JsonPropertyName("version")] public string Version { get; set; }
    [JsonPropertyName("separator")] public string Separator { get; set; }
}

[JsonSerializable(typeof(FabricInstallEntry))]
[JsonSerializable(typeof(IEnumerable<FabricInstallEntry>))]
public sealed partial class FabricInstallEntryContext : JsonSerializerContext;