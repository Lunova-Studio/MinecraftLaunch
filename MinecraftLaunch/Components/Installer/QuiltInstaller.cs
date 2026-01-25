using Flurl.Http;
using MinecraftLaunch.Base.Enums;
using MinecraftLaunch.Base.Models.Game;
using MinecraftLaunch.Base.Models.Network;
using MinecraftLaunch.Components.Downloader;
using MinecraftLaunch.Components.Parser;
using MinecraftLaunch.Extensions;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace MinecraftLaunch.Components.Installer;

public sealed class QuiltInstaller : InstallerBase
{
    public string CustomId { get; init; }
    public QuiltInstallEntry Entry { get; init; }
    public override string MinecraftFolder { get; init; }
    public MinecraftEntry InheritedMinecraft { get; init; }

    public static QuiltInstaller Create(string mcFolder, QuiltInstallEntry installEntry, string customId = default)
    {
        return new QuiltInstaller
        {
            CustomId = customId,
            Entry = installEntry,
            MinecraftFolder = mcFolder,
        };
    }

    public static async Task<IEnumerable<QuiltInstallEntry>> EnumerableQuiltAsync(string mcVersion, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        string json = await $"https://meta.quiltmc.org/v3/versions/loader/{mcVersion}"
            .GetStringAsync(cancellationToken: cancellationToken);

        var entries = json.Deserialize(QuiltInstallEntryContext.Default.IEnumerableQuiltInstallEntry);
        return entries;
    }

    public override async Task<MinecraftEntry> InstallAsync(CancellationToken cancellationToken = default)
    {
        ModifiedMinecraftEntry entry = default;
        MinecraftEntry inheritedEntry = default;

        ReportProgress(InstallStep.Started, 0.0d, TaskStatus.WaitingToRun, 1, 1);

        try
        {
            inheritedEntry = ParseMinecraft(cancellationToken);

            var jsonFile = await DownloadVersionJsonAsync(inheritedEntry, cancellationToken);
            entry = ParseModifiedMinecraft(jsonFile, cancellationToken);
            await CompleteQuiltLibrariesAsync(entry, cancellationToken);
        }
        catch (Exception ex)
        {
            ReportProgress(InstallStep.Interrupted, 1.0d, TaskStatus.Canceled, 1, 1);
            ReportCompleted(false, ex);
        }

        ReportProgress(InstallStep.RanToCompletion, 1.0d, TaskStatus.RanToCompletion, 1, 1);
        ReportCompleted(true);
        return entry ?? throw new ArgumentNullException(nameof(entry), "Unexpected null reference to variable");
    }

    #region Privates

    private MinecraftEntry ParseMinecraft(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ReportProgress(InstallStep.ParseMinecraft, 0.10d, TaskStatus.Running, 1, 0);

        if (InheritedMinecraft is not null)
        {
            return InheritedMinecraft;
        }

        var inheritedMinecraft = new MinecraftParser(MinecraftFolder).GetMinecrafts()
            .FirstOrDefault(x => x.Version.VersionId == Entry.McVersion);

        ReportProgress(InstallStep.ParseMinecraft, 0.15d, TaskStatus.Running, 1, 1);
        return inheritedMinecraft ?? throw new InvalidOperationException("The corresponding version's parent was not found."); ;
    }

    private async Task<FileInfo> DownloadVersionJsonAsync(MinecraftEntry entry, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ReportProgress(InstallStep.DownloadVersionJson, 0.20d, TaskStatus.Running, 1, 0);

        string requestUrl = $"https://meta.quiltmc.org/v3/versions/loader/{Entry.McVersion}/{Entry.BuildVersion}/profile/json";
        requestUrl = DownloadManager.BmclApi.TryFindUrl(requestUrl);

        var json = await requestUrl.GetStringAsync(HttpCompletionOption.ResponseContentRead, cancellationToken);
        string entryId = CustomId ??
            json.AsNode().GetString("id") ??
            $"quilt-loader-{Entry.Loader.Version}_{entry.Id}";

        var jsonFile = new FileInfo(Path
            .Combine(MinecraftFolder, "versions", entryId, $"{entryId}.json"));

        if (!jsonFile.Directory!.Exists)
            jsonFile.Directory.Create();

        await File.WriteAllTextAsync(jsonFile.FullName, json, cancellationToken);

        ReportProgress(InstallStep.DownloadVersionJson, 0.45d, TaskStatus.Running, 1, 1);
        return jsonFile;
    }

    private ModifiedMinecraftEntry ParseModifiedMinecraft(FileInfo file, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var entry = MinecraftParser.Parse(file.Directory, null, out var _) as ModifiedMinecraftEntry;

        return entry ?? throw new InvalidOperationException("An incorrect modified entry was encountered");
    }

    private async Task CompleteQuiltLibrariesAsync(MinecraftEntry minecraft, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ReportProgress(InstallStep.DownloadLibraries, 0.5d, TaskStatus.Running, 0, 0);

        var resourceDownloader = new MinecraftResourceDownloader(minecraft);

        resourceDownloader.ProgressChanged += (_, x)
            => ReportProgress(InstallStep.DownloadLibraries, x.ToPercentage().ToPercentage(0.5d, 0.95d),
                TaskStatus.Running, resourceDownloader.TotalCount,
                    x.CompletedCount, x.Speed, true);

        await resourceDownloader.VerifyAndDownloadDependenciesAsync(cancellationToken: cancellationToken);

        //if (groupDownloadResult.Failed.Count > 0)
        //    throw new InvalidOperationException("Some dependent files encountered errors during download");
    }

    #endregion Privates
}