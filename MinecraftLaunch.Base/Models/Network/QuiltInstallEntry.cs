using MinecraftLaunch.Base.Enums;
using MinecraftLaunch.Base.Interfaces;
using System.Text.Json.Serialization;

namespace MinecraftLaunch.Base.Models.Network;

public record QuiltInstallEntry : IInstallEntry
{
    [JsonPropertyName("loader")] public required FabricMavenItem Loader { get; set; }
    [JsonPropertyName("intermediary")] public required FabricMavenItem Intermediary { get; set; }

    [JsonIgnore] public string BuildVersion => Loader.Version;
    [JsonIgnore] public string McVersion => Intermediary.Version;
    [JsonIgnore] public ModLoaderType ModLoaderType => ModLoaderType.Quilt;
    [JsonIgnore] public string DisplayVersion => $"{McVersion}-{Loader.Version}";
    [JsonIgnore] public string Description => Loader.IsStable ? $"Release" : "Preview";
}

[JsonSerializable(typeof(QuiltInstallEntry))]
[JsonSerializable(typeof(IEnumerable<QuiltInstallEntry>))]
public sealed partial class QuiltInstallEntryContext : JsonSerializerContext;