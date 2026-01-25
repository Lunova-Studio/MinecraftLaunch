using Flurl.Http;
using MinecraftLaunch.Base.Models.Authentication;
using MinecraftLaunch.Base.Models.Authentication.Microsoft;
using MinecraftLaunch.Extensions;
using MinecraftLaunch.Utilities;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;

namespace MinecraftLaunch.Components.Authenticator;

public sealed class MicrosoftAuthenticator
{
    private readonly string _clientId;
    private readonly IEnumerable<string> _scopes = ["XboxLive.signin", "offline_access", "openid", "profile", "email"];

    /// <summary>
    /// Authenticator for Microsoft accounts.
    /// </summary>
    public MicrosoftAuthenticator(string clientId)
    {
        _clientId = clientId;
    }

    public async Task<MicrosoftAccount> RefreshAsync(MicrosoftAccount account, CancellationToken cancellationToken = default)
    {
        var request = HttpUtil.Request("https://login.live.com/oauth20_token.srf");
        Dictionary<string, string> payload = new()
        {
            ["client_id"] = _clientId,
            ["refresh_token"] = account.RefreshToken,
            ["grant_type"] = "refresh_token"
        };

        var result = await request.PostAsync(new FormUrlEncodedContent(payload),
            cancellationToken: cancellationToken);

        var json = await result.GetStringAsync();
        var response = json.Deserialize(OAuth2TokenResponseContext.Default.OAuth2TokenResponse);

        return await AuthenticateAsync(response, cancellationToken);
    }

    /// <summary>
    /// Asynchronously authenticates the Microsoft account.
    /// </summary>
    /// <returns>A ValueTask that represents the asynchronous operation. The task result contains the authenticated Microsoft account.</returns>
    public async Task<MicrosoftAccount> AuthenticateAsync(OAuth2TokenResponse oAuth2Token, CancellationToken cancellationToken = default)
    {
        try
        {
            if (oAuth2Token is null)
                ArgumentException.ThrowIfNullOrEmpty(nameof(oAuth2Token));

            var xblToken = await GetXBLTokenAsync(oAuth2Token.AccessToken, cancellationToken);
            var xsts = await GetXSTSTokenAsync(xblToken, cancellationToken);
            var minecraftAccessToken = await GetMinecraftAccessTokenAsync((xblToken, xsts), cancellationToken);
            var profile = await GetMinecraftProfileAsync(minecraftAccessToken.GetString("access_token"), oAuth2Token.RefreshToken, cancellationToken);

            return profile;
        }
        catch (Exception)
        {
            throw;
        }
    }

    /// <summary>
    /// Asynchronously authenticates the Microsoft account using device flow authentication.
    /// </summary>
    /// <param name="deviceCode">The action to be performed with the device code response.</param>
    /// <param name="source">The cancellation token source to be used to cancel the operation.</param>
    /// <returns>A Task that represents the asynchronous operation. The task result contains the OAuth2 token response.</returns>
    public async Task<OAuth2TokenResponse> DeviceFlowAuthAsync(Action<DeviceCodeResponse> deviceCode, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_clientId))
            ArgumentException.ThrowIfNullOrEmpty("ClientId");

        var parameters = new Dictionary<string, string>
        {
            ["client_id"] = _clientId,
            ["tenant"] = "/consumers",
            ["scope"] = string.Join(" ", _scopes)
        };

        var request = HttpUtil.Request("https://login.microsoftonline.com/consumers/oauth2/v2.0/devicecode");
        string json = await request.PostUrlEncodedAsync(parameters, cancellationToken: cancellationToken)
            .ReceiveString();

        var codeResponse = json.Deserialize(DeviceCodeResponseContext.Default.DeviceCodeResponse);
        deviceCode.Invoke(codeResponse);

        //Polling
        int timeout = codeResponse.ExpiresIn;
        OAuth2TokenResponse tokenResponse = default!;

        var stopwatch = Stopwatch.StartNew();
        var requestParams =
            "grant_type=urn:ietf:params:oauth:grant-type:device_code" +
            $"&client_id={_clientId}" +
            $"&device_code={codeResponse.DeviceCode}";

        do
        {
            cancellationToken.ThrowIfCancellationRequested();

            var response = await HttpUtil.Request("https://login.microsoftonline.com/consumers/oauth2/v2.0/token")
                .OnError(x => x.ExceptionHandled = true)
                .PostUrlEncodedAsync(new Dictionary<string, string>
                {
                    ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
                    ["client_id"] = _clientId,
                    ["device_code"] = codeResponse.DeviceCode
                }, HttpCompletionOption.ResponseContentRead, cancellationToken);

            var tokenJson = await response.GetStringAsync();
            var tempTokenResponse = tokenJson.AsNode();

            if (tempTokenResponse["error"] == null)
            {
                tokenResponse = new()
                {
                    AccessToken = tempTokenResponse.GetString("access_token"),
                    RefreshToken = tempTokenResponse.GetString("refresh_token"),
                    ExpiresIn = tempTokenResponse.GetInt32("expires_in"),
                };
            }

            if (tempTokenResponse.GetString("token_type") is "Bearer")
                return tokenResponse;

            await Task.Delay(TimeSpan.FromSeconds(codeResponse.Interval), cancellationToken);
        } while (stopwatch.Elapsed < TimeSpan.FromSeconds(timeout));

        throw new TimeoutException("登录操作已超时");
    }

    #region Privates

    /// <summary>
    /// Get Xbox live token & userhash
    /// </summary>
    private static async Task<JsonNode> GetXBLTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        var request = HttpUtil.Request("https://user.auth.xboxlive.com/user/authenticate");
        var xblContent = new XBLTokenPayload
        {
            Properties = new XBLProperties
            {
                AuthMethod = "RPS",
                SiteName = "user.auth.xboxlive.com",
                RpsTicket = $"d={token}"
            },
            RelyingParty = "http://auth.xboxlive.com",
            TokenType = "JWT"
        };

        using var xblJsonReq = await request.PostAsync(JsonContent.Create(xblContent,
            MicrosoftRequestPayloadContext.Default.XBLTokenPayload), cancellationToken: cancellationToken);

        return (await xblJsonReq.GetStringAsync()).AsNode();
    }

    /// <summary>
    /// Get Xbox security token service token & userhash
    /// </summary>
    /// <returns></returns>
    /// <exception cref="FailedAuthenticationException"></exception>
    private static async Task<JsonNode> GetXSTSTokenAsync(JsonNode xblTokenNode, CancellationToken cancellationToken = default)
    {
        var request = HttpUtil.Request("https://xsts.auth.xboxlive.com/xsts/authorize");
        var xstsContent = new XSTSTokenPayload
        {
            Properties = new XSTSProperties
            {
                SandboxId = "RETAIL",
                UserTokens = [xblTokenNode.GetString("Token")]
            },
            RelyingParty = "rp://api.minecraftservices.com/",
            TokenType = "JWT"
        };

        using var xstsJsonReq = await request.PostAsync(JsonContent.Create(xstsContent,
            MicrosoftRequestPayloadContext.Default.XSTSTokenPayload), cancellationToken: cancellationToken);

        return (await xstsJsonReq.GetStringAsync()).AsNode();
    }

    /// <summary>
    /// Get Minecraft access token
    /// </summary>
    private static async Task<JsonNode> GetMinecraftAccessTokenAsync((JsonNode xblTokenNode, JsonNode xstsTokenNode) nodes, CancellationToken cancellationToken = default)
    {
        var request = HttpUtil.Request("https://api.minecraftservices.com/authentication/login_with_xbox");
        var xstsToken = nodes.xstsTokenNode.GetString("Token");
        var uhsToken = nodes.xblTokenNode.Select("DisplayClaims")
            .GetEnumerable("xui")
            .FirstOrDefault()
            .GetString("uhs");

        var payload = new MinecraftPayload($"XBL3.0 x={uhsToken};{xstsToken}");
        using var mcTokenReq = await request.PostAsync(JsonContent.Create(payload,
            MicrosoftRequestPayloadContext.Default.MinecraftPayload), cancellationToken: cancellationToken);

        return (await mcTokenReq.GetStringAsync()).AsNode();
    }

    /// <summary>
    /// Get player's minecraft profile
    /// </summary>
    /// <param name="accessToken">Minecraft access token</param>
    /// <param name="refreshToken">Minecraft refresh token</param>
    /// <exception cref="InvalidOperationException">If authenticated user don't have minecraft, the exception will be thrown</exception>
    private static async Task<MicrosoftAccount> GetMinecraftProfileAsync(string accessToken, string refreshToken, CancellationToken cancellationToken = default)
    {
        var request = HttpUtil.Request("https://api.minecraftservices.com/minecraft/profile")
            .WithHeader("Authorization", $"Bearer {accessToken}");

        using var profileRes = await request.GetAsync(cancellationToken: cancellationToken);
        var profileNode = (await profileRes.GetStringAsync())
            .AsNode();

        return profileNode == null
            ? throw new InvalidOperationException("Failed to retrieve Minecraft profile")
            : new MicrosoftAccount(profileNode.GetString("name"), Guid.Parse(profileNode.GetString("id")), accessToken, refreshToken, DateTime.Now);
    }

    #endregion Privates
}