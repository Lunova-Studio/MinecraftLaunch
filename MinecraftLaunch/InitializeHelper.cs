using Flurl.Http;
using Flurl.Http.Configuration;
using MinecraftLaunch.Base.Models;
using MinecraftLaunch.Utilities;

namespace MinecraftLaunch;

public static class InitializeHelper
{
    public static void Initialize(Action<ComponentSettings> settingsProvider)
    {
        var componentSettings = new ComponentSettings();
        settingsProvider(componentSettings);

        DownloadManager.MaxThread = componentSettings.MaxThread;
        DownloadManager.MaxFragment = componentSettings.MaxFragment;
        DownloadManager.IsEnableMirror = componentSettings.IsEnableMirror;
        DownloadManager.IsEnableFragment = componentSettings.IsEnableFragment;
        DownloadManager.CurseforgeApiKey = componentSettings.CurseForgeApiKey;

        HttpUtil.FlurlClient = new FlurlClient
        {
            Settings = {
                Timeout = TimeSpan.FromSeconds(15),
                JsonSerializer = new DefaultJsonSerializer(JsonSerializerUtil.GetDefaultOptions()),
                Redirects = {
                    Enabled = true,
                }
            },
            Headers = {
                { "User-Agent", componentSettings.UserAgent },
            },
        };
    }
}