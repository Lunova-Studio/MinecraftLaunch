using System.Diagnostics;
using Flurl.Http;
using MinecraftLaunch.Base.Models.Authentication;
using MinecraftLaunch.Extensions;
using MinecraftLaunch.Utilities;
using System.Text.Json;

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
        await using var stream = await HttpUtil.Request(url).GetStreamAsync(cancellationToken: cancellationToken);
        byte[] base64;
        using (var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken))
        {
            var baseNode = doc.RootElement;

            var nullableBase64Element = baseNode.GetPropertyNullable("properties"u8);
            Debug.Assert(nullableBase64Element?.ValueKind is null or JsonValueKind.Array);
            base64 = nullableBase64Element?[0].GetPropertyNullable("value"u8)?.GetBytesFromBase64() ?? throw new InvalidOperationException();
        }
        using var skinDoc = JsonDocument.Parse(base64);
        
        var skinNode = skinDoc.RootElement;

        var skinUrl = skinNode
            .GetPropertyNullable("textures"u8)?
            .GetPropertyNullable("SKIN"u8)?
            .GetPropertyNullable("url"u8)?
            .GetString();

        return await HttpUtil.Request(skinUrl).GetStreamAsync(cancellationToken: cancellationToken);
    }
}