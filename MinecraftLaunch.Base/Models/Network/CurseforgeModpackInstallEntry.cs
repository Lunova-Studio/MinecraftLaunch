using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace MinecraftLaunch.Base.Models.Network;

public record CurseforgeModpackInstallEntry
{
    [JsonPropertyName("name")] public string Id { get; set; }
    [JsonPropertyName("author")] public string Author { get; set; }
    [JsonPropertyName("version")] public string Version { get; set; }
    [JsonPropertyName("overrides")] public string Overrides { get; set; }
    [JsonPropertyName("minecraft")] public CurseforgeModpackMinecraftEntry Minecraft { get; set; }
    [JsonPropertyName("files")] public IEnumerable<CurseforgeModpackFileEntry> ModFiles { get; set; }

    [JsonIgnore] public string McVersion => Minecraft.McVersion;
    [JsonIgnore] public bool IsOverride => Overrides is "overrides";
    [JsonIgnore] public string PrimaryModLoader => Minecraft.ModLoaders?.FirstOrDefault()?["id"]?.GetValue<string>().Split("-")?.First();
}

public record CurseforgeModpackMinecraftEntry
{
    [JsonPropertyName("version")] public string McVersion { get; set; }
    [JsonPropertyName("modLoaders")] public IEnumerable<JsonNode> ModLoaders { get; set; }
}

public record CurseforgeModpackFileEntry
{
    [JsonPropertyName("fileID")] public long FileId { get; set; }
    [JsonPropertyName("projectID")] public long ProjectId { get; set; }
    [JsonPropertyName("required")] public bool IsRequired { get; set; }
}

[JsonSerializable(typeof(CurseforgeModpackInstallEntry))]
public sealed partial class CurseforgeModpackInstallEntryContext : JsonSerializerContext;