using Flurl.Http;
using MinecraftLaunch.Base.Models.Authentication;
using MinecraftLaunch.Base.Models.Authentication.Microsoft;
using MinecraftLaunch.Extensions;
using MinecraftLaunch.Utilities;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;

namespace MinecraftLaunch.Components.Authenticator;

public sealed class MicrosoftAuthenticator {
    private readonly string _clientId;
    private readonly IEnumerable<string> _scopes = ["XboxLive.signin", "offline_access", "openid", "profile", "email"];

    /// <summary>
    /// Authenticator for Microsoft accounts.
    /// </summary>
    public MicrosoftAuthenticator(string clientId) {
        _clientId = clientId;
    }

    public async Task<MicrosoftAccount> RefreshAsync(MicrosoftAccount account, CancellationToken cancellationToken = default) {
        var request = HttpUtil.Request("https://login.live.com/oauth20_token.srf");
        Dictionary<string, string> payload = new() {
            ["client_id"] = _clientId,
            ["refresh_token"] = account.RefreshToken,
            ["grant_type"] = "refresh_token"
        };

        var result = await request.PostAsync(new FormUrlEncodedContent(payload),
            cancellationToken: cancellationToken);

        await using var json = await result.GetStreamAsync();
        var response = await JsonSerializer.DeserializeAsync(json,OAuth2TokenResponseContext.Default.OAuth2TokenResponse, cancellationToken);
        return await AuthenticateAsync(response, cancellationToken);
    }

    /// <summary>
    /// Asynchronously authenticates the Microsoft account.
    /// </summary>
    /// <returns>A ValueTask that represents the asynchronous operation. The task result contains the authenticated Microsoft account.</returns>
    public async Task<MicrosoftAccount> AuthenticateAsync(OAuth2TokenResponse oAuth2Token,
        CancellationToken cancellationToken = default)
    {
        if (oAuth2Token is null)
            ArgumentException.ThrowIfNullOrEmpty(nameof(oAuth2Token));

        using var xblToken = await GetXBLTokenAsync(oAuth2Token.AccessToken, cancellationToken);
        using var xsts = await GetXSTSTokenAsync(xblToken.RootElement, cancellationToken);
        using var minecraftAccessToken = await GetMinecraftAccessTokenAsync((xblToken.RootElement, xsts.RootElement), cancellationToken);
        var profile = await GetMinecraftProfileAsync(minecraftAccessToken.RootElement.GetProperty("access_token"u8).GetString(),
            oAuth2Token.RefreshToken, cancellationToken);

        return profile;
    }

    /// <summary>
    /// Asynchronously authenticates the Microsoft account using device flow authentication.
    /// </summary>
    /// <param name="deviceCode">The action to be performed with the device code response.</param>
    /// <param name="source">The cancellation token source to be used to cancel the operation.</param>
    /// <returns>A Task that represents the asynchronous operation. The task result contains the OAuth2 token response.</returns>
    public async Task<OAuth2TokenResponse> DeviceFlowAuthAsync(Action<DeviceCodeResponse> deviceCode, CancellationToken cancellationToken = default) {
        if (string.IsNullOrEmpty(_clientId))
            ArgumentException.ThrowIfNullOrEmpty("ClientId");

        var parameters = new Dictionary<string, string> {
            ["client_id"] = _clientId,
            ["tenant"] = "/consumers",
            ["scope"] = string.Join(" ", _scopes)
        };

        var request = HttpUtil.Request("https://login.microsoftonline.com/consumers/oauth2/v2.0/devicecode");
        await using var json = await request.PostUrlEncodedAsync(parameters, cancellationToken: cancellationToken)
            .ReceiveStream();

        var codeResponse = await JsonSerializer.DeserializeAsync(json,DeviceCodeResponseContext.Default.DeviceCodeResponse, cancellationToken);
        deviceCode.Invoke(codeResponse);

        //Polling
        int timeout = codeResponse.ExpiresIn;
        OAuth2TokenResponse tokenResponse = default!;

        var stopwatch = Stopwatch.StartNew();
        var requestParams =
            "grant_type=urn:ietf:params:oauth:grant-type:device_code" +
            $"&client_id={_clientId}" +
            $"&device_code={codeResponse.DeviceCode}";

        do {
            cancellationToken.ThrowIfCancellationRequested();

            var response = await HttpUtil.Request("https://login.microsoftonline.com/consumers/oauth2/v2.0/token")
                .OnError(x => x.ExceptionHandled = true)
                .PostUrlEncodedAsync(new Dictionary<string, string> {
                    ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
                    ["client_id"] = _clientId,
                    ["device_code"] = codeResponse.DeviceCode
                }, HttpCompletionOption.ResponseContentRead, cancellationToken);

            await using var tokenJson = await response.GetStreamAsync();
            using var doc = await JsonDocument.ParseAsync(tokenJson,cancellationToken: cancellationToken); 
            var tempTokenResponse = doc.RootElement;

            if (!tempTokenResponse.TryGetProperty("error"u8,out _)) {
                tokenResponse = new() {
                    AccessToken = tempTokenResponse.GetProperty("access_token"u8).GetString(),
                    RefreshToken = tempTokenResponse.GetProperty("refresh_token"u8).GetString(),
                    ExpiresIn = tempTokenResponse.GetProperty("expires_in"u8).GetInt32(),
                };
            }

            if (tempTokenResponse.GetPropertyNullable("token_type"u8)?.GetString() is "Bearer")
                return tokenResponse;

            await Task.Delay(TimeSpan.FromSeconds(codeResponse.Interval), cancellationToken);
        } while (stopwatch.Elapsed < TimeSpan.FromSeconds(timeout));

        throw new TimeoutException("登录操作已超时");
    }

    #region Privates

    /// <summary>
    /// Get Xbox live token & userhash
    /// </summary>
    private static async Task</*所有权*/JsonDocument> GetXBLTokenAsync(string token, CancellationToken cancellationToken = default) {
        var request = HttpUtil.Request("https://user.auth.xboxlive.com/user/authenticate");
        var xblContent = new XBLTokenPayload {
            Properties = new XBLProperties {
                AuthMethod = "RPS",
                SiteName = "user.auth.xboxlive.com",
                RpsTicket = $"d={token}"
            },
            RelyingParty = "http://auth.xboxlive.com",
            TokenType = "JWT"
        };

        using var xblJsonReq = await request.PostAsync(JsonContent.Create(xblContent,
            MicrosoftRequestPayloadContext.Default.XBLTokenPayload), cancellationToken: cancellationToken);

        return await JsonDocument.ParseAsync(await xblJsonReq.GetStreamAsync(),cancellationToken:cancellationToken);
    }

    /// <summary>
    /// Get Xbox security token service token & userhash
    /// </summary>
    /// <returns></returns>
    /// <exception cref="FailedAuthenticationException"></exception>
    private static async Task</*所有权*/JsonDocument> GetXSTSTokenAsync(JsonElement xblTokenNode, CancellationToken cancellationToken = default) {
        var request = HttpUtil.Request("https://xsts.auth.xboxlive.com/xsts/authorize");
        var xstsContent = new XSTSTokenPayload {
            Properties = new XSTSProperties {
                SandboxId = "RETAIL",
                UserTokens = [xblTokenNode.GetProperty("Token"u8).GetString()]
            },
            RelyingParty = "rp://api.minecraftservices.com/",
            TokenType = "JWT"
        };

        using var xstsJsonReq = await request.PostAsync(JsonContent.Create(xstsContent,
            MicrosoftRequestPayloadContext.Default.XSTSTokenPayload), cancellationToken: cancellationToken);

        return await JsonDocument.ParseAsync(await xstsJsonReq.GetStreamAsync(),cancellationToken:cancellationToken);
    }

    /// <summary>
    /// Get Minecraft access token
    /// </summary>
    private static async Task</*所有权*/JsonDocument> GetMinecraftAccessTokenAsync((JsonElement xblTokenNode, JsonElement xstsTokenNode) nodes, CancellationToken cancellationToken = default) {
        var request = HttpUtil.Request("https://api.minecraftservices.com/authentication/login_with_xbox");
        var xstsToken = nodes.xstsTokenNode.GetProperty("Token"u8).GetString();
        var uhsToken = nodes.xblTokenNode
            .GetProperty("DisplayClaims"u8)
            .GetProperty("xui"u8)
            [0]//get first
            .GetProperty("uhs"u8)
            .GetString();

        var payload = new MinecraftPayload($"XBL3.0 x={uhsToken};{xstsToken}");
        using var mcTokenReq = await request.PostAsync(JsonContent.Create(payload,
            MicrosoftRequestPayloadContext.Default.MinecraftPayload), cancellationToken: cancellationToken);

        return await JsonDocument.ParseAsync(await mcTokenReq.GetStreamAsync(),cancellationToken:cancellationToken);
    }

    /// <summary>
    /// Get player's minecraft profile
    /// </summary>
    /// <param name="accessToken">Minecraft access token</param>
    /// <param name="refreshToken">Minecraft refresh token</param>
    /// <exception cref="InvalidOperationException">If authenticated user don't have minecraft, the exception will be thrown</exception>
    private static async Task<MicrosoftAccount> GetMinecraftProfileAsync(string accessToken, string refreshToken, CancellationToken cancellationToken = default) {
        var request = HttpUtil.Request("https://api.minecraftservices.com/minecraft/profile")
            .WithHeader("Authorization", $"Bearer {accessToken}");

        using var profileRes = await request.GetAsync(cancellationToken: cancellationToken);
        try
        {
            using var profileNode = await JsonDocument.ParseAsync(await profileRes.GetStreamAsync(),
                cancellationToken: cancellationToken);
            return new MicrosoftAccount(profileNode.RootElement.GetProperty("name"u8).GetString(), profileNode.RootElement.GetProperty("id"u8).GetGuid(), accessToken, refreshToken, DateTime.Now);
        }
        catch (Exception e)
        {
            throw new InvalidOperationException("Failed to retrieve Minecraft profile", e);
        }
    }

    #endregion
}