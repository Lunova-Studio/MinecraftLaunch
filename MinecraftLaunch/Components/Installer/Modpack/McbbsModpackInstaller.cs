using MinecraftLaunch.Base.Enums;
using MinecraftLaunch.Base.Interfaces;
using MinecraftLaunch.Base.Models.Game;
using MinecraftLaunch.Base.Models.Network;
using MinecraftLaunch.Extensions;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace MinecraftLaunch.Components.Installer.Modpack;

public sealed class McbbsModpackInstaller : InstallerBase {
    public string ModpackPath { get; init; }
    public MinecraftEntry Minecraft { get; init; }
    public McbbsModpackInstallEntry Entry { get; init; }
    public override string MinecraftFolder { get; init; }

    public static McbbsModpackInstaller Create(string mcFolder, string modpackPath, McbbsModpackInstallEntry installEntry, MinecraftEntry entry) {
        return new McbbsModpackInstaller {
            MinecraftFolder = mcFolder,
            ModpackPath = modpackPath,
            Entry = installEntry,
            Minecraft = entry
        };
    }

    public static async IAsyncEnumerable<IInstallEntry> ParseModLoaderEntryAsync(McbbsModpackInstallEntry modpack, [EnumeratorCancellation] CancellationToken cancellationToken = default) {
        foreach (var addon in modpack.Addons) {
            cancellationToken.ThrowIfCancellationRequested();

            IInstallEntry entry = addon.Id switch {
                "fabric" => (await FabricInstaller.EnumerableFabricAsync(modpack.McVersion, cancellationToken))
                    .First(x => x.BuildVersion.Equals(addon.Version)),
                "quilt" => (await QuiltInstaller.EnumerableQuiltAsync(modpack.McVersion, cancellationToken))
                    .First(x => x.BuildVersion.Equals(addon.Version)),
                "forge" => (await ForgeInstaller.EnumerableForgeAsync(modpack.McVersion, false, cancellationToken))
                    .First(x => x.ForgeVersion.Equals(addon.Version)),
                "neoforge" => (await ForgeInstaller.EnumerableForgeAsync(modpack.McVersion, true, cancellationToken))
                    .First(x => x.ForgeVersion.Equals(addon.Version)),
                "optifine" => (await OptifineInstaller.EnumerableOptifineAsync(modpack.McVersion, cancellationToken))
                    .First(x => addon.Version.Contains(x.Type)),
                _ => null
            };

            if (entry != null)
                yield return entry;
        }
    }

    public static McbbsModpackInstallEntry ParseModpackInstallEntry(string modpackPath) {
        using var zipArchive = ZipFile.OpenRead(modpackPath);
        using var stream = zipArchive?.GetEntry("mcbbs.packmeta")?.Open()
            ?? throw new ArgumentException("Not found mcbbs.packmeta");

        return JsonSerializer.Deserialize(stream,McbbsModpackInstallEntryContext.Default.McbbsModpackInstallEntry)
            ?? throw new InvalidOperationException("Failed to parsemcbbs.packmeta");
    }

    public override async Task<MinecraftEntry> InstallAsync(CancellationToken cancellationToken = default) {
        ReportProgress(InstallStep.Started, 0.0d, TaskStatus.WaitingToRun, 1, 1);

        try {
            await ExtractModpackAsync(cancellationToken);

            ReportProgress(InstallStep.RanToCompletion, 1.0d, TaskStatus.RanToCompletion, 1, 1);
            ReportCompleted(true);
        } catch (Exception ex) {
            ReportProgress(InstallStep.Interrupted, 1.0d, TaskStatus.Canceled, 1, 1);
            ReportCompleted(false, ex);
        }

        return Minecraft;
    }

    #region Privates

    private async Task ExtractModpackAsync(CancellationToken cancellationToken) {
      
        ReportProgress(InstallStep.ExtractModpack, 0.85d, TaskStatus.Running, 0, 0); // 此处未开始解析,返回0

        const string decompressPrefix = "overrides";

        var count = 0; 
        await ModPackUtils.ExtractSingleThreadAsync(
            srcZipPath: ModpackPath,
            overridesPrefix: decompressPrefix,
            independentAndFullWorkingPath: Minecraft.ToWorkingPath(true),
            whenEachEntryCompleted: ReportEntryExtractingProgress,
            cancellationToken: cancellationToken);
        return;
        

        void ReportEntryExtractingProgress(ZipArchive zipArchive) =>
            ReportProgress(
                step: InstallStep.ExtractModpack, 
                progress: (Interlocked.Increment(ref count) / (double)zipArchive.Entries.Count).ToPercentage(0.85d, 1.0d),
                status:  TaskStatus.Running,
                totalCount: zipArchive.Entries.Count,
                finshedCount: count);
    }

    #endregion
}