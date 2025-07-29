using Flurl.Http;
using Flurl.Http.Configuration;
using MinecraftLaunch.Base.Models;
using MinecraftLaunch.Utilities;

namespace MinecraftLaunch;

public static class InitializeHelper {
    public static void Initialize(Action<ComponentSettings> settingsProvider) {
        var componentSettings = new ComponentSettings();
        settingsProvider(componentSettings);

        DownloadManager.MaxThread = componentSettings.MaxThread;
        DownloadManager.MaxFragmented = componentSettings.MaxFragmented;
        DownloadManager.IsEnableMirror = componentSettings.IsEnableMirror;
        DownloadManager.CurseforgeApiKey = componentSettings.CurseForgeApiKey;

        HttpUtil.FlurlClient = new FlurlClient {
            Settings = {
                Timeout = TimeSpan.FromMinutes(1),
                JsonSerializer = new DefaultJsonSerializer(JsonSerializerUtil.GetDefaultOptions()),
            },
            Headers = {
                { "User-Agent", componentSettings.UserAgent },
            },
        };
    }
}