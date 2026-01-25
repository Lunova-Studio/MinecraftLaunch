using Flurl.Http;
using MinecraftLaunch.Base.Enums;
using MinecraftLaunch.Base.Interfaces;
using MinecraftLaunch.Base.Models.Game;
using MinecraftLaunch.Base.Models.Network;
using MinecraftLaunch.Components.Downloader;
using MinecraftLaunch.Components.Parser;
using MinecraftLaunch.Extensions;
using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace MinecraftLaunch.Components.Installer;

/// <summary>
/// Forge（Neo）通用安装器
/// </summary>
public sealed class ForgeInstaller : InstallerBase
{
    public string CustomId { get; init; }
    public string JavaPath { get; init; }
    public ForgeInstallEntry Entry { get; init; }
    public override string MinecraftFolder { get; init; }
    public MinecraftEntry InheritedMinecraft { get; init; }

    public static ForgeInstaller Create(string folder, string javaPath, ForgeInstallEntry installEntry, string customId = default)
    {
        return new ForgeInstaller
        {
            CustomId = customId,
            JavaPath = javaPath,
            Entry = installEntry,
            MinecraftFolder = folder
        };
    }

    public override async Task<MinecraftEntry> InstallAsync(CancellationToken cancellationToken = default)
    {
        FileInfo forgePackageFile = default;
        MinecraftEntry inheritedEntry = default;
        ModifiedMinecraftEntry entry = default;

        ReportProgress(InstallStep.Started, 0.0d, TaskStatus.WaitingToRun, 1, 1);

        try
        {
            inheritedEntry = ParseMinecraft(cancellationToken);
            forgePackageFile = await DownloadForgePackageAsync(cancellationToken);

            var (package, installProfile, isLegacy) = ParseForgePackage(forgePackageFile.FullName, cancellationToken);
            var forgeClientFile = await WriteVersionJsonAndSomeDependenciesAsync(isLegacy, installProfile, package, cancellationToken);

            entry = ParseModifiedMinecraft(forgeClientFile, cancellationToken);
            await CompleteForgeDependenciesAsync(isLegacy, installProfile, entry, cancellationToken);

            if (!isLegacy)
            {
                await RunInstallProcessorAsync(forgePackageFile.FullName, installProfile, entry, cancellationToken);
            }

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

    public static async Task<IEnumerable<ForgeInstallEntry>> EnumerableForgeAsync(string mcVersion, bool isNeoforge = false, CancellationToken cancellationToken = default)
    {
        var packagesUrl = isNeoforge
            ? $"https://bmclapi2.bangbang93.com/neoforge/list/{mcVersion}"
            : $"https://bmclapi2.bangbang93.com/forge/minecraft/{mcVersion}";

        string json = await packagesUrl.GetStringAsync(cancellationToken: cancellationToken);
        var entries = json.Deserialize(ForgeInstallEntryContext.Default.IEnumerableForgeInstallEntry)
            .OrderByDescending(entry => entry.Build);

        foreach (var entry in entries)
            entry.IsNeoforge = isNeoforge;

        return entries;
    }

    #region Privates

    private MinecraftEntry ParseMinecraft(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ReportProgress(InstallStep.ParseMinecraft, 0.15d, TaskStatus.Running, 1, 0);

        if (InheritedMinecraft is not null)
        {
            return InheritedMinecraft;
        }

        var inheritedMinecraft = new MinecraftParser(MinecraftFolder).GetMinecrafts()
            .FirstOrDefault(x => x.Version.VersionId == Entry.McVersion);

        ReportProgress(InstallStep.ParseMinecraft, 0.30d, TaskStatus.Running, 1, 1);
        return inheritedMinecraft ?? throw new InvalidOperationException("The corresponding version's parent was not found."); ;
    }

    private async Task<FileInfo> DownloadForgePackageAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ReportProgress(InstallStep.DownloadPackage, 0.30d, TaskStatus.Running, 1, 0);

        string packageUrl;
        if (Entry.IsNeoforge)
        {
            string prefix = Entry.McVersion is "1.20.1" ? "forge" : "neoforge";
            packageUrl = $"https://maven.neoforged.net/releases/net/neoforged/{prefix}/"
                + Entry.ForgeVersion + $"/{prefix}-{Entry.ForgeVersion}-installer.jar";
        }
        else
        {
            List<string> identifiers = [Entry.McVersion, Entry.ForgeVersion];
            string loaderVersion = string.Join('-', identifiers);

            packageUrl = $"https://maven.minecraftforge.net/releases/net/minecraftforge/forge/"
                + loaderVersion + $"/forge-{loaderVersion}-installer.jar";
        }

        string fileName = Entry.IsNeoforge
            ? $"neoforge-{Entry.ForgeVersion}-installer.jar"
            : $"forge-{Entry.McVersion}-{Entry.ForgeVersion}" +
                $"{(string.IsNullOrEmpty(Entry.Branch) ? string.Empty : $"-{Entry.Branch}")}" +
                $"-installer.jar";

        var packageFile = new FileInfo(Path.Combine(MinecraftFolder, fileName));
        var downloadRequest = new DownloadRequest(packageUrl, packageFile.FullName);

        await new DefaultDownloader()
            .DownloadAsync(downloadRequest, cancellationToken);

        ReportProgress(InstallStep.DownloadPackage, 0.45d, TaskStatus.Running, 1, 1);

        return packageFile;
    }

    private (ZipArchive package, JsonNode installProfile, bool isLegacy) ParseForgePackage(string packageFilePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ReportProgress(InstallStep.ParsePackage, 0.45d, TaskStatus.Running, 1, 0);

        var packageArchive = ZipFile.OpenRead(packageFilePath);
        var installProfileNode = packageArchive
            .GetEntry("install_profile.json")
            ?.ReadAsString()
            .AsNode()
            ?? throw new Exception("Failed to parse install_profile.json");

        bool isLegacyForgeVersion = installProfileNode.Select("install") != null;

        ReportProgress(InstallStep.ParsePackage, 0.50d, TaskStatus.Running, 1, 1);
        return (packageArchive, installProfileNode, isLegacyForgeVersion);
    }

    private async Task<FileInfo> WriteVersionJsonAndSomeDependenciesAsync(bool isLegacyForgeVersion, JsonNode installProfile, ZipArchive packageArchive, CancellationToken cancellationToken)
    {
        string forgeVersion = $"{Entry.McVersion}-{Entry.ForgeVersion}";
        string forgeLibsFolder = Path.Combine(MinecraftFolder, "libraries\\net\\minecraftforge\\forge", forgeVersion);

        ReportProgress(InstallStep.WriteVersionJsonAndSomeDependencies, 0.50d, TaskStatus.Running, 1, 0);

        if (isLegacyForgeVersion)
        {
            var universalFilePath = installProfile.Select("install").GetString("filePath")
                ?? throw new InvalidDataException("Unable to resolve location of universal file in archive");

            var universalFileEntry = packageArchive.GetEntry(universalFilePath)
                ?? throw new FileNotFoundException("The universal file was not found in the archive");

            universalFileEntry.ExtractTo(Path.Combine(forgeLibsFolder, universalFileEntry.Name.Replace("-universal", string.Empty)));
        }

        if (packageArchive.GetEntry($"maven/net/minecraftforge/forge/{forgeVersion}/") != null)
            foreach (var entry in packageArchive.Entries.Where(x => !x.FullName.EndsWith('/') && x.FullName.StartsWith($"maven/net/minecraftforge/forge/{forgeVersion}")))
                entry.ExtractTo(Path.Combine(forgeLibsFolder, entry.Name));

        packageArchive.GetEntry("data/client.lzma")?.ExtractTo(Path.Combine(forgeLibsFolder, $"forge-{forgeVersion}-clientdata.lzma"));

        string jsonContent = (isLegacyForgeVersion
            ? installProfile.Select("versionInfo")!.ToString()
            : packageArchive.GetEntry("version.json")?.ReadAsString())
            ?? throw new Exception("Failed to read version.json");
        var jsonNode = JsonNode.Parse(jsonContent);

        string entryId = CustomId ?? $"{Entry.McVersion}-{(Entry.IsNeoforge ? "neoforge" : "forge")}-{Entry.ForgeVersion}";
        var jsonFile = new FileInfo(Path.Combine(MinecraftFolder, "versions", entryId, $"{entryId}.json"));

        if (!jsonFile.Directory!.Exists)
            jsonFile.Directory.Create();

        jsonNode!["id"] = entryId;
        await File.WriteAllTextAsync(jsonFile.FullName, jsonNode.ToJsonString(), cancellationToken);

        ReportProgress(InstallStep.WriteVersionJsonAndSomeDependencies, 0.60d, TaskStatus.Running, 1, 1);
        return jsonFile;
    }

    private async Task CompleteForgeDependenciesAsync(bool isLegacyForgeVersion, JsonNode installProfile, MinecraftEntry minecraft, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ReportProgress(InstallStep.DownloadLibraries, 0.50d, TaskStatus.Running, 1, 0);

        string forgeVersion = $"{Entry.McVersion}-{Entry.ForgeVersion}";
        var dependencies = new List<MinecraftLibrary>();

        var libraries = minecraft.GetRequiredLibraries().Libraries.ToList();
        foreach (var lib in libraries.Where(x => x.MavenName.Equals($"net.minecraftforge:forge:{forgeVersion}")
            || x.MavenName.Equals($"net.minecraftforge:forge:{forgeVersion}:client") || x is not IDownloadDependency).ToArray())
            libraries.Remove(lib);

        dependencies.AddRange(libraries);

        if (!isLegacyForgeVersion)
        {
            var processorLibraries = installProfile.Select("libraries")
                .Deserialize(LibraryEntryContext.Default.IEnumerableLibraryEntry)?
                .Select(lib => MinecraftLibrary.ParseJsonNode(lib, MinecraftFolder))
                ?? throw new InvalidDataException();

            foreach (var item in processorLibraries)
                if (!dependencies.Contains(item))
                    dependencies.Add(item);
        }

        var groupDownloadRequest = new GroupDownloadRequest(dependencies.OfType<IDownloadDependency>()
            .Select(x => new DownloadRequest(DownloadManager.BmclApi.TryFindUrl(x.Url), x.FullPath)));

        groupDownloadRequest.ProgressChanged = args
            => ReportProgress(InstallStep.DownloadLibraries, args.Percentage.ToPercentage(0.50d, 0.70d),
                    TaskStatus.Running, args.TotalCount, args.CompletedCount, args.Speed, true);

        var groupDownloadResult = await new DefaultDownloader()
            .DownloadManyAsync(groupDownloadRequest, cancellationToken);

        //if (groupDownloadResult.Failed.Count() > 0)
        //    throw new InvalidOperationException("Some dependent files encountered errors during download");
    }

    private async Task RunInstallProcessorAsync(string packageFilePath, JsonNode installProfile, MinecraftEntry entry, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Dictionary<string, Dictionary<string, string>> forgeDataDictionary = installProfile.Select("data")
            .Deserialize(ForgeInstallerContext.Default.DictionaryStringDictionaryStringString)
            ?? throw new Exception("Failed to parse install profile data");

        string forgeVersion = $"{Entry.McVersion}-{Entry.ForgeVersion}";

        if (forgeDataDictionary.TryGetValue("BINPATCH", out Dictionary<string, string> value))
        {
            value["client"] = $"[net.minecraftforge:forge:{forgeVersion}:clientdata@lzma]";
            value["server"] = $"[net.minecraftforge:forge:{forgeVersion}:serverdata@lzma]";
        }

        var replaceValues = new Dictionary<string, string> {
            { "{SIDE}", "client" },
            { "{MINECRAFT_JAR}", entry.ClientJarPath },
            { "{MINECRAFT_VERSION}", Entry.McVersion },
            { "{ROOT}", MinecraftFolder.ToPath() },
            { "{INSTALLER}", packageFilePath.ToPath() },
            { "{LIBRARY_DIR}", Path.Combine(MinecraftFolder, "libraries").ToPath() }
        };

        var replaceProcessorArgs = forgeDataDictionary.ToDictionary(
            kvp => $"{{{kvp.Key}}}", kvp =>
            {
                var value = kvp.Value["client"];
                if (!value.StartsWith('[')) return value;

                return Path.Combine(MinecraftFolder, "libraries", value.TrimStart('[').TrimEnd(']')
                    .FormatLibraryNameToRelativePath())
                    .ToPath();
            });

        var forgeProcessors = installProfile.Select("processors")?
            .Deserialize(ForgeInstallerContext.Default.IEnumerableForgeProcessorData)?
            .Where(x => !(x.Sides.Count == 1 && x.Sides.Contains("server")))
            .ToArray()
            ?? throw new InvalidDataException("Unable to parse Forge Processors");

        int count = 0;
        int totalCount = forgeProcessors.Length;

        ReportProgress(InstallStep.RunInstallProcessor, 0.70d, TaskStatus.Running, totalCount, count);
        foreach (var processor in forgeProcessors)
        {
            cancellationToken.ThrowIfCancellationRequested();

            processor.Args = processor.Args.Select(x =>
            {
                if (x.StartsWith('['))
                    return Path.Combine(MinecraftFolder, "libraries", x.TrimStart('[').TrimEnd(']').FormatLibraryNameToRelativePath())
                        .ToPath();

                return x.ReplaceFromDictionary(replaceProcessorArgs)
                    .ReplaceFromDictionary(replaceValues);
            });

            processor.Outputs = processor.Outputs.ToDictionary(
                kvp => kvp.Key.ReplaceFromDictionary(replaceProcessorArgs),
                kvp => kvp.Value.ReplaceFromDictionary(replaceProcessorArgs));

            var fileName = Path.Combine(MinecraftFolder, "libraries", processor.Jar.FormatLibraryNameToRelativePath());

            using var fileArchive = ZipFile.OpenRead(fileName);
            string mainClass = fileArchive.GetEntry("META-INF/MANIFEST.MF")?
                .ReadAsString()
                .Split("\r\n".ToCharArray())
                .FirstOrDefault(x => x.Contains("Main-Class: "))
                ?.Replace("Main-Class: ", string.Empty)
                ?? throw new InvalidDataException("Unable to find MainClass for Processor");

            string classPath = string.Join(Path.PathSeparator.ToString(), new List<string>() { fileName }
                .Concat(processor.Classpath.Select(x => Path.Combine(MinecraftFolder, "libraries", x.FormatLibraryNameToRelativePath()))));

            var args = new List<string> {
                "-cp",
                classPath.ToPath(),
                mainClass
            };

            args.AddRange(processor.Args);

            using var process = Process.Start(new ProcessStartInfo(JavaPath)
            {
                Arguments = string.Join(" ", args),
                UseShellExecute = false,
                WorkingDirectory = MinecraftFolder,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            }) ?? throw new Exception("Failed to start Java");

            List<string> _errorOutputs = [];

            process.ErrorDataReceived += (_, args) =>
            {
                if (args.Data is string data && !string.IsNullOrEmpty(data))
                    _errorOutputs.Add(args.Data);
            };

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync(cancellationToken);

            ReportProgress(InstallStep.RunInstallProcessor, ((double)count / (double)totalCount).ToPercentage(0.75d, 0.95d),
                 TaskStatus.Running, totalCount, Interlocked.Increment(ref count));
        }
    }

    private static ModifiedMinecraftEntry ParseModifiedMinecraft(FileInfo file, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var entry = MinecraftParser.Parse(file.Directory, null, out var _) as ModifiedMinecraftEntry;

        return entry ?? throw new InvalidOperationException("An incorrect modified entry was encountered");
    }

    #endregion Privates
}

public record ForgeProcessorData
{
    [JsonPropertyName("jar")] public string Jar { get; set; } = null!;
    [JsonPropertyName("sides")] public List<string> Sides { get; set; } = [];
    [JsonPropertyName("args")] public IEnumerable<string> Args { get; set; } = null!;
    [JsonPropertyName("classpath")] public IEnumerable<string> Classpath { get; set; } = null!;
    [JsonPropertyName("outputs")] public Dictionary<string, string> Outputs { get; set; } = [];
}

[JsonSerializable(typeof(IEnumerable<ForgeProcessorData>))]
[JsonSerializable(typeof(Dictionary<string, Dictionary<string, string>>))]
internal sealed partial class ForgeInstallerContext : JsonSerializerContext;