using System.Text.Json.Serialization;

namespace MinecraftLaunch.Base.Models.Authentication.Yggdrasil;

public record Agent
{
    [JsonPropertyName("version")] public int Version { get; init; }
    [JsonPropertyName("name")] public required string Name { get; init; }
}

public record SelectedProfile
{
    [JsonPropertyName("id")] public required string Id { get; init; }
    [JsonPropertyName("name")] public required string Name { get; init; }
}

public record YggdrasilAuthenticatePayload
{
    [JsonPropertyName("requestUser")] public bool RequestUser { get; init; }
    [JsonPropertyName("username")] public required string Username { get; init; }
    [JsonPropertyName("password")] public required string Password { get; init; }
    [JsonPropertyName("clientToken")] public required string ClientToken { get; init; }

    [JsonPropertyName("agent")]
    public Agent Agent { get; set; } = new Agent
    {
        Name = "Minecraft",
        Version = 1
    };
}

public class YggdrasilRefreshPayload
{
    [JsonPropertyName("accessToken")] public string AccessToken { get; init; }
    [JsonPropertyName("clientToken")] public string ClientToken { get; init; }
    [JsonPropertyName("requestUser")] public bool RequestUser { get; init; }
    [JsonPropertyName("selectedProfile")] public SelectedProfile SelectedProfile { get; init; }
}

[JsonSerializable(typeof(YggdrasilRefreshPayload))]
[JsonSerializable(typeof(YggdrasilAuthenticatePayload))]
public sealed partial class YggdrasilRequestPayloadContext : JsonSerializerContext;