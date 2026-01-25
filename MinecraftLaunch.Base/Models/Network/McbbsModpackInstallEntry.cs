using System.Text.Json.Serialization;

namespace MinecraftLaunch.Base.Models.Network;

public record McbbsModpackInstallEntry
{
    [JsonPropertyName("name")] public string Name { get; set; }
    [JsonPropertyName("author")] public string Author { get; set; }
    [JsonPropertyName("version")] public string Version { get; set; }
    [JsonPropertyName("description")] public string Description { get; set; }
    [JsonPropertyName("addons")] public IEnumerable<McbbsModpackAddons> Addons { get; set; }
    [JsonPropertyName("files")] public IEnumerable<McbbsModpackFileEntry> Files { get; set; }

    [JsonIgnore] public string McVersion => Addons?.FirstOrDefault(x => x.Id.Equals("game")).Version;
}

public record McbbsModpackAddons
{
    [JsonPropertyName("id")] public string Id { get; set; }
    [JsonPropertyName("version")] public string Version { get; set; }
}

public record McbbsModpackFileEntry
{
    [JsonPropertyName("path")] public string Path { get; set; }
    [JsonPropertyName("hash")] public string Hash { get; set; }
    [JsonPropertyName("type")] public string Type { get; set; }
    [JsonPropertyName("force")] public bool IsForce { get; set; }
}

[JsonSerializable(typeof(McbbsModpackInstallEntry))]
public sealed partial class McbbsModpackInstallEntryContext : JsonSerializerContext;