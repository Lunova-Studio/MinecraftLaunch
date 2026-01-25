using MinecraftLaunch.Base.Enums;
using MinecraftLaunch.Base.Interfaces;
using System.Text.Json.Serialization;

namespace MinecraftLaunch.Base.Models.Network;

public record ForgeInstallEntry : IInstallEntry
{
    [JsonIgnore] public bool IsNeoforge { get; set; }
    [JsonPropertyName("build")] public int Build { get; set; }
    [JsonPropertyName("branch")] public string Branch { get; set; }
    [JsonPropertyName("mcversion")] public string McVersion { get; set; }
    [JsonPropertyName("version")] public string ForgeVersion { get; set; }
    [JsonPropertyName("modified")] public DateTime ModifiedTime { get; set; }

    [JsonIgnore] public string DisplayVersion => ForgeVersion;
    [JsonIgnore] public ModLoaderType ModLoaderType => IsNeoforge ? ModLoaderType.NeoForge : ModLoaderType.Forge;
    [JsonIgnore]
    public string Description => IsNeoforge
        ? ForgeVersion.Contains("beta", StringComparison.OrdinalIgnoreCase)
        ? "Preview"
        : "Release"
        : ModifiedTime.ToString();
}

[JsonSerializable(typeof(ForgeInstallEntry))]
[JsonSerializable(typeof(IEnumerable<ForgeInstallEntry>))]
public sealed partial class ForgeInstallEntryContext : JsonSerializerContext;