using System.Text.Json.Serialization;

namespace MinecraftLaunch.Base.Models.Authentication.Microsoft;

public record OAuth2TokenResponse
{
    [JsonPropertyName("foci")] public string Foci { get; set; }
    [JsonPropertyName("scope")] public string Scope { get; set; }
    [JsonPropertyName("user_id")] public string UserId { get; set; }
    [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }
    [JsonPropertyName("token_type")] public string TokenType { get; set; }
    [JsonPropertyName("access_token")] public string AccessToken { get; set; }
    [JsonPropertyName("refresh_token")] public string RefreshToken { get; set; }
}

[JsonSerializable(typeof(OAuth2TokenResponse))]
public sealed partial class OAuth2TokenResponseContext : JsonSerializerContext;