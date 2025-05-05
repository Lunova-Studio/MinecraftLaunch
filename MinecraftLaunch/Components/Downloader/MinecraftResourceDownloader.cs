using MinecraftLaunch.Base.Enums;
using MinecraftLaunch.Base.EventArgs;
using MinecraftLaunch.Base.Interfaces;
using MinecraftLaunch.Base.Models.Game;
using MinecraftLaunch.Base.Models.Network;
using MinecraftLaunch.Extensions;
using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace MinecraftLaunch.Components.Downloader;

public sealed class MinecraftResourceDownloader {
    private readonly MinecraftEntry _entry;
    private readonly FileDownloader _downloader;
    private readonly List<MinecraftDependency> _dependencies = [];

    public event EventHandler<ResourceDownloadProgressChangedEventArgs> ProgressChanged;

    internal int TotalCount { get; set; }
    public bool AllowVerifyAssets { get; init; } = true;
    public bool AllowInheritedDependencies { get; init; } = true;

    public MinecraftResourceDownloader(MinecraftEntry entry, int maxThread = 64, IEnumerable<MinecraftDependency> extraDependencies = null) {
        if (extraDependencies is not null)
            _dependencies.AddRange(extraDependencies);

        _entry = entry;
        _downloader = new(maxThread);
    }

    public async Task<GroupDownloadResult> VerifyAndDownloadDependenciesAsync(int fileVerificationParallelism = 10, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(fileVerificationParallelism);

        #region 1.1 Libraries & Inherited Libraries

        var (libs, nativeLibs) = _entry.GetRequiredLibraries();
        _dependencies.AddRange(libs);
        _dependencies.AddRange(nativeLibs);

        if (AllowInheritedDependencies
            && _entry is ModifiedMinecraftEntry modInstance
            && modInstance.HasInheritance)
        {
            (libs, nativeLibs) = modInstance.InheritedMinecraft.GetRequiredLibraries();
            _dependencies.AddRange(libs);
            _dependencies.AddRange(nativeLibs);
        }

        #endregion

        #region 1.2 Client.jar

        var jar = _entry.GetJarElement();
        if (jar != null)
        {
            _dependencies.Add(jar);
        }

        #endregion

        #region 1.3 AssetIndex & Assets

        if (AllowVerifyAssets)
        {
            var assetIndex = _entry.GetAssetIndex();

            // 验证 AssetIndex 文件
            if (!VerifyDependency(assetIndex, cancellationToken))
            {
                var result = await _downloader
                    .DownloadFileAsync(new(assetIndex.Url, assetIndex.FullPath), cancellationToken);

                if (result.Type == DownloadResultType.Failed)
                {
                    throw new Exception("Failed to obtain the dependent material index file");
                }
            }

            // 添加资源文件到依赖列表
            _dependencies.AddRange(_entry.GetRequiredAssets());
        }

        #endregion

        // 2. 验证依赖项
        ConcurrentBag<MinecraftDependency> invalidDeps = new();
        Parallel.ForEach(_dependencies, new ParallelOptions { MaxDegreeOfParallelism = fileVerificationParallelism }, dep => {
            if (!VerifyDependency(dep, cancellationToken))
            {
                invalidDeps.Add(dep);
            }
        });

        // 3. 下载无效的依赖项
        TotalCount = invalidDeps.Count;
        var downloadItems = invalidDeps
            .OfType<IDownloadDependency>()
            .Select(dep => new DownloadRequest(dep.Url, dep.FullPath))
            .ToList();

        int currentCount = 0;
        double speed = 0;
        int totalCount = downloadItems.Count;

        var groupRequest = new GroupDownloadRequest(downloadItems);
        groupRequest.DownloadSpeedChanged += arg => speed = arg;

        groupRequest.SingleRequestCompleted += (request, result) => {
            Interlocked.Increment(ref currentCount);
            ProgressChanged?.Invoke(this, new ResourceDownloadProgressChangedEventArgs
            {
                Speed = speed,
                TotalCount = totalCount,
                CompletedCount = currentCount,
            });
        };

        // 增加下载失败的重试机制
        GroupDownloadResult downloadResult = await _downloader.DownloadFilesAsync(groupRequest, cancellationToken);
        if (downloadResult.Type == DownloadResultType.Failed && downloadResult.Failed.Count > 0)
        {
            var failedItems = downloadResult.Failed.Keys
                .Select(req => new DownloadRequest(req.Url, req.FileInfo.FullName))
                .ToList();

            var retryRequest = new GroupDownloadRequest(failedItems);
            retryRequest.DownloadSpeedChanged += arg => speed = arg;

            retryRequest.SingleRequestCompleted += (request, result) => {
                Interlocked.Increment(ref currentCount);
                ProgressChanged?.Invoke(this, new ResourceDownloadProgressChangedEventArgs
                {
                    Speed = speed,
                    TotalCount = totalCount,
                    CompletedCount = currentCount,
                });
            };

            // 重试下载失败的文件
            var retryResult = await _downloader.DownloadFilesAsync(retryRequest, cancellationToken);
            if (retryResult.Type == DownloadResultType.Failed)
            {
                throw new Exception("Some dependencies failed to download after retrying.");
            }
        }

        return downloadResult;
    }

    #region Privates

    private static bool VerifyDependency(MinecraftDependency dep, CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(dep.FullPath))
            return false;

        if (dep is not IVerifiableDependency verifiableDependency)
            return true;

        bool VerifySha1() {
            using var fileStream = File.OpenRead(dep.FullPath);
            byte[] sha1Bytes = SHA1.HashData(fileStream);
            string sha1Str = BitConverter.ToString(sha1Bytes).Replace("-", string.Empty).ToLower();

            return sha1Str == verifiableDependency.Sha1;
        }

        bool VerifySize() {
            var file = new FileInfo(dep.FullPath);
            return verifiableDependency.Size == file.Length;
        }

        if (verifiableDependency.Sha1 != null)
            return VerifySha1();
        else if (verifiableDependency.Size != null)
            return VerifySize();

        return true;
    }

    #endregion
}