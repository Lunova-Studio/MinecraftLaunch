using Flurl;
using Flurl.Http;
using MinecraftLaunch.Base.Models.Authentication;
using MinecraftLaunch.Base.Models.Authentication.Yggdrasil;
using MinecraftLaunch.Extensions;
using MinecraftLaunch.Utilities;
using System.Net.Http.Json;

namespace MinecraftLaunch.Components.Authenticator;

public sealed class YggdrasilAuthenticator
{
    private readonly string _url;
    private readonly string _email;
    private readonly string _password;

    /// <summary>
    /// Constructor for YggdrasilAuthenticator.
    /// </summary>
    /// <param name="url">The URL for authentication.</param>
    /// <param name="email">The email of the account.</param>
    /// <param name="password">The password of the account.</param>
    public YggdrasilAuthenticator(string url, string email, string password)
    {
        _url = url;
        _email = email;
        _password = password;
    }

    public async Task<YggdrasilAccount> RefreshAsync(YggdrasilAccount account, CancellationToken cancellationToken = default)
    {
        var request = HttpUtil.Request(new Url(_url), "authserver", "refresh");
        var payload = new YggdrasilRefreshPayload
        {
            RequestUser = true,
            AccessToken = account.AccessToken,
            ClientToken = account.ClientToken,
            SelectedProfile = new SelectedProfile
            {
                Name = account.Name,
                Id = account.Uuid.ToString("N"),
            }
        };

        using var responseMessage = await request.PostAsync(JsonContent.Create(payload,
            YggdrasilRequestPayloadContext.Default.YggdrasilRefreshPayload),
                cancellationToken: cancellationToken);

        var json = await responseMessage.GetStringAsync();
        var entry = json.Deserialize(YggdrasilResponseContext.Default.YggdrasilResponse);
        var profile = entry.SelectedProfile;

        return new YggdrasilAccount(profile.Name, Guid.Parse(profile.Id), entry.AccessToken, _url, entry.ClientToken);
    }

    /// <summary>
    /// Asynchronously authenticates the Yggdrasil account.
    /// </summary>
    /// <returns>A ValueTask that represents the asynchronous operation. The task result contains the authenticated Yggdrasil account.</returns>
    public async Task<IEnumerable<YggdrasilAccount>> AuthenticateAsync(CancellationToken cancellationToken = default)
    {
        var request = HttpUtil.Request(new Url(_url), "authserver", "authenticate");
        var payload = new YggdrasilAuthenticatePayload
        {
            ClientToken = Guid.NewGuid().ToString("N"),
            Username = _email,
            Password = _password,
            RequestUser = false,
        };

        using var responseMessage = await request.PostAsync(JsonContent.Create(payload,
            YggdrasilRequestPayloadContext.Default.YggdrasilAuthenticatePayload),
                cancellationToken: cancellationToken);

        var json = await responseMessage.GetStringAsync();
        var entry = json.Deserialize(YggdrasilResponseContext.Default.YggdrasilResponse);

        return entry.AvailableProfiles.Select(profile =>
            new YggdrasilAccount(profile.Name, Guid.Parse(profile.Id), entry.AccessToken, _url, entry.ClientToken));
    }
}