using MinecraftLaunch.Base.Enums;
using MinecraftLaunch.Base.Interfaces;
using MinecraftLaunch.Base.Models.Game;
using MinecraftLaunch.Base.Models.Network;
using MinecraftLaunch.Components.Downloader;
using MinecraftLaunch.Extensions;
using System.IO.Compression;

namespace MinecraftLaunch.Components.Installer.Modpack;

public sealed class ModrinthModpackInstaller : InstallerBase
{
    public string ModpackPath { get; init; }
    public MinecraftEntry Minecraft { get; init; }
    public override string MinecraftFolder { get; init; }
    public ModrinthModpackInstallEntry Entry { get; init; }

    public static ModrinthModpackInstallEntry ParseModpackInstallEntry(string modpackPath)
    {
        using var zipArchive = ZipFile.OpenRead(modpackPath);
        var json = zipArchive?.GetEntry("modrinth.index.json")?.ReadAsString()
            ?? throw new ArgumentException("Not found modrinth.index.json");

        return json.Deserialize(ModrinthModpackInstallEntryContext.Default.ModrinthModpackInstallEntry)
            ?? throw new InvalidOperationException("Failed to parse modrinth.index.json");
    }

    public static async Task<IInstallEntry> ParseModLoaderEntryAsync(ModrinthModpackInstallEntry modpack, CancellationToken cancellationToken = default)
    {
        if (modpack.Dependencies.ContainsKey("fabric-loader"))
            return (await FabricInstaller.EnumerableFabricAsync(modpack.McVersion, cancellationToken: cancellationToken))
                .First(x => x.BuildVersion.Equals(modpack.Dependencies["fabric-loader"]));
        else if (modpack.Dependencies.ContainsKey("quilt-loader"))
            return (await QuiltInstaller.EnumerableQuiltAsync(modpack.McVersion, cancellationToken))
                .First(x => x.BuildVersion.Equals(modpack.Dependencies["quilt-loader"]));
        else if (modpack.Dependencies.ContainsKey("forge"))
            return (await ForgeInstaller.EnumerableForgeAsync(modpack.McVersion, false, cancellationToken))
                .First(x => x.ForgeVersion.Equals(modpack.Dependencies["forge"]));
        else if (modpack.Dependencies.ContainsKey("neoforge"))
            return (await ForgeInstaller.EnumerableForgeAsync(modpack.McVersion, true, cancellationToken))
                .First(x => x.ForgeVersion.Equals(modpack.Dependencies["neoforge"]));
        else
            throw new NotSupportedException();
    }

    public static ModrinthModpackInstaller Create(string mcFolder, string modpackPath, ModrinthModpackInstallEntry installEntry, MinecraftEntry entry)
    {
        return new ModrinthModpackInstaller
        {
            Entry = installEntry,
            ModpackPath = modpackPath,
            MinecraftFolder = mcFolder,
            Minecraft = entry
        };
    }

    public override async Task<MinecraftEntry> InstallAsync(CancellationToken cancellationToken = default)
    {
        ReportProgress(InstallStep.Started, 0.0d, TaskStatus.WaitingToRun, 1, 1);

        try
        {
            var downloadRequests = ParseModFiles(cancellationToken);

            await DownloadModsAsync(downloadRequests, cancellationToken);
            await ExtractModpackAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            ReportCompleted(false, ex);
        }

        ReportProgress(InstallStep.RanToCompletion, 1.0d, TaskStatus.RanToCompletion, 1, 1);
        ReportCompleted(true);

        return Minecraft;
    }

    #region Privates

    private IEnumerable<DownloadRequest> ParseModFiles(CancellationToken cancellationToken)
    {
        int totalCount = Entry.Files.Count();
        ReportProgress(InstallStep.ParseDownloadUrls, 0.1d, TaskStatus.Running, totalCount, 0);

        int count = 0;
        string versionPath = Minecraft.ToWorkingPath(true);
        foreach (var file in Entry.Files.AsParallel())
        {
            cancellationToken.ThrowIfCancellationRequested();

            lock (Entry)
            {
                double progress = (double)Interlocked.Increment(ref count) / (double)totalCount;
                ReportProgress(InstallStep.ParseDownloadUrls, progress.ToPercentage(0.1d, 0.45d),
                    TaskStatus.Running, totalCount, count);
            }

            if (!file.Downloads.Any())
                continue;

            if (string.IsNullOrEmpty(file.Path))
                continue;

            var filePath = Path.Combine(versionPath, file.Path);
            yield return new DownloadRequest(file.Downloads.First(), filePath);
        }
    }

    private Task<GroupDownloadResult> DownloadModsAsync(IEnumerable<DownloadRequest> downloadRequests, CancellationToken cancellationToken)
    {
        List<Task> downloadTasks = [];

        var groupRequest = new GroupDownloadRequest(downloadRequests);

        groupRequest.ProgressChanged = args =>
        {
            ReportProgress(InstallStep.DownloadMods, args.Percentage.ToPercentage(0.45d, 0.7d),
                TaskStatus.Running, args.TotalCount, args.CompletedCount, args.Speed, true);
        };

        return new DefaultDownloader()
            .DownloadManyAsync(groupRequest, cancellationToken);
    }

    private async Task ExtractModpackAsync(CancellationToken cancellationToken)
    {
        var zipArchive = ZipFile.OpenRead(ModpackPath);
        var entries = zipArchive?.Entries;
        ReportProgress(InstallStep.ExtractModpack, 0.85d, TaskStatus.Running, entries.Count, 0);

        const string decompressPrefix = "overrides";
        string woringPath = Minecraft.ToWorkingPath(true);

        int count = 0;
        var tasks = entries.Select(x => Task.Run(() =>
        {
            lock (zipArchive)
            {
                ReportProgress(InstallStep.ExtractModpack,
                    ((double)Interlocked.Increment(ref count) / (double)entries.Count).ToPercentage(0.85d, 1.0d),
                    TaskStatus.Running, entries.Count, count);

                if (!x.FullName.StartsWith(decompressPrefix))
                    return;

                var subPath = x.FullName[(decompressPrefix.Length + 1)..];
                if (string.IsNullOrEmpty(subPath))
                    return;

                var filePath = new FileInfo(Path.Combine(woringPath, subPath));
                if (x.FullName.EndsWith('/'))
                {
                    filePath.Directory.Create();
                    return;
                }

                x.ExtractTo(filePath.FullName);
            }
        }, cancellationToken));

        await Task.WhenAll(tasks);
        zipArchive.Dispose();
    }

    #endregion Privates
}