using System.Text.Json.Serialization;

namespace MinecraftLaunch.Base.Models.Authentication.Yggdrasil;

public record YggdrasilResponse
{
    [JsonPropertyName("user")] public User User { get; set; }
    [JsonPropertyName("clientToken")] public string ClientToken { get; set; }
    [JsonPropertyName("accessToken")] public string AccessToken { get; set; }
    [JsonPropertyName("selectedProfile")] public ProfileModel SelectedProfile { get; set; }
    [JsonPropertyName("availableProfiles")] public IEnumerable<ProfileModel> AvailableProfiles { get; set; }
}

public record User
{
    [JsonPropertyName("id")] public string Id { get; set; }
    [JsonPropertyName("properties")] public IEnumerable<PropertyModel> Properties { get; set; }
}

public record PropertyModel
{
    [JsonPropertyName("name")] public string Name { get; set; }
    [JsonPropertyName("value")] public string Value { get; set; }
    [JsonPropertyName("userId")] public string UserId { get; set; }
    [JsonPropertyName("profileId")] public string ProfileId { get; set; }
}

public record ProfileModel
{
    [JsonPropertyName("id")] public string Id { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; }
}

[JsonSerializable(typeof(YggdrasilResponse))]
public sealed partial class YggdrasilResponseContext : JsonSerializerContext;