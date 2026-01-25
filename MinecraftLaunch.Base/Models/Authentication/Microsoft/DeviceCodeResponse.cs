using System.Text.Json.Serialization;

namespace MinecraftLaunch.Base.Models.Authentication.Microsoft;

public record DeviceCodeResponse
{
    [JsonPropertyName("interval")] public int Interval { get; set; }
    [JsonPropertyName("message")] public string Message { get; set; }
    [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }
    [JsonPropertyName("user_code")] public string UserCode { get; set; }
    [JsonPropertyName("device_code")] public string DeviceCode { get; set; }
    [JsonPropertyName("verification_uri")] public string VerificationUrl { get; set; }
}

[JsonSerializable(typeof(DeviceCodeResponse))]
public sealed partial class DeviceCodeResponseContext : JsonSerializerContext;