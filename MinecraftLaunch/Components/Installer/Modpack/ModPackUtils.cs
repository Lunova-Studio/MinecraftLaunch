using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using System.Net.Sockets;
using MinecraftLaunch.Extensions;

namespace MinecraftLaunch.Components.Installer.Modpack;

internal static class ModPackUtils
{
    public const char ZipPathSeparator = '/';
    

    public static async Task ExtractParallelAccelerationAsync(
        string srcZipPath,
        string overridesPrefix,
        string independentAndFullWorkingPath,
        CancellationToken cancelToken = default)
    {
        // 从-1开始即第一次递增后为0
        const int startOffset = -1;
        //Init
        var parallelCount = Environment.ProcessorCount * 2;
        var taskThreads = new Task[parallelCount];
        var zips = new ZipArchive[parallelCount];
        var targetOffset = startOffset;
        var concurrentOffset = startOffset;
        for (var i = 0; i < parallelCount; i++)
        {
            zips[i] = ZipFile.OpenRead(srcZipPath);
            // 首次循环读取长度
            if (i is 0) targetOffset = zips[0].Entries.Count;
            // 拷贝tid用于不同实例闭包
            var taskThreadId = i;
            taskThreads[i] = Task.Run(ExtractManyJob, cancelToken);
            continue;

            // main job
            async Task ExtractManyJob()
            {
                while (true)
                {
                    cancelToken.ThrowIfCancellationRequested();
                    var entryIndex = Interlocked.Increment(ref concurrentOffset);
                    // entries已全部复制
                    // targetOffset不会被修改
                    // ReSharper disable once AccessToModifiedClosure
                    if (entryIndex >= targetOffset) return;
                    var entry = zips[taskThreadId].Entries[entryIndex];
                    if (!IsShouldExtract(entry, overridesPrefix)) continue;
                    // 获取路径
                    var dstPath = Path.Combine(independentAndFullWorkingPath,
                        RemoveOverridesPrefix(entry.FullName, overridesPrefix));
                    // 复制
                    await entry.ExtractToFileAsync(dstPath);
                }
            }
        }

        // 确保资源释放,Task只会在await时重新抛出
        try
        {
            await Task.WhenAll(taskThreads);
        }
        finally
        {
            foreach (var zip in zips) zip.Dispose();
        }
    }

    public static async Task ExtractSingleThreadAsync(
        string srcZipPath,
        string overridesPrefix,
        string independentAndFullWorkingPath,
        CancellationToken cancelToken = default)
    {
        using var zip = ZipFile.OpenRead(srcZipPath);
        foreach (var item in zip.Entries)
        {
            cancelToken.ThrowIfCancellationRequested();
            if (!IsShouldExtract(item, overridesPrefix)) continue;
            await item.ExtractToFileAsync(
                Path.Combine(
                    independentAndFullWorkingPath,
                    RemoveOverridesPrefix(item.FullName, overridesPrefix))
            );
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