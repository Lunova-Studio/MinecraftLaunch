using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using System.Net.Sockets;
using MinecraftLaunch.Extensions;

namespace MinecraftLaunch.Components.Installer.Modpack;

internal static class ModPackUtils
{
    public const char ZipPathSeparator = '/';
    

    public static async Task ExtractSingleThreadAsync(
        string srcZipPath,
        string overridesPrefix,
        string independentAndFullWorkingPath,
        /*执行线程不保证*/Action<ZipArchive> whenEachEntryCompleted = null,
        CancellationToken cancellationToken = default)
    {
        using var zip = ZipFile.OpenRead(srcZipPath);
        foreach (var item in zip.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsShouldExtract(item, overridesPrefix)) continue;
            await item.ExtractToFileAsync(
                Path.Combine(
                    independentAndFullWorkingPath,
                    RemoveOverridesPrefix(item.FullName, overridesPrefix)),
                overwrite:true,cancellationToken
            ).ConfigureAwait(false);
            whenEachEntryCompleted?.Invoke(zip);
        }
    }

    #region Util

    private static bool IsShouldExtract(
        ZipArchiveEntry entry,
        string overridesPrefix)
    {
        // 排除目录
        if (entry.FullName.EndsWith(ZipPathSeparator)) return false;
        // 排除非 <overrides>/文件
        if (!entry.FullName.StartsWith(overridesPrefix, StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }

    // 修正路径
    private static string RemoveOverridesPrefix(
        string source,
        string overridesPrefix)
    {
        if (overridesPrefix.EndsWith('/'))
        {
            return source[overridesPrefix.Length..];
        }

        // 补充/
        return source[(overridesPrefix.Length + 1)..];
    }

    #endregion
}
