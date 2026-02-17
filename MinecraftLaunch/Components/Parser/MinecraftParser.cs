using MinecraftLaunch.Base.Enums;
using MinecraftLaunch.Base.Interfaces;
using MinecraftLaunch.Base.Models.Game;
using System.Collections.Frozen;
using System.Diagnostics;
using System.Text.Json;

namespace MinecraftLaunch.Components.Parser;

using PartialData = (
    string VersionFolderName,
    string MinecraftFolderPath,
    string ClientJsonPath);

public sealed class MinecraftParser {
    private static readonly FrozenDictionary<string, (ModLoaderType, Func<string, string>)> _modLoaderLibs = new Dictionary<string, (ModLoaderType, Func<string, string>)>() {
        { "net.minecraftforge:forge:", (ModLoaderType.Forge, libVersion => libVersion.Split('-')[1]) },
        { "net.minecraftforge:fmlloader:", (ModLoaderType.Forge, libVersion => libVersion.Split('-')[1]) },
        { "net.neoforged.fancymodloader:loader:", (ModLoaderType.NeoForge, libVersion => libVersion) },
        { "optifine:optifine", (ModLoaderType.OptiFine, libVersion => libVersion[(libVersion.IndexOf('_') + 1)..].ToUpper()) },
        { "net.fabricmc:fabric-loader:", (ModLoaderType.Fabric, libVersion => libVersion) },
        { "com.mumfrey:liteloader:", (ModLoaderType.LiteLoader, libVersion => libVersion) },
        { "org.quiltmc:quilt-loader:", (ModLoaderType.Quilt, libVersion => libVersion) },
    }.ToFrozenDictionary();

    public DirectoryInfo Root { set; get; }
    public static Dictionary<string, IDataProcessor> DataProcessors { get; } = [];

    public MinecraftParser(string root) {
        Root = new(root);
    }

    public static implicit operator MinecraftParser(string minecraftRootPath) {
        return new(minecraftRootPath);
    }

    public static implicit operator string(MinecraftParser resolver) {
        return resolver.Root.FullName;
    }

    public MinecraftEntry GetMinecraft(string id) {
        var versionDirectory = new DirectoryInfo(Path.Combine(Root.FullName, "versions", id));
        return Parse(versionDirectory, null, out var _);
    }

    public List<MinecraftEntry> GetMinecrafts() {
        var list = new List<MinecraftEntry>();
        var versionsDirectory = new DirectoryInfo(Path.Combine(Root.FullName, "versions"));

        if (!versionsDirectory.Exists)
            return [];

        foreach (DirectoryInfo dir in versionsDirectory.EnumerateDirectories())
        {
            var entry = Parse(dir, list, out bool inheritedInstanceAlreadyFound);
            int index = list.FindIndex(i => i.Id == entry.Id);
            if (index != -1)
            {
                list.RemoveAt(index);
            }

            list.Add(entry);
            if (entry is ModifiedMinecraftEntry m && m.HasInheritance && !inheritedInstanceAlreadyFound)
                list.Add(m.InheritedMinecraft);
        }

        foreach (var processor in DataProcessors.Values) {
            processor.Handle(list);
            _ = processor.SaveAsync();
        }

        return list;
    }

    internal static MinecraftEntry Parse(DirectoryInfo clientDir, IEnumerable<MinecraftEntry> parsedInstances, out bool foundInheritedInstanceInParsed) {
        foundInheritedInstanceInParsed = false;

        if (!clientDir.Exists)
            throw new DirectoryNotFoundException($"{clientDir.FullName} not found");

        // Find client.json
        var clientJsonFile = clientDir
            .GetFiles($"{clientDir.Name}.json")
            .FirstOrDefault()
            ?? throw new FileNotFoundException($"client.json not found in {clientDir.FullName}");
        string clientJsonPath = clientJsonFile.FullName;

        // Parse client.json
        using var stream = clientJsonFile.OpenRead();
        using var doc = JsonDocument.Parse(stream);
        var clientJsonObject = doc.Deserialize(MinecraftJsonEntryContext.Default.MinecraftJsonEntry)
            ?? throw new JsonException($"Failed to deserialize {clientJsonPath} into {typeof(MinecraftJsonEntry)}");

        // <version> folder name
        string versionFolderName = clientDir.Name;

        // .minecraft folder path
        string minecraftFolderPath = clientDir.Parent?.Parent?.FullName
            ?? throw new DirectoryNotFoundException($"Failed to find .minecraft folder for {clientDir.FullName}");

        PartialData partialData = (versionFolderName, minecraftFolderPath, clientJsonPath);

        // Create MinecraftInstance
        return IsVanilla(clientJsonObject)
            ? ParseVanilla(partialData, clientJsonObject, doc.RootElement)
            : ParseModified(partialData, clientJsonObject, doc.RootElement, parsedInstances, out foundInheritedInstanceInParsed);
    }

    private static bool IsVanilla(MinecraftJsonEntry clientJsonObject) {
        if (clientJsonObject.MainClass is null)
            throw new JsonException("MainClass is not defined in client.json");

        bool hasVanillaMainClass = clientJsonObject.MainClass is
            "net.minecraft.client.main.Main"
            or "net.minecraft.launchwrapper.Launch"
            or "com.mojang.rubydung.RubyDung";

        bool hasTweakClass =
            // Before 1.13
            clientJsonObject.MinecraftArguments?.Contains("--tweakClass") == true
            && clientJsonObject.MinecraftArguments?.Contains("net.minecraft.launchwrapper.AlphaVanillaTweaker") == false
            // Since 1.13
            || CheckIfOver113(clientJsonObject.Arguments);
        
        if (!string.IsNullOrEmpty(clientJsonObject.InheritsFrom)
            || !hasVanillaMainClass
            || hasVanillaMainClass && hasTweakClass)
            return false;

        return true;

        static bool CheckIfOver113(JsonElement argumentElement)
        {
            if (!argumentElement.TryGetProperty("game"u8, out var gameElement)) return false;
            foreach (var item in gameElement.EnumerateArray())
            {
                if (item.ValueKind is JsonValueKind.String &&
                    string.Equals(item.GetString()!, "--tweakClass", StringComparison.Ordinal)) return true;
            }
            return false;
        }
    }

    private static string ReadVersionIdFromNonInheritingClientJson(MinecraftJsonEntry gameJsonEntry,
        JsonElement clientJsonNode)
    {
        Debug.Assert(clientJsonNode.ValueKind == JsonValueKind.Object);
        var versionId = gameJsonEntry.Id;
        if (clientJsonNode.TryGetProperty("patches"u8, out var hmclPatchesNode))
        {
            if(hmclPatchesNode.GetArrayLength() < 0)throw new FormatException("Failed to parse version id");
            versionId = hmclPatchesNode[0].GetProperty("version"u8).GetString();
        }
        else if (clientJsonNode.TryGetProperty("clientVersion"u8, out var pclClientVersionNode)) 
            versionId = pclClientVersionNode.GetString();

        return versionId ?? throw new FormatException("Failed to parse version id");
    }

    private static VanillaMinecraftEntry ParseVanilla(PartialData partialData, MinecraftJsonEntry gameJsonEntry, JsonElement clientJsonNode) {
        // Check if client.jar exists
        string clientJarPath = partialData.ClientJsonPath[..^"json".Length] + "jar";

        // Parse version
        string versionId = ReadVersionIdFromNonInheritingClientJson(gameJsonEntry, clientJsonNode);
        var version = MinecraftVersion.Parse(versionId);

        // Asset index path
        string assetIndexId = gameJsonEntry.AssetIndex?.Id
            ?? throw new InvalidDataException("Asset index ID does not exist in client.json");
        string assetIndexJsonPath = Path.Combine(partialData.MinecraftFolderPath, "assets", "indexes", $"{assetIndexId}.json");

        return new VanillaMinecraftEntry {
            Version = version,
            ClientJarPath = clientJarPath,
            Id = partialData.VersionFolderName,
            AssetIndexJsonPath = assetIndexJsonPath,
            ReleaseTime = gameJsonEntry.ReleaseTime,
            ClientJsonPath = partialData.ClientJsonPath,
            MinecraftFolderPath = partialData.MinecraftFolderPath
        };
    }

    private static ModifiedMinecraftEntry ParseModified(PartialData partialData, MinecraftJsonEntry minecraftJsonEntry, JsonElement clientJsonNode,
        IEnumerable<MinecraftEntry> minecraftEntries,
        out bool foundInheritedInstanceInParsed) {
        foundInheritedInstanceInParsed = false;

        bool hasInheritance = !string.IsNullOrEmpty(minecraftJsonEntry.InheritsFrom);
        VanillaMinecraftEntry inheritedEntry = null!;
        if (hasInheritance) {
            // Find the inherited instance
            string inheritedInstanceId = minecraftJsonEntry.InheritsFrom
                ?? throw new InvalidOperationException("InheritsFrom is not defined in client.json");
            inheritedEntry = minecraftEntries?.FirstOrDefault(i => i is VanillaMinecraftEntry v && v.Version.VersionId == inheritedInstanceId) as  VanillaMinecraftEntry;

            if (inheritedEntry is not null) {
                foundInheritedInstanceInParsed = true;
            } else {
                string inheritedInstancePath = Path.Combine(partialData.MinecraftFolderPath, "versions", inheritedInstanceId);
                var inheritedInstanceDir = new DirectoryInfo(inheritedInstancePath);

                inheritedEntry = Parse(inheritedInstanceDir, null, out  _) as VanillaMinecraftEntry
                    ?? throw new InvalidOperationException($"Failed to parse inherited instance {inheritedInstanceId}");
            }
        }

        string assetIndexJsonPath = hasInheritance
            ? inheritedEntry.AssetIndexJsonPath
            : minecraftJsonEntry.AssetIndex?.Id == null
                ? throw new InvalidDataException("Asset index ID does not exist in client.json")
                : Path.Combine(partialData.MinecraftFolderPath, "assets", "indexes", $"{minecraftJsonEntry.AssetIndex.Id}.json");

        // Check if client.jar exists
        string clientJarPath = hasInheritance
            ? inheritedEntry.ClientJarPath
            : partialData.ClientJsonPath[..^"json".Length] + "jar";

        // Parse version
        MinecraftVersion? version;
        if (hasInheritance) {
            // Use version from the inherited instance
            version = inheritedEntry.Version;

        } else {
            // Read from client.json
            string versionId = ReadVersionIdFromNonInheritingClientJson(minecraftJsonEntry, clientJsonNode);
            version = MinecraftVersion.Parse(versionId);
        }

        // Parse mod loaders
        List<ModLoaderInfo> modLoaders = [];
        var librariesElement = minecraftJsonEntry.Libraries;
        foreach (var lib in librariesElement.EnumerateArray()) {
            if(!lib.TryGetProperty("name"u8, out var libNameElement))continue;
            var libName = libNameElement.GetString();
            if (libName is null)
                continue;

            foreach (var key in _modLoaderLibs.Keys) {
                if (!libName.Contains(key,StringComparison.OrdinalIgnoreCase))
                    continue;
                
                // Mod loader library detected
                var id = libName.Split(':')[2];
                var loader = new ModLoaderInfo {
                    Type = _modLoaderLibs[key].Item1,
                    Version = _modLoaderLibs[key].Item2(id)
                };
                
                if (!modLoaders.Contains(loader))
                    modLoaders.Add(loader);

                break;
            }
        }

        var releaseTime = hasInheritance
            ? inheritedEntry.ReleaseTime
            : minecraftJsonEntry.ReleaseTime;

        return new ModifiedMinecraftEntry {
            ReleaseTime = releaseTime,
            Id = partialData.VersionFolderName,
            Version = (MinecraftVersion)version,
            AssetIndexJsonPath = assetIndexJsonPath,
            MinecraftFolderPath = partialData.MinecraftFolderPath,
            ClientJsonPath = partialData.ClientJsonPath,
            ClientJarPath = clientJarPath,
            InheritedMinecraft = inheritedEntry,
            ModLoaders = modLoaders
        };
    }
}