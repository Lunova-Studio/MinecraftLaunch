using Flurl.Http;
using MinecraftLaunch.Base.EventArgs;
using MinecraftLaunch.Base.Interfaces;
using MinecraftLaunch.Base.Models.Game;
using MinecraftLaunch.Base.Models.Network;
using MinecraftLaunch.Extensions;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;

namespace MinecraftLaunch.Components.Downloader;

public sealed class MinecraftResourceDownloader
{
    private readonly MinecraftEntry _entry;
    private readonly DefaultDownloader _downloader;
    private readonly List<MinecraftDependency> _dependencies = [];

    public event EventHandler<ResourceDownloadProgressChangedEventArgs> ProgressChanged;

    internal int TotalCount { get; set; }
    public bool AllowVerifyAssets { get; init; } = true;
    public bool AllowInheritedDependencies { get; init; } = true;

    public MinecraftResourceDownloader(MinecraftEntry entry, IEnumerable<MinecraftDependency> extraDependencies = null)
    {
        if (extraDependencies is not null)
            _dependencies.AddRange(extraDependencies);

        _entry = entry;
        _downloader = new();
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

        #endregion 1.1 Libraries & Inherited Libraries

        #region 1.2 Client.jar

        var jar = _entry.GetJarElement();
        if (jar != null)
        {
            _dependencies.Add(jar);
        }

        #endregion 1.2 Client.jar

        #region 1.3 AssetIndex & Assets

        if (AllowVerifyAssets)
        {
            var assetIndex = _entry.GetAssetIndex();

            // 验证 AssetIndex 文件
            if (!VerifyDependency(assetIndex, cancellationToken))
            {
                await assetIndex.Url.DownloadFileAsync(Path.Combine(assetIndex.MinecraftFolderPath, "assets", "indexes"),
                    $"{assetIndex.Id}.json", 65536, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            }

            // 添加资源文件到依赖列表
            _dependencies.AddRange(_entry.GetRequiredAssets());
        }

        #endregion 1.3 AssetIndex & Assets

        // 2. 验证依赖项
        ConcurrentBag<MinecraftDependency> invalidDeps = [];
        Parallel.ForEach(_dependencies, new ParallelOptions { MaxDegreeOfParallelism = fileVerificationParallelism }, dep =>
        {
            if (!VerifyDependency(dep, cancellationToken))
            {
                invalidDeps.Add(dep);
            }
        });

        // 3. 下载无效的依赖项
        TotalCount = invalidDeps.Count;
        var downloadItems = invalidDeps
            .OfType<IDownloadDependency>()
            .Select(dep => new DownloadRequest(dep.Url, dep.FullPath, dep.Size ?? 0))
            .ToList();

        Debug.WriteLine(_dependencies.Where(x => x is FabricLibrary).Count());

        var groupDownloadRequest = new GroupDownloadRequest(downloadItems);
        groupDownloadRequest.ProgressChanged += args
            => ProgressChanged?.Invoke(this, args);

        return await _downloader.DownloadManyAsync(groupDownloadRequest, cancellationToken);
    }

    #region Privates

    private static bool VerifyDependency(MinecraftDependency dep, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Debug.WriteLineIf(dep is FabricLibrary, dep.FullPath);
        if (!File.Exists(dep.FullPath))
            return false;

        if (dep is not IVerifiableDependency verifiableDependency)
            return true;

        bool VerifySha1()
        {
            using var fileStream = File.OpenRead(dep.FullPath);
            byte[] sha1Bytes = SHA1.HashData(fileStream);

#if NET9_0_OR_GREATER
            string sha1Str = Convert.ToHexStringLower(sha1Bytes);
#else
            string sha1Str = BitConverter.ToString(sha1Bytes).Replace("-", string.Empty).ToLowerInvariant();
#endif

            return sha1Str == verifiableDependency.Sha1;
        }

        bool VerifySize()
        {
            var file = new FileInfo(dep.FullPath);
            return verifiableDependency.Size == file.Length;
        }

        if (verifiableDependency.Sha1 != null)
            return VerifySha1();
        else if (verifiableDependency.Size != null)
            return VerifySize();

        return true;
    }

    #endregion Privates
}