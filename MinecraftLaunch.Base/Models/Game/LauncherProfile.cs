using System.Text.Json.Serialization;

namespace MinecraftLaunch.Base.Models.Game;

public record LauncherProfileEntry
{
    [JsonPropertyName("clientToken")] public string ClientToken { get; set; }
    [JsonPropertyName("launcherVersion")] public LauncherVersionEntry LauncherVersion { get; set; }
    [JsonPropertyName("profiles")] public Dictionary<string, GameProfileEntry> Profiles { get; set; }

    [JsonPropertyName("selectedUser")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SelectedUserEntry SelectedAccount { get; set; }
}

public record GameProfileEntry
{
    [JsonPropertyName("name")] public string Name { get; set; }
    [JsonPropertyName("gameDir")] public string GameFolder { get; set; }
    [JsonPropertyName("lastVersionId")] public string LastVersionId { get; set; }
    [JsonPropertyName("type")] public string Type { get; set; } = "custom";
    [JsonPropertyName("created")] public DateTime Created { get; set; } = DateTime.Now;
    [JsonPropertyName("lastUsed")] public DateTime LastUsed { get; set; } = DateTime.Now;

    [JsonPropertyName("icon")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string Icon { get; set; }

    [JsonPropertyName("javaArgs")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string Jvm { get; set; }

    [JsonPropertyName("javaDir")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string JavaFolder { get; set; }

    [JsonPropertyName("resolution")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ResolutionEntry Resolution { get; set; }
}

public record SelectedUserEntry
{
    [JsonPropertyName("account")] public string Account { get; set; }
    [JsonPropertyName("profile")] public string Profile { get; set; }
}

public record LauncherVersionEntry
{
    [JsonPropertyName("name")] public string Name { get; set; }
    [JsonPropertyName("format")] public int Format { get; set; }
}

public record ResolutionEntry
{
    [JsonIgnore] public bool IsFullScreen { get; set; }
    [JsonPropertyName("width")] public int Width { get; set; } = 854;
    [JsonPropertyName("height")] public int Height { get; set; } = 480;

    public bool IsDefault() => Width == 0 && Height == 0;
}

[JsonSerializable(typeof(LauncherProfileEntry))]
public sealed partial class LauncherProfileEntryContext : JsonSerializerContext;