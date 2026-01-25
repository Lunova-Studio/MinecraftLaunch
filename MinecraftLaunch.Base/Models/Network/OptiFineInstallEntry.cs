using MinecraftLaunch.Base.Enums;
using MinecraftLaunch.Base.Interfaces;
using System.Text.Json.Serialization;

namespace MinecraftLaunch.Base.Models.Network;

public record OptifineInstallEntry : IInstallEntry
{
    [JsonPropertyName("type")] public string Type { get; set; }
    [JsonPropertyName("patch")] public string Patch { get; set; }
    [JsonPropertyName("filename")] public string FileName { get; set; }
    [JsonPropertyName("mcversion")] public string McVersion { get; set; }

    [JsonIgnore] public ModLoaderType ModLoaderType => ModLoaderType.OptiFine;
    [JsonIgnore] public string DisplayVersion => $"{Type}_{Patch}";
    [JsonIgnore] public string Description => Patch.Contains("pre", StringComparison.OrdinalIgnoreCase) ? "Preview" : "Release";
}

[JsonSerializable(typeof(OptifineInstallEntry))]
[JsonSerializable(typeof(IEnumerable<OptifineInstallEntry>))]
public sealed partial class OptifineInstallEntryContext : JsonSerializerContext;