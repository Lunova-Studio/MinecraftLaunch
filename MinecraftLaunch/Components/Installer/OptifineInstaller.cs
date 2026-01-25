using Flurl.Http;
using MinecraftLaunch.Base.Enums;
using MinecraftLaunch.Base.Models.Game;
using MinecraftLaunch.Base.Models.Network;
using MinecraftLaunch.Components.Downloader;
using MinecraftLaunch.Components.Parser;
using MinecraftLaunch.Extensions;
using System.Diagnostics;
using System.IO.Compression;

namespace MinecraftLaunch.Components.Installer;

public sealed class OptifineInstaller : InstallerBase
{
    public string CustomId { get; init; }
    public string JavaPath { get; init; }
    public OptifineInstallEntry Entry { get; init; }
    public override string MinecraftFolder { get; init; }
    public MinecraftEntry InheritedMinecraft { get; init; }
    public MinecraftEntry Minecraft { get; set; }

    public static OptifineInstaller Create(string mcFolder, string javaPath, OptifineInstallEntry optifineInstallEntry, string customId = default)
    {
        return new OptifineInstaller
        {
            MinecraftFolder = mcFolder,
            Entry = optifineInstallEntry,
            CustomId = customId,
            JavaPath = javaPath,
        };
    }

    public static OptifineInstaller Create(string mcFolder, OptifineInstallEntry optifineInstallEntry, MinecraftEntry minecraft)
    {
        return new OptifineInstaller
        {
            MinecraftFolder = mcFolder,
            Entry = optifineInstallEntry,
            Minecraft = minecraft,
        };
    }

    public static async Task<IEnumerable<OptifineInstallEntry>> EnumerableOptifineAsync(string mcVersion, CancellationToken cancellationToken = default)
    {
        string url = $"https://bmclapi2.bangbang93.com/optifine/{mcVersion}";

        string json = await url.GetStringAsync(cancellationToken: cancellationToken);
        var entries = json.Deserialize(OptifineInstallEntryContext.Default.IEnumerableOptifineInstallEntry)
            .OrderByDescending(entry => entry.Patch);

        return entries;
    }

    public override async Task<MinecraftEntry> InstallAsync(CancellationToken cancellationToken = default)
    {
        FileInfo optifinePackageFile = default;
        ModifiedMinecraftEntry entry = default;
        MinecraftEntry inheritedEntry = default;

        ReportProgress(InstallStep.Started, 0.0d, TaskStatus.WaitingToRun, 1, 1);

        try
        {
            inheritedEntry = ParseMinecraft(cancellationToken);
            optifinePackageFile = await DownloadOptifinePackageAsync(cancellationToken);
            if (Minecraft is ModifiedMinecraftEntry modifiedMinecraft)
            {
                CopyToMods(optifinePackageFile);

                ReportProgress(InstallStep.RanToCompletion, 1.0d, TaskStatus.RanToCompletion, 1, 1);
                ReportCompleted(true);

                return modifiedMinecraft;
            }

            var (package, launchwrapperVersion, launchwrapperName) = ParseOptifinePackage(optifinePackageFile.FullName, cancellationToken);

            var optifineVersionJsonPath = await WriteVersionJsonAndSomeDependenciesAsync(inheritedEntry, launchwrapperVersion, launchwrapperName, package, cancellationToken);
            entry = ParseModifiedMinecraft(optifineVersionJsonPath, cancellationToken);
            await RunInstallProcessorAsync(optifinePackageFile.FullName, inheritedEntry, cancellationToken);

            ReportProgress(InstallStep.RanToCompletion, 1.0d, TaskStatus.RanToCompletion, 1, 1);
            ReportCompleted(true);
        }
        catch (Exception ex)
        {
            ReportProgress(InstallStep.Interrupted, 1.0d, TaskStatus.Canceled, 1, 1);
            ReportCompleted(false, ex);
        }

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

    private async Task<FileInfo> DownloadOptifinePackageAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ReportProgress(InstallStep.DownloadPackage, 0.2d, TaskStatus.Running, 1, 0);

        string packageUrl = $"https://bmclapi2.bangbang93.com/optifine/{Entry.McVersion}/{Entry.Type}/{Entry.Patch}";
        var packageFile = new FileInfo(Path.Combine(MinecraftFolder, Entry.FileName));

        var downloadRequest = new DownloadRequest(packageUrl,
            packageFile.FullName);

        await new DefaultDownloader()
            .DownloadAsync(downloadRequest, cancellationToken);

        ReportProgress(InstallStep.DownloadPackage, 0.3d, TaskStatus.Running, 1, 1);
        return packageFile;
    }

    private void CopyToMods(FileInfo packageInfo)
    {
        var fileInfo = new FileInfo(Path.Combine(Minecraft.ToWorkingPath(true), "mods", packageInfo.Name));

        if (!fileInfo.Directory.Exists)
            fileInfo.Directory.Create();

        packageInfo.MoveTo(fileInfo.FullName, true);
    }

    private (ZipArchive package, string launchwrapperVersion, string launchwrapperName) ParseOptifinePackage(string packageFilePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ReportProgress(InstallStep.ParsePackage, 0.3d, TaskStatus.Running, 1, 0);

        var packageArchive = ZipFile.OpenRead(packageFilePath);
        string launchwrapperVersion = packageArchive.GetEntry("launchwrapper-of.txt")?.ReadAsString() ?? "1.12";
        string launchwrapperName = launchwrapperVersion.Equals("1.12")
                ? "net.minecraft:launchwrapper:1.12"
                : $"optifine:launchwrapper-of:{launchwrapperVersion}";

        ReportProgress(InstallStep.ParsePackage, 0.45d, TaskStatus.Running, 1, 1);
        return (packageArchive, launchwrapperVersion, launchwrapperName);
    }

    private async Task<FileInfo> WriteVersionJsonAndSomeDependenciesAsync(
        MinecraftEntry minecraft,
        string launchwrapperVersion,
        string launchwrapperName,
        ZipArchive packageArchive,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ReportProgress(InstallStep.WriteVersionJsonAndSomeDependencies, 0.45d, TaskStatus.Running, 1, 0);

        if (launchwrapperVersion is not "1.12")
        {
            var launchwrapperJar = packageArchive.GetEntry($"launchwrapper-of-{launchwrapperVersion}.jar")
                ?? throw new FileNotFoundException("Invalid OptiFine package");

            launchwrapperJar.ExtractTo(Path.Combine(MinecraftFolder, "libraries", launchwrapperName.FormatLibraryNameToRelativePath()));
        }

        string entryId = CustomId ?? $"{Entry.McVersion}-Optifine_{Entry.Patch}";
        var jsonFile = new FileInfo(Path.Combine(MinecraftFolder, "versions", entryId, $"{entryId}.json"));

        if (!jsonFile.Directory!.Exists)
            jsonFile.Directory.Create();

        var time = Minecraft.ReleaseTime.ToString("s");
        var jsonEntry = new OptifineMinecraftEntry
        {
            Id = entryId,
            InheritsFrom = minecraft.Id,
            Time = time,
            ReleaseTime = time,
            Type = "release",
            Libraries = [
                new() { Name = $"optifine:Optifine:{Entry.McVersion}_{Entry.Type}_{Entry.Patch}" },
                new() { Name = launchwrapperName }
            ],
            MainClass = "net.minecraft.launchwrapper.Launch",
            MinecraftArguments = "--tweakClass optifine.OptiFineTweaker"
        };

        await File.WriteAllTextAsync(jsonFile.FullName,
            jsonEntry.Serialize(MinecraftJsonEntryContext.Default.OptifineMinecraftEntry), cancellationToken);

        if (minecraft.ClientJarPath is null || !File.Exists(minecraft.ClientJarPath))
            throw new FileNotFoundException("Unable to find the original client client.jar file");

        File.Copy(minecraft.ClientJarPath, jsonFile.FullName.Replace(".json", ".jar"), true);

        ReportProgress(InstallStep.WriteVersionJsonAndSomeDependencies, 0.60d, TaskStatus.Running, 1, 1);
        return jsonFile;
    }

    private ModifiedMinecraftEntry ParseModifiedMinecraft(FileInfo file, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var entry = MinecraftParser.Parse(file.Directory, null, out var _) as ModifiedMinecraftEntry;

        return entry ?? throw new InvalidOperationException("An incorrect modified entry was encountered");
    }

    private async Task RunInstallProcessorAsync(string packageFilePath, MinecraftEntry entry, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ReportProgress(InstallStep.RunInstallProcessor, 0.65d, TaskStatus.Running, 1, 0);

        string optifineLibName = $"optifine:Optifine:{Entry.McVersion}_{Entry.Type}_{Entry.Patch}";
        var optifineLibraryFile = new FileInfo(Path.Combine(MinecraftFolder, "libraries",
            optifineLibName.FormatLibraryNameToRelativePath()));

        if (!optifineLibraryFile.Directory!.Exists)
            optifineLibraryFile.Directory.Create();

        using var process = Process.Start(
            new ProcessStartInfo(JavaPath)
            {
                UseShellExecute = false,
                WorkingDirectory = MinecraftFolder,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                Arguments = string.Join(
                    " ",
                    [
                        "-cp",
                        packageFilePath.ToPath(),
                        "optifine.Patcher",
                        entry.ClientJarPath.ToPath(),
                        packageFilePath.ToPath(),
                        optifineLibraryFile.FullName.ToPath()
                    ])
            }) ?? throw new InvalidOperationException("Unable to run the compilation process");

        process.BeginErrorReadLine();
        process.BeginOutputReadLine();

        await process.WaitForExitAsync(cancellationToken);
        ReportProgress(InstallStep.RunInstallProcessor, 1.0d, TaskStatus.Running, 1, 1);
    }

    #endregion Privates
}