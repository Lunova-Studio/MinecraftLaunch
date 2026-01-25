using MinecraftLaunch.Base.Enums;
using MinecraftLaunch.Base.Interfaces;
using MinecraftLaunch.Base.Models.Game;
using MinecraftLaunch.Base.Models.Network;
using MinecraftLaunch.Components.Downloader;
using MinecraftLaunch.Components.Provider;
using MinecraftLaunch.Extensions;
using System.IO.Compression;
using System.Runtime.CompilerServices;

namespace MinecraftLaunch.Components.Installer.Modpack;

public sealed class CurseforgeModpackInstaller : InstallerBase
{
    public string ModpackPath { get; init; }
    public MinecraftEntry Minecraft { get; init; }
    public override string MinecraftFolder { get; init; }
    public CurseforgeModpackInstallEntry Entry { get; init; }

    [Obsolete("Implemented processing method")]
    public List<long> FaildParseModProjectId { get; set; } = [];

    public static CurseforgeModpackInstaller Create(string mcFolder, string modpackPath, CurseforgeModpackInstallEntry installEntry, MinecraftEntry entry)
    {
        return new CurseforgeModpackInstaller
        {
            Minecraft = entry,
            Entry = installEntry,
            ModpackPath = modpackPath,
            MinecraftFolder = mcFolder
        };
    }

    public static CurseforgeModpackInstallEntry ParseModpackInstallEntry(string modpackPath)
    {
        using var zipArchive = ZipFile.OpenRead(modpackPath);
        var json = zipArchive?.GetEntry("manifest.json")?.ReadAsString()
            ?? throw new ArgumentException("Not found manifest.json");

        var entry = json.Deserialize(CurseforgeModpackInstallEntryContext.Default.CurseforgeModpackInstallEntry)
            ?? throw new InvalidOperationException("Failed to parse manifest.json");

        return entry;
    }

    public static async IAsyncEnumerable<IInstallEntry> ParseModLoaderEntryByManifestAsync(CurseforgeModpackInstallEntry entry, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var loader in entry.Minecraft.ModLoaders)
        {
            cancellationToken.ThrowIfCancellationRequested();

            (bool isPrimary, string id) = (loader.GetBool("primary"), loader.GetString("id"));
            var idDatas = id.Split('-');

            var loaderVersion = idDatas.Last();
            var loaderType = idDatas.First() switch
            {
                "forge" => ModLoaderType.Forge,
                "fabric" => ModLoaderType.Fabric,
                "neoforge" => ModLoaderType.NeoForge,
                _ => throw new NotSupportedException("Unsupported installer type")
            };

            IInstallEntry installEntry = loaderType switch
            {
                ModLoaderType.Forge => (await ForgeInstaller.EnumerableForgeAsync(entry.McVersion, cancellationToken: cancellationToken))
                    .First(x => x.ForgeVersion.Equals(loaderVersion)),

                ModLoaderType.Fabric => (await FabricInstaller.EnumerableFabricAsync(entry.McVersion, cancellationToken: cancellationToken))
                    .First(x => x.BuildVersion.Equals(loaderVersion)),

                ModLoaderType.NeoForge => (await ForgeInstaller.EnumerableForgeAsync(entry.McVersion, true, cancellationToken))
                    .First(x => x.ForgeVersion.Equals(loaderVersion)),

                _ => throw new NotImplementedException()
            };

            yield return installEntry ?? throw new InvalidOperationException();
        }
    }

    public override async Task<MinecraftEntry> InstallAsync(CancellationToken cancellationToken = default)
    {
        ReportProgress(InstallStep.Started, 0.0d, TaskStatus.WaitingToRun, 1, 1);

        try
        {
            var modInfoGroup = (await ParseModFilesAsync(cancellationToken))
                .ToLookup(x => string.IsNullOrEmpty(x.url));

            var downloadUrls = modInfoGroup[false].Select(x => x.url).ToList();
            var invalidMods = modInfoGroup[true].Select(x => x.invalidMod).ToList();

            var redirectownloadUrls = RedirectInvalidModsAsync(invalidMods, cancellationToken);
            await foreach (var downloadUrl in redirectownloadUrls)
                downloadUrls.Add(downloadUrl);

            await DownloadModsAsync(downloadUrls, cancellationToken);
            await ExtractModpackAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            ReportProgress(InstallStep.Interrupted, 1.0d, TaskStatus.Canceled, 1, 1);
            ReportCompleted(false, ex);
        }

        ReportProgress(InstallStep.RanToCompletion, 1.0d, TaskStatus.RanToCompletion, 1, 1);
        ReportCompleted(true);

        return Minecraft;
    }

    #region Privates

    [Obsolete]
    private void ParseMinecraft(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ReportProgress(InstallStep.ParseMinecraft, 0.05d, TaskStatus.Running, 1, 0);

        if (Minecraft is not null && Minecraft is ModifiedMinecraftEntry && Minecraft.Version.VersionId.Equals(Entry.McVersion))
        {
            ReportProgress(InstallStep.ParseMinecraft, 0.1d, TaskStatus.Running, 1, 1);
            return;
        }

        throw new NotSupportedException("Your entry is incorrect or does not exist");
    }

    private async Task<IEnumerable<(string url, CurseforgeModpackFileEntry invalidMod)>> ParseModFilesAsync(CancellationToken cancellationToken)
    {
        int count = 0;
        int totalCount = Entry.ModFiles.Count();
        List<Task> requestTasks = [];
        List<(string, CurseforgeModpackFileEntry)> downloadInfoGroup = [];
        SemaphoreSlim semaphoreSlim = new(256, 256);

        ReportProgress(InstallStep.ParseDownloadUrls, 0.1d, TaskStatus.Running, totalCount, count);

        foreach (var modpackFile in Entry.ModFiles)
        {
            string downloadUrl = string.Empty;

            requestTasks.Add(Task.Run(async () =>
            {
                await semaphoreSlim.WaitAsync(cancellationToken);
                if (modpackFile.IsRequired)
                    downloadUrl = await CurseforgeProvider.GetModDownloadUrlAsync(modpackFile.ProjectId, modpackFile.FileId, cancellationToken);
                else return;

                lock (requestTasks)
                {
                    var progress = (double)Interlocked.Increment(ref count) / (double)totalCount;
                    ReportProgress(InstallStep.ParseDownloadUrls, progress.ToPercentage(0.1d, 0.5d),
                        TaskStatus.Running, totalCount, count);

                    downloadInfoGroup.Add(new(downloadUrl, modpackFile));
                }

                semaphoreSlim.Release();
            }, cancellationToken));
        }

        await Task.WhenAll(requestTasks);
        return downloadInfoGroup;
    }

    private async IAsyncEnumerable<string> RedirectInvalidModsAsync(IEnumerable<CurseforgeModpackFileEntry> modpacks, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ReportProgress(InstallStep.RedirectInvalidMod, 0.5d, TaskStatus.Running, modpacks.Count(), 0);

        int count = 0;
        int totalCount = modpacks.Count();
        foreach (var modpackFile in modpacks)
        {
            var modFileName = (await CurseforgeProvider
                .GetModFileEntryAsync(modpackFile.ProjectId, modpackFile.FileId, cancellationToken))
                .GetString("fileName");

            lock (modpacks)
            {
                var progress = (double)count / (double)totalCount;

                ReportProgress(InstallStep.RedirectInvalidMod, progress.ToPercentage(0.5d, 0.6d), TaskStatus.Running, totalCount,
                    Interlocked.Increment(ref count));
            }

            yield return await CurseforgeProvider.TestDownloadUrlAsync(modpackFile.FileId, modFileName, cancellationToken);
        }
    }

    private async Task DownloadModsAsync(IEnumerable<string> asyncUrls, CancellationToken cancellationToken)
    {
        List<Task> downloadTasks = [];
        var urls = asyncUrls.ToList();

        var modsPath = new DirectoryInfo(Path.Combine(Minecraft.ToWorkingPath(true), "mods"));
        if (!modsPath.Exists)
            modsPath.Create();

        var groupRequest = new GroupDownloadRequest(urls
            .Select(x => new DownloadRequest(x, Path.Combine(modsPath.FullName,
                Path.GetFileName(x)))));

        groupRequest.ProgressChanged = args =>
            ReportProgress(InstallStep.DownloadMods, args.Percentage.ToPercentage(0.6d, 0.85d),
                TaskStatus.Running, args.TotalCount, args.CompletedCount, args.Speed, true);

        ReportProgress(InstallStep.DownloadMods, 0.6d, TaskStatus.Running,
            urls.Count, 0, 0, false);

        await new DefaultDownloader().DownloadManyAsync(groupRequest, cancellationToken);
    }

    private async Task ExtractModpackAsync(CancellationToken cancellationToken)
    {
        var zipArchive = ZipFile.OpenRead(ModpackPath);
        var entries = zipArchive?.Entries;
        ReportProgress(InstallStep.ExtractModpack, 0.85d, TaskStatus.Running, entries.Count, 0);

        int count = 0;
        var tasks = entries.Select(x => Task.Run(() =>
        {
            lock (zipArchive)
            {
                ReportProgress(InstallStep.ExtractModpack,
                    ((double)Interlocked.Increment(ref count) / (double)entries.Count).ToPercentage(0.85d, 1.0d),
                    TaskStatus.Running, entries.Count, count);

                if (!Entry.IsOverride ||
                    !x.FullName.StartsWith(Entry.Overrides, StringComparison.OrdinalIgnoreCase)) return;

                var subPath = x.FullName[(Entry.Overrides.Length + 1)..];
                if (string.IsNullOrEmpty(subPath))
                    return;

                var filePath = new FileInfo(Path.Combine(Path.GetFullPath(Minecraft.ToWorkingPath(true)), subPath));
                if (x.FullName.EndsWith('/'))
                {
                    Directory.CreateDirectory(filePath.FullName);
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