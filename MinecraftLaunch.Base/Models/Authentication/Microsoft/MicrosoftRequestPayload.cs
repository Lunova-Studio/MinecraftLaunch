using System.Text.Json.Serialization;

namespace MinecraftLaunch.Base.Models.Authentication.Microsoft;

public record MinecraftPayload(string identityToken);

public record XBLProperties
{
    public required string SiteName { get; init; }
    public required string RpsTicket { get; init; }
    public required string AuthMethod { get; init; }
}

public record XSTSProperties
{
    public required string SandboxId { get; init; }
    public required string[] UserTokens { get; init; }
}

public record XBLTokenPayload
{
    public required string TokenType { get; init; }
    public required string RelyingParty { get; init; }
    public required XBLProperties Properties { get; init; }
}

public record XSTSTokenPayload
{
    public required string TokenType { get; init; }
    public required string RelyingParty { get; init; }
    public required XSTSProperties Properties { get; init; }
}

public record RefreshTokenPayload
{
    [JsonPropertyName("client_id")] public required string ClientId { get; init; }
    [JsonPropertyName("grant_type")] public required string GrantType { get; init; }
    [JsonPropertyName("refresh_token")] public required string RefreshToken { get; init; }
}

[JsonSerializable(typeof(XBLTokenPayload))]
[JsonSerializable(typeof(XSTSTokenPayload))]
[JsonSerializable(typeof(MinecraftPayload))]
public sealed partial class MicrosoftRequestPayloadContext : JsonSerializerContext;