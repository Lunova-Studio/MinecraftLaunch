using MinecraftLaunch.Base.Enums;
using MinecraftLaunch.Base.Models.Game;
using MinecraftLaunch.Base.Utilities;
using System.IO.Compression;
using System.Text.Json;
using MinecraftLaunch.Base.Models.SHA1;

namespace MinecraftLaunch.Extensions;

public static class MinecraftEntryExtension {
    public static JavaEntry GetAppropriateJava(this MinecraftEntry minecraft, IEnumerable<JavaEntry> javas) {
        var targetJavaVersion = minecraft.GetAppropriateJavaVersion();

        var isForgeOrNeoForge = false;

        if (minecraft is ModifiedMinecraftEntry modifiedMinecraft)
        {
            isForgeOrNeoForge = modifiedMinecraft.ModLoaders.Any(static x => x.Type is ModLoaderType.Forge or ModLoaderType.NeoForge);
        }

        if (targetJavaVersion is 0 or -1) return javas.Last();
        if (isForgeOrNeoForge) return javas.Last(x => x.MajorVersion == targetJavaVersion);
        else return javas.Last(x => x.MajorVersion >= targetJavaVersion);
    }

    public static int GetAppropriateJavaVersion(this MinecraftEntry minecraft) {
        if (minecraft is ModifiedMinecraftEntry { HasInheritance: true } mc)
            return mc.InheritedMinecraft.GetAppropriateJavaVersion();

        using var stream = File.OpenRead(minecraft.ClientJsonPath);
        using var doc =  JsonDocument.Parse(stream);
        if (doc.RootElement.TryGetProperty("javaVersion"u8, out var javaVersionElement) &&
            javaVersionElement.TryGetProperty("majorVersion"u8, out var majorVersionElement))
        {
            return majorVersionElement.GetInt32();
        }
        return 8;
        
    }

    public static MinecraftClient GetJarElement(this MinecraftEntry entry) {
        string clientJsonPath = entry.ClientJsonPath;
        if (entry is ModifiedMinecraftEntry { HasInheritance: true } inst)
            clientJsonPath = inst.InheritedMinecraft.ClientJsonPath;
        using var stream = File.OpenRead(clientJsonPath);
        using var doc = JsonDocument.Parse(stream);
        if (!doc.RootElement.TryGetProperty("downloads"u8, out var downloadsElement) ||
            !downloadsElement.TryGetProperty("client", out var clientArtifactNode)) return null;
        

        string clientJarPath = entry.ClientJarPath;
        if (entry is ModifiedMinecraftEntry { HasInheritance: true } inst_)
            clientJarPath = inst_.ClientJarPath;

        if (clientJarPath is null)
            return null;

        long size = clientArtifactNode.GetProperty("size"u8).GetInt64();
        string url = clientArtifactNode.GetProperty("url"u8).GetString();
        var sha1 = clientArtifactNode.GetPropertyNullable("sha1"u8)?.Deserialize(Sha1Data.Sha1DataSerializerContext.Default.Sha1Data);

        if (sha1 is null || url is null)
            throw new InvalidDataException("Invalid client info");

        return new MinecraftClient {
            MinecraftFolderPath = entry.MinecraftFolderPath,
            ClientId = Path.GetFileNameWithoutExtension(clientJarPath),
            Url = url,
            Sha1 = sha1.Value,
            Size = size
        };
    }

    public static AssstIndex GetAssetIndex(this MinecraftEntry minecraftEntry) {
        // Identify file paths
        string clientJsonPath = minecraftEntry is ModifiedMinecraftEntry { HasInheritance: true } entry
            ? entry.InheritedMinecraft.ClientJsonPath
            : minecraftEntry.ClientJsonPath;
        
        // Parse client.json
        using var stream = File.OpenRead(clientJsonPath);
        using var doc = JsonDocument.Parse(stream);
        var root = doc.RootElement;
        if(!root.TryGetProperty("assetIndex"u8, out var assetIndex))throw new InvalidDataException("Error in parsing version.json");
        

        long size = assetIndex.GetProperty("size"u8).GetInt64();
        string id = assetIndex.GetProperty("id"u8).GetString() ?? throw new InvalidDataException();
        string url = assetIndex.GetProperty("url"u8).GetString() ?? throw new InvalidDataException();
        var sha1 = assetIndex.GetPropertyNullable("sha1"u8)?.Deserialize(Sha1Data.Sha1DataSerializerContext.Default.Sha1Data) ?? throw new InvalidDataException();

        return new AssstIndex {
            Id = id,
            Url = url,
            Size = size,
            Sha1 = sha1,
            MinecraftFolderPath = minecraftEntry.MinecraftFolderPath,
        };
    }

    public static void ExtractNatives(this MinecraftEntry minecraftEntry, IReadOnlyList<MinecraftLibrary> natives) {
        if (!natives.Any()) return;

        var extension = EnvironmentUtil.GetPlatformName() switch {
            "windows" => ".dll",
            "linux" => ".so",
            "osx" => ".dylib",
            _ => "."
        };

        foreach (var file in natives) {
            using ZipArchive zip = ZipFile.OpenRead(file.FullPath);

            foreach (ZipArchiveEntry entry in zip.Entries) {
                if (Path.HasExtension(entry.FullName)) {
                    var toExtract = new FileInfo(Path.Combine(minecraftEntry.MinecraftFolderPath, "versions", minecraftEntry.Id, "natives", entry.Name));
                    toExtract.Directory?.Create();
                    if (!toExtract.Exists) {
                        entry.ExtractToFile(toExtract.FullName, true);
                    }
                }
            }
        }
    }

    public static Task ExtractNativesAsync(this MinecraftEntry minecraftEntry, IReadOnlyList<MinecraftLibrary> natives, CancellationToken cancellationToken = default) => Task.Run(() => {
        if (!natives.Any()) return;

        var extension = EnvironmentUtil.GetPlatformName() switch {
            "windows" => ".dll",
            "linux" => ".so",
            "osx" => ".dylib",
            _ => "."
        };

        foreach (var file in natives) {
            using ZipArchive zip = ZipFile.OpenRead(file.FullPath);

            foreach (ZipArchiveEntry entry in zip.Entries) {
                if (Path.HasExtension(entry.FullName)) {
                    var toExtract = new FileInfo(Path.Combine(minecraftEntry.MinecraftFolderPath, "versions", minecraftEntry.Id, "natives", entry.Name));
                    toExtract.Directory?.Create();
                    if (!toExtract.Exists) {
                        entry.ExtractToFile(toExtract.FullName, true);
                    }
                }
            }
        }
    }, cancellationToken);
}