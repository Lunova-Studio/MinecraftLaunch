using MinecraftLaunch.Base.Interfaces;
using System.Collections.Frozen;

namespace MinecraftLaunch;

public static class DownloadManager
{
    public static string CurseforgeApiKey { get; set; } = string.Empty;

    public static int MaxThread { get; set; } = 64;
    public static int MaxFragment { get; set; } = 128;
    public static int MaxRetryCount { get; set; } = 8;

    public static bool IsEnableMirror { get; set; }
    public static bool IsEnableFragment { get; set; } = true;

    public static readonly IDownloadMirror BmclApi = new BmclApiSource();
}

public sealed class BmclApiSource : IDownloadMirror
{
    private static readonly FrozenDictionary<string, string> _replacementMap = new Dictionary<string, string> {
        { "https://resources.download.minecraft.net", "https://bmclapi2.bangbang93.com/assets" },
        { "https://piston-meta.mojang.com", "https://bmclapi2.bangbang93.com" },
        { "https://launchermeta.mojang.com", "https://bmclapi2.bangbang93.com" },
        { "https://launcher.mojang.com" , "https://bmclapi2.bangbang93.com" },
        { "https://libraries.minecraft.net", "https://bmclapi2.bangbang93.com/maven" },
        { "https://maven.minecraftforge.net", "https://bmclapi2.bangbang93.com/maven" },
        { "https://files.minecraftforge.net/maven", "https://bmclapi2.bangbang93.com/maven" },
        { "https://maven.fabricmc.net", "https://bmclapi2.bangbang93.com/maven" },
        { "https://meta.fabricmc.net", "https://bmclapi2.bangbang93.com/fabric-meta" },
        { "https://maven.neoforged.net/releases/net/neoforged/forge", "https://bmclapi2.bangbang93.com/maven/net/neoforged/forge" }
    }.ToFrozenDictionary();

    public string TryFindUrl(string sourceUrl)
    {
        if (!DownloadManager.IsEnableMirror)
            return sourceUrl;

        foreach (var (src, mirror) in _replacementMap)
            if (sourceUrl.StartsWith(src))
                return sourceUrl.Replace(src, mirror);

        return sourceUrl;
    }
}