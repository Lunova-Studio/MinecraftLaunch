using MinecraftLaunch.Base.Interfaces;
using MinecraftLaunch.Base.Utilities;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace MinecraftLaunch.Base.Models.Game;

[JsonDerivedType(typeof(VanillaMinecraftEntry), typeDiscriminator: "vanilla")]
[JsonDerivedType(typeof(ModifiedMinecraftEntry), typeDiscriminator: "modified")]
public abstract class MinecraftEntry
{
    public string Id { get; init; }
    public MinecraftVersion Version { get; init; }

    public string ClientJarPath { get; init; }
    public DateTime ReleaseTime { get; init; }
    public string ClientJsonPath { get; init; }
    public string AssetIndexJsonPath { get; init; }
    public string MinecraftFolderPath { get; init; }

    public bool IsVanilla => this is VanillaMinecraftEntry;

    private static bool IsLibraryEnabled(IEnumerable<RuleEntry> rules)
    {
        bool windows, linux, osx;
        windows = linux = osx = false;

        foreach (var item in rules)
        {
            if (item.Action == "allow")
            {
                if (item.System == null)
                {
                    windows = linux = osx = true;
                    continue;
                }

                switch (item.System.Name)
                {
                    case "windows":
                        windows = true;
                        break;

                    case "linux":
                        linux = true;
                        break;

                    case "osx":
                        osx = true;
                        break;
                }
            }
            else if (item.Action == "disallow")
            {
                if (item.System == null)
                {
                    windows = linux = osx = false;
                    continue;
                }

                switch (item.System.Name)
                {
                    case "windows":
                        windows = false;
                        break;

                    case "linux":
                        linux = false;
                        break;

                    case "osx":
                        osx = false;
                        break;
                }
            }
        }

        // TODO: Check OS version and architecture?

        return EnvironmentUtil.GetPlatformName() switch
        {
            "windows" => windows,
            "linux" => linux,
            "osx" => osx,
            _ => false,
        };
    }

    public IEnumerable<MinecraftAsset> GetRequiredAssets()
    {
        // Identify file paths
        string assetIndexJsonPath = AssetIndexJsonPath;
        if (this is ModifiedMinecraftEntry { HasInheritance: true } instance)
            assetIndexJsonPath = instance.InheritedMinecraft.AssetIndexJsonPath;

        // Parse asset index json
        JsonNode jsonNode = JsonNode.Parse(File.ReadAllText(assetIndexJsonPath));
        Dictionary<string, AssetJsonEntry> assets = jsonNode?["objects"]?
            .Deserialize(AssetJsonEntryContext.Default.DictionaryStringAssetJsonEntry)
            ?? throw new InvalidDataException("Error in parsing asset index json file");

        // Parse GameAsset objects
        foreach (var (key, assetJsonNode) in assets)
        {
            int size = assetJsonNode.Size;
            string hash = assetJsonNode.Hash ?? throw new InvalidDataException("Invalid asset index");

            yield return new MinecraftAsset
            {
                MinecraftFolderPath = MinecraftFolderPath,
                Key = key,
                Sha1 = hash,
                Size = size
            };
        }
    }

    public (IEnumerable<MinecraftLibrary> Libraries, IEnumerable<MinecraftLibrary> NativeLibraries) GetRequiredLibraries()
    {
        List<MinecraftLibrary> libs = [];
        List<MinecraftLibrary> nativeLibs = [];

        var libNodes = JsonNode.Parse(File.ReadAllText(ClientJsonPath))?["libraries"]?
            .Deserialize(LibraryEntriesContext.Default.IEnumerableLibraryEntry)
                ?? throw new InvalidDataException("client.json does not contain library information");

        foreach (var libNode in libNodes)
        {
            if (libNode is null)
                continue;

            // Check if a library is enabled
            if (libNode.Rules is IEnumerable<RuleEntry> libRules)
            {
                if (!IsLibraryEnabled(libRules))
                    continue;
            }

            // Parse library
            var gameLib = MinecraftLibrary.ParseJsonNode(libNode, MinecraftFolderPath);

            if (gameLib.IsNativeLibrary)
                nativeLibs.Add(gameLib);
            else
            {
                libs.Add(gameLib);
            }
        }

        return (libs, nativeLibs);
    }

    public override bool Equals(object obj)
    {
        if (obj is MinecraftEntry other)
        {
            return string.Equals(Id, other.Id, StringComparison.OrdinalIgnoreCase)
                && string.Equals(MinecraftFolderPath, other.MinecraftFolderPath, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Id?.ToLowerInvariant(),
            MinecraftFolderPath?.ToLowerInvariant());
    }
}

[JsonSerializable(typeof(IEnumerable<LibraryEntry>))]
public sealed partial class LibraryEntriesContext : JsonSerializerContext;

public class VanillaMinecraftEntry : MinecraftEntry;

public class ModifiedMinecraftEntry : MinecraftEntry
{
    public IEnumerable<ModLoaderInfo> ModLoaders { get; init; }

    public VanillaMinecraftEntry InheritedMinecraft { get; init; }

    [MemberNotNullWhen(true, nameof(InheritedMinecraft))]
    public bool HasInheritance { get => InheritedMinecraft is not null; }
}

public abstract class MinecraftDependency
{
    /// <summary>
    /// Absolute path of the .minecraft folder
    /// </summary>
    public string MinecraftFolderPath { get; init; }

    /// <summary>
    /// File path relative to the .minecraft folder
    /// </summary>
    public abstract string FilePath { get; }

    /// <summary>
    /// Absolute path of the file
    /// </summary>
    public string FullPath => Path.Combine(MinecraftFolderPath, FilePath);
}

public abstract partial class MinecraftLibrary : MinecraftDependency
{
    private static readonly Regex MavenParseRegex = GenerateMavenParseRegex();

    public string MavenName { get; init; }
    public bool IsNativeLibrary { get; init; }
    public override string FilePath => Path.Combine("libraries", GetLibraryPath());

    public MinecraftLibrary(string mavenName)
    {
        this.MavenName = mavenName;
        Match match = MavenParseRegex.Match(mavenName);

        if (match.Success)
        {
            Domain = match.Groups["domain"].Value;
            Name = match.Groups["name"].Value;
            Version = match.Groups["version"].Value;

            if (match.Groups["classifier"].Success)
                Classifier = match.Groups["classifier"].Value;
        }
    }

    #region Maven Package Info

    public string Name { get; init; }
    public string Domain { get; init; }
    public string Version { get; init; }
    public string Classifier { get; init; }

    #endregion Maven Package Info

    internal string GetLibraryPath() => GetLibraryPath(this.MavenName);

    internal static string GetLibraryPath(string mavenName)
    {
        string path = "";

        var extension = mavenName.Contains('@') ? mavenName.Split('@') : [];
        var subString = extension.Length != 0
            ? mavenName.Replace($"@{extension[1]}", string.Empty).Split(':')
            : mavenName.Split(':');

        // Group name
        foreach (string item in subString[0].Split('.'))
            path = Path.Combine(path, item);

        // Artifact name + version
        path = Path.Combine(path, subString[1], subString[2]);

        // Filename of the library
        string filename = $"{subString[1]}-{subString[2]}{(subString.Length > 3 ? $"-{subString[3]}" : string.Empty)}.";
        filename += extension.Length != 0 ? extension[1] : "jar";

        return Path.Combine(path, filename);
    }

    public static MinecraftLibrary ParseJsonNode(LibraryEntry libNode, string minecraftFolderPath)
    {
        // Check platform-specific library name
        if (libNode.MavenName is null)
            throw new InvalidDataException("Invalid library name");

        if (libNode.NativeClassifierNames is not null)
            libNode.MavenName += ":" + libNode.NativeClassifierNames[EnvironmentUtil.GetPlatformName()].Replace("${arch}", EnvironmentUtil.Arch);

        if (libNode.DownloadInformation != null)
        {
            DownloadArtifactEntry artifactNode = GetLibraryArtifactInfo(libNode);
            if (artifactNode.Sha1 is null || artifactNode.Size is null || artifactNode.Url is null)
                throw new InvalidDataException("Invalid artifact node");

            #region Vanilla Pattern

            if (artifactNode.Url.StartsWith("https://libraries.minecraft.net/"))
            {
                return new VanillaLibrary(libNode.MavenName)
                {
                    MinecraftFolderPath = minecraftFolderPath,
                    Sha1 = artifactNode.Sha1,
                    Size = (long)artifactNode.Size,
                    IsNativeLibrary = libNode.NativeClassifierNames is not null
                };
            }

            #endregion Vanilla Pattern

            #region Forge Pattern

            if (artifactNode.Url.StartsWith("https://maven.minecraftforge.net/"))
            {
                return new ForgeLibrary(libNode.MavenName)
                {
                    MinecraftFolderPath = minecraftFolderPath,
                    Sha1 = artifactNode.Sha1,
                    Size = (long)artifactNode.Size,
                    Url = artifactNode.Url,
                    IsNativeLibrary = false
                };
            }

            #endregion Forge Pattern

            #region NeoForge Pattern

            if (artifactNode.Url.StartsWith("https://maven.neoforged.net/"))
            {
                return new NeoForgeLibrary(libNode.MavenName)
                {
                    MinecraftFolderPath = minecraftFolderPath,
                    Sha1 = artifactNode.Sha1,
                    Size = (long)artifactNode.Size,
                    Url = artifactNode.Url,
                    IsNativeLibrary = false
                };
            }

            #endregion NeoForge Pattern
        }

        #region Other Patterns

        if (libNode.MavenName.StartsWith("net.minecraft:launchwrapper"))
        {
            return new DownloadableDependency(libNode.MavenName, $"https://libraries.minecraft.net/{GetLibraryPath(libNode.MavenName).Replace("\\", "/")}")
            {
                MinecraftFolderPath = minecraftFolderPath,
                IsNativeLibrary = libNode.NativeClassifierNames is not null
            };
        }

        #endregion Other Patterns

        #region Legacy Forge Pattern

        if (libNode.MavenUrl == "https://maven.minecraftforge.net/"
            || libNode.ClientRequest != null
            || libNode.ServerRequest != null)
        {
            string legacyForgeLibraryUrl = (libNode.MavenUrl == "https://maven.minecraftforge.net/"
                ? "https://maven.minecraftforge.net/"
                : "https://libraries.minecraft.net/") + GetLibraryPath(libNode.MavenName).Replace("\\", "/");

            return new LegacyForgeLibrary(libNode.MavenName, legacyForgeLibraryUrl)
            {
                MinecraftFolderPath = minecraftFolderPath,
                IsNativeLibrary = false,
                ClientRequest = libNode.ClientRequest.Value || (libNode.ClientRequest == null && libNode.ServerRequest == null)
            };
        }

        #endregion Legacy Forge Pattern

        #region Fabric Pattern

        if (libNode.MavenUrl == "https://maven.fabricmc.net/")
        {
            return new FabricLibrary(libNode.MavenName)
            {
                MinecraftFolderPath = minecraftFolderPath,
                IsNativeLibrary = false,
                Size = libNode?.Size,
                Sha1 = libNode?.Sha1
            };
        }

        #endregion Fabric Pattern

        #region Quilt Pattern

        if (libNode.MavenUrl == "https://maven.quiltmc.org/repository/release/"
            && libNode.Sha1 == null && libNode.Size == null && libNode.DownloadInformation == null)
        {
            return new QuiltLibrary(libNode.MavenName)
            {
                MinecraftFolderPath = minecraftFolderPath,
                IsNativeLibrary = false
            };
        }

        #endregion Quilt Pattern

        #region OptiFine Pattern

        if (libNode.MavenName.StartsWith("optifine:optifine", StringComparison.CurrentCultureIgnoreCase)
            || libNode.MavenName.StartsWith("optifine:launchwrapper-of", StringComparison.CurrentCultureIgnoreCase))
        {
            return new OptiFineLibrary(libNode.MavenName)
            {
                IsNativeLibrary = false,
                MinecraftFolderPath = minecraftFolderPath
            };
        }

        #endregion OptiFine Pattern

        return new UnknownLibrary(libNode.MavenName)
        {
            IsNativeLibrary = false,
            MinecraftFolderPath = minecraftFolderPath
        };
    }

    private static DownloadArtifactEntry GetLibraryArtifactInfo(LibraryEntry libNode)
    {
        if (libNode.DownloadInformation is null)
            throw new InvalidDataException("The library does not contain download information");

        DownloadArtifactEntry artifact = libNode.DownloadInformation.Artifact;
        if (libNode.NativeClassifierNames is not null)
        {
            string nativeClassifier = libNode.NativeClassifierNames[EnvironmentUtil.GetPlatformName()]
                .Replace("${arch}", EnvironmentUtil.Arch);
            artifact = libNode.DownloadInformation.Classifiers?[nativeClassifier];
        }

        return artifact ?? throw new InvalidDataException("Invalid artifact information");
    }

    public override bool Equals(object obj)
    {
        if (obj is MinecraftLibrary library)
            return library.FullPath.Equals(FullPath);

        return false;
    }

    public override int GetHashCode() => FullPath.GetHashCode();

    [GeneratedRegex(@"^(?<domain>[^:]+):(?<name>[^:]+):(?<version>[^:]+)(?::(?<classifier>[^:]+))?")]
    private static partial Regex GenerateMavenParseRegex();
}

public class MinecraftClient : MinecraftDependency, IDownloadDependency, IVerifiableDependency
{
    public override string FilePath => Path.Combine("versions", ClientId, $"{ClientId}.jar");
    public string ClientId { get; init; }
    public string Url { get; init; }
    public long? Size { get; init; }
    long? IVerifiableDependency.Size => Size;
    public string Sha1 { get; init; }
}

public sealed class MinecraftAsset : MinecraftDependency, IDownloadDependency, IVerifiableDependency
{
    public string Key { get; set; }
    public long? Size { get; init; }
    public string Sha1 { get; init; }
    public string Url => $"https://resources.download.minecraft.net/{Sha1[0..2]}/{Sha1}";
    public override string FilePath => Path.Combine("assets", "objects", Sha1[0..2], Sha1);

    long? IVerifiableDependency.Size => Size;
}

public record DownloadArtifactEntry
{
    [JsonPropertyName("url")] public string Url { get; set; }
    [JsonPropertyName("size")] public long? Size { get; set; }
    [JsonPropertyName("path")] public string Path { get; set; }
    [JsonPropertyName("sha1")] public string Sha1 { get; set; }
}

public record LibraryEntry
{
    [JsonPropertyName("size")] public long? Size { get; set; }
    [JsonPropertyName("sha1")] public string Sha1 { get; set; }
    [JsonPropertyName("url")] public string MavenUrl { get; set; }
    [JsonPropertyName("clientreq")] public bool? ClientRequest { get; set; }
    [JsonPropertyName("serverreq")] public bool? ServerRequest { get; set; }
    [JsonPropertyName("rules")] public IEnumerable<RuleEntry> Rules { get; set; }
    [JsonPropertyName("downloads")] public DownloadInformationEntry DownloadInformation { get; set; }
    [JsonPropertyName("natives")] public Dictionary<string, string> NativeClassifierNames { get; set; }

    [JsonRequired]
    [JsonPropertyName("name")]
    public string MavenName { get; set; }
}

public record DownloadInformationEntry
{
    [JsonPropertyName("artifact")] public DownloadArtifactEntry Artifact { get; set; }
    [JsonPropertyName("classifiers")] public Dictionary<string, DownloadArtifactEntry> Classifiers { get; set; }
}

public record RuleEntry
{
    [JsonPropertyName("os")] public Os System { get; set; }
    [JsonPropertyName("action")] public string Action { get; set; }
}

public record Os
{
    [JsonPropertyName("name")] public string Name { get; set; }
    [JsonPropertyName("arch")] public string Arch { get; set; }
    [JsonPropertyName("version")] public string Version { get; set; }
}

public class ForgeLibrary(string mavenName) : MinecraftLibrary(mavenName), IDownloadDependency, IVerifiableDependency
{
    long? IVerifiableDependency.Size => Size;

    public long? Size { get; init; }
    public string Url { get; init; }
    public string Sha1 { get; init; }
}

public sealed class VanillaLibrary(string mavenName) : MinecraftLibrary(mavenName), IDownloadDependency, IVerifiableDependency
{
    long? IVerifiableDependency.Size => Size;

    public long? Size { get; init; }
    public string Sha1 { get; init; }
    public string Url => $"https://libraries.minecraft.net/{GetLibraryPath().Replace("\\", "/")}";
}

public sealed class NeoForgeLibrary(string mavenName) : ForgeLibrary(mavenName);

public sealed class LegacyForgeLibrary(string mavenName, string url) : MinecraftLibrary(mavenName), IDownloadDependency
{
    long? IDownloadDependency.Size => throw new NotSupportedException();

    public string Url { get; init; } = url;
    public bool ClientRequest { get; init; }
}

public sealed class OptiFineLibrary(string mavenName) : MinecraftLibrary(mavenName);

public sealed class FabricLibrary(string mavenName) : MinecraftLibrary(mavenName), IDownloadDependency, IVerifiableDependency
{
    long? IVerifiableDependency.Size => Size;

    public long? Size { get; set; }
    public string Sha1 { get; set; }
    public string Url => $"https://maven.fabricmc.net/{GetLibraryPath().Replace("\\", "/")}";
}

public class QuiltLibrary(string mavenName) : MinecraftLibrary(mavenName), IDownloadDependency, IVerifiableDependency
{
    long? IVerifiableDependency.Size => Size;

    public long? Size { get; set; }
    public string Sha1 { get; set; }
    public string Url => $"https://maven.quiltmc.org/repository/release/{GetLibraryPath().Replace("\\", "/")}";
}

public class DownloadableDependency(string mavenName, string url) : MinecraftLibrary(mavenName), IDownloadDependency
{
    long? IDownloadDependency.Size => throw new NotSupportedException();

    public string Url { get; init; } = url;
}

public sealed class UnknownLibrary(string mavenName) : MinecraftLibrary(mavenName);

public record AssetJsonEntry
{
    [JsonPropertyName("size")] public int Size { get; set; }
    [JsonPropertyName("hash")] public string Hash { get; set; }
}

[JsonSerializable(typeof(IEnumerable<LibraryEntry>))]
public sealed partial class LibraryEntryContext : JsonSerializerContext;

[JsonSerializable(typeof(Dictionary<string, AssetJsonEntry>))]
public sealed partial class AssetJsonEntryContext : JsonSerializerContext;