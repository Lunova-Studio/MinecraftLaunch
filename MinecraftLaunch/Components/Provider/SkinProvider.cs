using Flurl.Http;
using MinecraftLaunch.Base.Models.Authentication;
using MinecraftLaunch.Extensions;
using MinecraftLaunch.Utilities;
using System.Text;

namespace MinecraftLaunch.Components.Provider;

public sealed class SkinProvider {
    private static readonly string YggdrasilSplitUrl = "{0}/sessionserver/session/minecraft/profile/{1}";
    private static readonly string MicrosoftSplitUrl = "https://sessionserver.mojang.com/session/minecraft/profile/{0}";

    public static Task<Stream> GetYggdrasilSkinDataAsync(YggdrasilAccount account, CancellationToken cancellationToken) {
        var url = string.Format(YggdrasilSplitUrl, account.YggdrasilServerUrl,
            account.Uuid.ToString("N"));

        return GetSkinDataAsync(url, cancellationToken);
    }

    public static Task<Stream> GetMicrosoftSkinDataAsync(MicrosoftAccount account, CancellationToken cancellationToken) {
        var url = string.Format(MicrosoftSplitUrl, account.Uuid.ToString("N"));
        return GetSkinDataAsync(url, cancellationToken);
    }

    private static async Task<Stream> GetSkinDataAsync(string url, CancellationToken cancellationToken) {
        var baseJson = await HttpUtil.Request(url).GetStringAsync(cancellationToken: cancellationToken);
        var baseNode = baseJson?.AsNode();

        var base64 = baseNode?.GetEnumerable("properties")
            ?.FirstOrDefault()
            ?.GetString("value");

        var skinJson = Encoding.UTF8.GetString(Convert.FromBase64String(base64));
        var skinNode = skinJson.AsNode();

        var skinUrl = skinNode?.Select("textures")?
            .Select("SKIN")?
            .GetString("url");

        return await HttpUtil.Request(skinUrl).GetStreamAsync(cancellationToken: cancellationToken);
    }
}