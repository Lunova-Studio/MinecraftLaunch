using MinecraftLaunch.Base.Enums;
using MinecraftLaunch.Base.Models.Game;
using MinecraftLaunch.Base.Utilities;
using System.IO.Compression;
using System.Text.Json.Nodes;

namespace MinecraftLaunch.Extensions;

public static class MinecraftEntryExtension
{
    public static JavaEntry GetAppropriateJava(this MinecraftEntry minecraft, IEnumerable<JavaEntry> javas)
    {
        var targetJavaVersion = minecraft.GetAppropriateJavaVersion();

        bool isForgeOrNeoForge = false;
        List<JavaEntry> possiblyAvailableJavas = [];

        if (minecraft is ModifiedMinecraftEntry modifiedMinecraft)
        {
            var loaders = modifiedMinecraft.ModLoaders.Select(x => x.Type);
            isForgeOrNeoForge = loaders.Contains(ModLoaderType.Forge) || loaders.Contains(ModLoaderType.NeoForge);
        }

        possiblyAvailableJavas = targetJavaVersion is 0 or -1
            ? [.. javas]
            : isForgeOrNeoForge
                ? [.. javas.Where(x => x.MajorVersion.Equals(targetJavaVersion))]
                : [.. javas.Where(x => x.MajorVersion >= targetJavaVersion)];

        if (possiblyAvailableJavas.Count == 0)
            throw new FileNotFoundException($"No suitable version of Java found to start this game, version {targetJavaVersion} is required");

        possiblyAvailableJavas.Reverse();
        return possiblyAvailableJavas.First();
    }

    public static int GetAppropriateJavaVersion(this MinecraftEntry minecraft)
    {
        if (minecraft is ModifiedMinecraftEntry { HasInheritance: true } mc)
            return mc.InheritedMinecraft.GetAppropriateJavaVersion();

        var majorJavaVersionNode = File.ReadAllText(minecraft.ClientJsonPath).AsNode()
            .Select("javaVersion")?
            .Select("majorVersion");

        return majorJavaVersionNode is null
            ? 8
            : majorJavaVersionNode.GetInt32();
    }

    public static MinecraftClient GetJarElement(this MinecraftEntry entry)
    {
        string clientJsonPath = entry.ClientJsonPath;
        if (entry is ModifiedMinecraftEntry { HasInheritance: true } inst)
            clientJsonPath = inst.InheritedMinecraft.ClientJsonPath;

        JsonNode clientArtifactNode = File.ReadAllText(clientJsonPath).AsNode().Select("downloads")?.Select("client");

        if (clientArtifactNode is null)
            return null;

        string clientJarPath = entry.ClientJarPath;
        if (entry is ModifiedMinecraftEntry { HasInheritance: true } inst_)
            clientJarPath = inst_.ClientJarPath;

        if (clientJarPath is null)
            return null;

        long? size = clientArtifactNode.GetInt64("size");
        string url = clientArtifactNode.GetString("url");
        string sha1 = clientArtifactNode.GetString("sha1");

        if (sha1 is null || url is null || size is null)
            throw new InvalidDataException("Invalid client info");

        return new MinecraftClient
        {
            MinecraftFolderPath = entry.MinecraftFolderPath,
            ClientId = Path.GetFileNameWithoutExtension(clientJarPath),
            Url = url,
            Sha1 = sha1,
            Size = size.Value
        };
    }

    public static AssstIndex GetAssetIndex(this MinecraftEntry minecraftEntry)
    {
        // Identify file paths
        string clientJsonPath = minecraftEntry is ModifiedMinecraftEntry { HasInheritance: true } entry
            ? entry.InheritedMinecraft.ClientJsonPath
            : minecraftEntry.ClientJsonPath;

        // Parse client.json
        JsonNode jsonNode = JsonNode.Parse(File.ReadAllText(clientJsonPath));
        var assetIndex = jsonNode.Select("assetIndex")
            ?? throw new InvalidDataException("Error in parsing version.json");

        long size = assetIndex.GetInt64("size").Value;
        string id = assetIndex.GetString("id") ?? throw new InvalidDataException();
        string url = assetIndex.GetString("url") ?? throw new InvalidDataException();
        string sha1 = assetIndex.GetString("sha1") ?? throw new InvalidDataException();

        return new AssstIndex
        {
            Id = id,
            Url = url,
            Size = size,
            Sha1 = sha1,
            MinecraftFolderPath = minecraftEntry.MinecraftFolderPath,
        };
    }

    public static void ExtractNatives(this MinecraftEntry minecraftEntry, IReadOnlyList<MinecraftLibrary> natives)
    {
        if (!natives.Any()) return;

        var extension = EnvironmentUtil.GetPlatformName() switch
        {
            "windows" => ".dll",
            "linux" => ".so",
            "osx" => ".dylib",
            _ => "."
        };

        foreach (var file in natives)
        {
            using ZipArchive zip = ZipFile.OpenRead(file.FullPath);

            foreach (ZipArchiveEntry entry in zip.Entries)
            {
                if (Path.HasExtension(entry.FullName))
                {
                    var toExtract = new FileInfo(Path.Combine(minecraftEntry.MinecraftFolderPath, "versions", minecraftEntry.Id, "natives", entry.Name));
                    toExtract.Directory?.Create();
                    if (!toExtract.Exists)
                    {
                        entry.ExtractToFile(toExtract.FullName, true);
                    }
                }
            }
        }
    }

    public static Task ExtractNativesAsync(this MinecraftEntry minecraftEntry, IReadOnlyList<MinecraftLibrary> natives, CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        if (!natives.Any()) return;

        var extension = EnvironmentUtil.GetPlatformName() switch
        {
            "windows" => ".dll",
            "linux" => ".so",
            "osx" => ".dylib",
            _ => "."
        };

        foreach (var file in natives)
        {
            using ZipArchive zip = ZipFile.OpenRead(file.FullPath);

            foreach (ZipArchiveEntry entry in zip.Entries)
            {
                if (Path.HasExtension(entry.FullName))
                {
                    var toExtract = new FileInfo(Path.Combine(minecraftEntry.MinecraftFolderPath, "versions", minecraftEntry.Id, "natives", entry.Name));
                    toExtract.Directory?.Create();
                    if (!toExtract.Exists)
                    {
                        entry.ExtractToFile(toExtract.FullName, true);
                    }
                }
            }
        }
    }, cancellationToken);
}