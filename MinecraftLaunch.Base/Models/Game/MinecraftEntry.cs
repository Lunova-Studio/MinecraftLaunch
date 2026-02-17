using System.Buffers;
using System.Diagnostics;
using MinecraftLaunch.Base.Interfaces;
using MinecraftLaunch.Base.Utilities;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using MinecraftLaunch.Base.Models.SHA1;
#if DEBUG
using Xunit;
#endif

namespace MinecraftLaunch.Base.Models.Game;

[JsonDerivedType(typeof(VanillaMinecraftEntry), typeDiscriminator: "vanilla")]
[JsonDerivedType(typeof(ModifiedMinecraftEntry), typeDiscriminator: "modified")]
public abstract class MinecraftEntry {
    public required string Id { get; init; }
    public required MinecraftVersion Version { get; init; }

    public required string ClientJarPath { get; init; }
    public required DateTime ReleaseTime { get; init; }
    public required string ClientJsonPath { get; init; }
    public required string AssetIndexJsonPath { get; init; }
    public required string MinecraftFolderPath { get; init; }

    public bool IsVanilla => this is VanillaMinecraftEntry;

    private static bool IsLibraryEnabled(IEnumerable<RuleEntry> rules) {
        bool windows, linux, osx;
        windows = linux = osx = false;

        foreach (var item in rules) {
            if (item.Action == "allow") {
                if (item.System == null) {
                    windows = linux = osx = true;
                    continue;
                }

                switch (item.System.Name) {
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
            } else if (item.Action == "disallow") {
                if (item.System == null) {
                    windows = linux = osx = false;
                    continue;
                }

                switch (item.System.Name) {
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

        return EnvironmentUtil.GetPlatformName() switch {
            "windows" => windows,
            "linux" => linux,
            "osx" => osx,
            _ => false,
        };
    }
    
    public IEnumerable<MinecraftAsset> GetRequiredAssets() {
        // Identify file paths
        var assetIndexJsonPath =
            this is ModifiedMinecraftEntry { HasInheritance: true } inner
                ? inner.InheritedMinecraft.AssetIndexJsonPath
                : this.AssetIndexJsonPath;
        // Parse asset index json
        
        using var stream = File.OpenRead(assetIndexJsonPath);
        using var doc = JsonDocument.Parse(stream);
        var root = doc.RootElement;
        if (!root.TryGetProperty("objects"u8, out var value))
            throw new InvalidDataException("Error in parsing asset index json file");
        var assets = value;

        // Parse GameAsset objects
        foreach (var item in assets.EnumerateObject()) {
            var size = item.Value.GetProperty("size"u8).GetInt32();
            var hash = item.Value.GetProperty("hash"u8).Deserialize(Sha1Data.Sha1DataSerializerContext.Default.Sha1Data);

            yield return new MinecraftAsset {
                MinecraftFolderPath = MinecraftFolderPath,
                Key = item.Name,
                Sha1 = hash,
                Size = size
            };
        }
    }
    
    public (IEnumerable<MinecraftLibrary> Libraries, IEnumerable<MinecraftLibrary> NativeLibraries) GetRequiredLibraries() {
        List<MinecraftLibrary> libs = [];
        List<MinecraftLibrary> nativeLibs = [];
        IEnumerable<LibraryEntry> libNodes;
        using (var stream = File.OpenRead(ClientJsonPath))
        using (var doc = JsonDocument.Parse(stream))
        {
            var root = doc.RootElement;
            if (!root.TryGetProperty("libraries"u8, out var librariesElement))
                throw new InvalidDataException("client.json does not contain library information");
            libNodes =
                librariesElement.Deserialize(LibraryEntriesContext.Default.IEnumerableLibraryEntry)
                ?? throw new InvalidDataException("client.json does not contain library information");
        }

        foreach (var libNode in libNodes)
        {
            if (libNode is null)
                continue;

            // Check if a library is enabled
            if (libNode.Rules is { } libRules)
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

    public override bool Equals(object obj) {
        if (obj is MinecraftEntry other) {
            return string.Equals(Id, other.Id, StringComparison.OrdinalIgnoreCase)
                && string.Equals(MinecraftFolderPath, other.MinecraftFolderPath, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    public override int GetHashCode() {
        return HashCode.Combine(Id?.ToLowerInvariant(),
            MinecraftFolderPath?.ToLowerInvariant());
    }
}

[JsonSerializable(typeof(IEnumerable<LibraryEntry>))]
public sealed partial class LibraryEntriesContext : JsonSerializerContext;

public class VanillaMinecraftEntry : MinecraftEntry;

public class ModifiedMinecraftEntry : MinecraftEntry {
    public required IEnumerable<ModLoaderInfo> ModLoaders { get; init; }

    public VanillaMinecraftEntry InheritedMinecraft { get; init; }

    [MemberNotNullWhen(true, nameof(InheritedMinecraft))]
    public bool HasInheritance { get => InheritedMinecraft is not null; }
}

public abstract class MinecraftDependency {
    /// <summary>
    /// Absolute path of the .minecraft folder
    /// </summary>
    public required string MinecraftFolderPath { get; init; }

    /// <summary>
    /// File path relative to the .minecraft folder
    /// </summary>
    public abstract string FilePath { get; }

    /// <summary>
    /// Absolute path of the file
    /// </summary>
    public string FullPath => Path.Combine(MinecraftFolderPath, FilePath);
}

public abstract partial class MinecraftLibrary : MinecraftDependency {
    private readonly static Regex MavenParseRegex = GenerateMavenParseRegex();

    public string MavenName { get; init; }
    public required bool IsNativeLibrary { get; init; }
    public override string FilePath => Path.Combine("libraries", GetLibraryPath());

    public MinecraftLibrary(string mavenName) {
        this.MavenName = mavenName;
        Match match = MavenParseRegex.Match(mavenName);

        if (match.Success) {
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

    #endregion

    internal string GetLibraryPath() => GetLibraryPath(this.MavenName);

#if DEBUG

    internal static string GetLibraryPathOld(string mavenName) {
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
    
    [Theory]
    [Trait("Category", "Debug")]
    // 这些是AI生成的测试样例
    // 基础格式 (group:artifact:version)
    [InlineData("com.google.guava:guava:31.1-jre")]
    [InlineData("org.springframework:spring-core:5.3.23")]
    [InlineData("io.netty:netty-all:4.1.82.Final")]
// 带 Classifier (group:artifact:version:classifier)
    [InlineData("org.lwjgl:lwjgl:3.3.1:natives-windows")]
    [InlineData("com.android.tools.build:gradle:7.2.0:sources")]
    [InlineData("org.jetbrains.kotlin:kotlin-stdlib:1.7.10:javadoc")]
// 带扩展名 (group:artifact:version@extension)
    [InlineData("com.google.android:android:4.1.1.4@aar")]
    [InlineData("androidx.appcompat:appcompat:1.5.1@aar")]
    [InlineData("com.squareup.okhttp3:okhttp:4.10.0@pom")]
// 完整格式 (group:artifact:version:classifier@extension)
    [InlineData("org.lwjgl:lwjgl-glfw:3.3.1:natives-linux@jar")]
    [InlineData("com.android.support:support-v4:28.0.0:sources@jar")]
    [InlineData("io.fabric8:kubernetes-client:6.0.0:tests@jar")]
// 多层级 Group
    [InlineData("org.apache.logging.log4j:log4j-core:2.19.0")]
    [InlineData("com.fasterxml.jackson.core:jackson-databind:2.13.4")]
    [InlineData("org.hibernate.validator:hibernate-validator:7.0.5.Final")]
// 边界情况
    [InlineData("a:b:1.0")]
    [InlineData("com.example:my-lib:1.0.0-SNAPSHOT")]
    [InlineData("org.test:artifact:1.0-beta.1:classifier@zip")]
    [InlineData("io.a.b.c.d.e.f:deep-artifact:1.0.0")]
// 特殊版本号
    [InlineData("com.google.code.gson:gson:2.10.1")]
    [InlineData("org.junit.jupiter:junit-jupiter:5.9.0-M1")]
    [InlineData("com.squareup.retrofit2:retrofit:2.9.0-RC1")]
    public static void T2T(string src)
    {
        var libraryPathOld = GetLibraryPathOld(src);
        var actualMemory = GetLibraryPath(src);
        Console.WriteLine(actualMemory);
        Console.WriteLine(libraryPathOld);
        Assert.Equal(libraryPathOld,actualMemory);
    }
#endif

    internal static string GetLibraryPath(string mavenName)
    {
        scoped Span<Range> extensionRanges = stackalloc Range[2];
        var extensionCount = mavenName.AsSpan().Split(extensionRanges, '@');
        var mainSpan = mavenName.AsSpan(extensionRanges[0]);
        var extensionSpan = extensionCount > 1 ? mavenName.AsSpan(extensionRanges[1]) : default;

        scoped Span<Range> subRanges = stackalloc Range[4];
        var subCount = mainSpan.Split(subRanges, ':');
        Debug.Assert(subCount >= 3, "Maven name must have at least group:artifact:version");

        // 申请缓冲区
        var bufferSize = mavenName.Length + mainSpan[subRanges[0]].Length + 40;
        var buffer = ArrayPool<char>.Shared.Rent(bufferSize);
        var offset = 0;

        try
        {
            // 1. 处理 Group name（替换 . 为 Path.DirectorySeparatorChar）
            scoped var groupSpan = mainSpan[subRanges[0]];
            groupSpan.Replace(buffer, '.', Path.DirectorySeparatorChar);
            offset += groupSpan.Length;
            buffer[offset++] = Path.DirectorySeparatorChar;

            // 2. 处理 Artifact name
            scoped var artifactSpan = mainSpan[subRanges[1]];
            artifactSpan.CopyTo(buffer.AsSpan(offset));
            offset += artifactSpan.Length;
            buffer[offset++] = Path.DirectorySeparatorChar;

            // 3. 处理 Version
            scoped var versionSpan = mainSpan[subRanges[2]];
            versionSpan.CopyTo(buffer.AsSpan(offset));
            offset += versionSpan.Length;
            buffer[offset++] = Path.DirectorySeparatorChar;

            // 4.1 Artifact
            artifactSpan.CopyTo(buffer.AsSpan(offset));
            offset += artifactSpan.Length;

            // 4.2 -version
            buffer[offset++] = '-';
            versionSpan.CopyTo(buffer.AsSpan(offset));
            offset += versionSpan.Length;

            // 4.3 -classifier
            if (subCount > 3)
            {
                buffer[offset++] = '-';
                scoped var classifierSpan = mainSpan[subRanges[3]];
                classifierSpan.CopyTo(buffer.AsSpan(offset));
                offset += classifierSpan.Length;
            }

            // 4.4 .extension
            buffer[offset++] = '.';
            if (!extensionSpan.IsEmpty)
            {
                extensionSpan.CopyTo(buffer.AsSpan(offset));
                offset += extensionSpan.Length;
            }
            else
            {
                "jar".AsSpan().CopyTo(buffer.AsSpan(offset));
                offset += 3;
            }

            // 5. 生成最终字符串
            return new string(buffer.AsSpan(0, offset));
        }
        finally
        {
            ArrayPool<char>.Shared.Return(buffer);
        }
    }

    public static MinecraftLibrary ParseJsonNode(LibraryEntry libNode, string minecraftFolderPath) {
        // Check platform-specific library name
        if (libNode.MavenName is null)
            throw new InvalidDataException("Invalid library name");

        if (libNode.NativeClassifierNames is not null)
            libNode.MavenName += ":" + libNode.NativeClassifierNames[EnvironmentUtil.GetPlatformName()].Replace("${arch}", EnvironmentUtil.Arch,StringComparison.Ordinal);

        if (libNode.DownloadInformation != null) {
            DownloadArtifactEntry artifactNode = GetLibraryArtifactInfo(libNode);
            if (artifactNode.Size is null || artifactNode.Url is null)
                throw new InvalidDataException("Invalid artifact node");

            #region Vanilla Pattern

            if (artifactNode.Url.StartsWith("https://libraries.minecraft.net/", StringComparison.Ordinal)) {
                return new VanillaLibrary(libNode.MavenName) {
                    MinecraftFolderPath = minecraftFolderPath,
                    Sha1 = artifactNode.Sha1,
                    Size = (long)artifactNode.Size,
                    IsNativeLibrary = libNode.NativeClassifierNames is not null
                };
            }

            #endregion

            #region Forge Pattern
            
            if (artifactNode.Url.StartsWith("https://maven.minecraftforge.net/", StringComparison.Ordinal)) {
                return new ForgeLibrary(libNode.MavenName) {
                    MinecraftFolderPath = minecraftFolderPath,
                    Sha1 = artifactNode.Sha1,
                    Size = (long)artifactNode.Size,
                    Url = artifactNode.Url,
                    IsNativeLibrary = false
                };
            }

            #endregion

            #region NeoForge Pattern

            if (artifactNode.Url.StartsWith("https://maven.neoforged.net/", StringComparison.Ordinal)) {
                return new NeoForgeLibrary(libNode.MavenName) {
                    MinecraftFolderPath = minecraftFolderPath,
                    Sha1 = artifactNode.Sha1,
                    Size = (long)artifactNode.Size,
                    Url = artifactNode.Url,
                    IsNativeLibrary = false
                };
            }

            #endregion
        }

        #region Other Patterns

        if (libNode.MavenName.StartsWith("net.minecraft:launchwrapper", StringComparison.Ordinal)) {
            return new DownloadableDependency(libNode.MavenName, $"https://libraries.minecraft.net/{GetLibraryPath(libNode.MavenName).Replace('\\', '/')}") {
                MinecraftFolderPath = minecraftFolderPath,
                IsNativeLibrary = libNode.NativeClassifierNames is not null
            };
        }

        #endregion

        #region Legacy Forge Pattern

        if (libNode.MavenUrl == "https://maven.minecraftforge.net/"
            || libNode.ClientRequest != null
            || libNode.ServerRequest != null) {
            string legacyForgeLibraryUrl = (libNode.MavenUrl == "https://maven.minecraftforge.net/"
                ? "https://maven.minecraftforge.net/"
                : "https://libraries.minecraft.net/") + GetLibraryPath(libNode.MavenName).Replace('\\','/');

            return new LegacyForgeLibrary(libNode.MavenName, legacyForgeLibraryUrl) {
                MinecraftFolderPath = minecraftFolderPath,
                IsNativeLibrary = false,
                ClientRequest = libNode.ClientRequest.Value || (libNode.ClientRequest == null && libNode.ServerRequest == null)
            };
        }

        #endregion

        #region Fabric Pattern

        if (libNode.MavenUrl == "https://maven.fabricmc.net/") {
            return new FabricLibrary(libNode.MavenName) {
                MinecraftFolderPath = minecraftFolderPath,
                IsNativeLibrary = false,
                Size = libNode.Size,
                Sha1 = libNode.Sha1!.Value
            };
        }

        #endregion

        #region Quilt Pattern

        if (libNode.MavenUrl == "https://maven.quiltmc.org/repository/release/"
            && libNode.Sha1 == null && libNode.Size == null && libNode.DownloadInformation == null) {
            return new QuiltLibrary(libNode.MavenName) {
                MinecraftFolderPath = minecraftFolderPath,
                IsNativeLibrary = false
            };
        }

        #endregion

        #region OptiFine Pattern
        
        if (libNode.MavenName.StartsWith("optifine:optifine", StringComparison.Ordinal)
            || libNode.MavenName.StartsWith("optifine:launchwrapper-of", StringComparison.Ordinal)) {
            return new OptiFineLibrary(libNode.MavenName) {
                IsNativeLibrary = false,
                MinecraftFolderPath = minecraftFolderPath
            };
        }

        #endregion

        return new UnknownLibrary(libNode.MavenName) {
            IsNativeLibrary = false,
            MinecraftFolderPath = minecraftFolderPath
        };
    }

    private static DownloadArtifactEntry GetLibraryArtifactInfo(LibraryEntry libNode) {
        if (libNode.DownloadInformation is null)
            throw new InvalidDataException("The library does not contain download information");

        DownloadArtifactEntry artifact = libNode.DownloadInformation.Artifact;
        if (libNode.NativeClassifierNames is not null) {
            string nativeClassifier = libNode.NativeClassifierNames[EnvironmentUtil.GetPlatformName()]
                .Replace("${arch}", EnvironmentUtil.Arch);
            artifact = libNode.DownloadInformation.Classifiers?[nativeClassifier];
        }

        return artifact ?? throw new InvalidDataException("Invalid artifact information");
    }

    public override bool Equals(object obj) {
        if (obj is MinecraftLibrary library)
            return library.FullPath.Equals(FullPath);

        return false;
    }

    public override int GetHashCode() => FullPath.GetHashCode();

    [GeneratedRegex(@"^(?<domain>[^:]+):(?<name>[^:]+):(?<version>[^:]+)(?::(?<classifier>[^:]+))?")]
    private static partial Regex GenerateMavenParseRegex();
}

public sealed class MinecraftClient : MinecraftDependency, IDownloadDependency, IVerifiableDependency {
    public override string FilePath => Path.Combine("versions", ClientId, $"{ClientId}.jar");
    public required string ClientId { get; init; }
    public required string Url { get; init; }
    public required long? Size { get; init; }
    long? IVerifiableDependency.Size => Size;
    public required Sha1Data? Sha1 { get; init; }
}

public sealed class MinecraftAsset : MinecraftDependency, IDownloadDependency, IVerifiableDependency {
    public required string Key { get; set; }
    public required long? Size { get; init; }
    public required Sha1Data? Sha1 { get; init; }


    public string Url
    {
        get
        {
            var buf = (Span<char>)stackalloc char[40];
            Sha1!.Value.FormatTo(buf);
            return $"https://resources.download.minecraft.net/{buf[..2]}/{buf}";
        }
    }

    public override string FilePath
    {
        get
        {
            var buf = (Span<char>)stackalloc char[40];
            Sha1!.Value.FormatTo(buf);
            return $"assets{Path.DirectorySeparatorChar}objects{Path.DirectorySeparatorChar}{buf[..2]}{Path.DirectorySeparatorChar}{buf}";
        }
    }

    long? IVerifiableDependency.Size => Size;
}

public sealed record DownloadArtifactEntry {
    [JsonPropertyName("url")] public string Url { get; set; }
    [JsonPropertyName("size")] public long? Size { get; set; }
    [JsonPropertyName("path")] public string Path { get; set; }
    [JsonPropertyName("sha1")] public Sha1Data? Sha1 { get; set; }
}

public sealed record LibraryEntry {
    [JsonPropertyName("size")] public long? Size { get; set; }
    [JsonPropertyName("sha1")] public Sha1Data? Sha1 { get; set; }
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

public sealed record DownloadInformationEntry {
    [JsonPropertyName("artifact")] public DownloadArtifactEntry Artifact { get; set; }
    [JsonPropertyName("classifiers")] public Dictionary<string, DownloadArtifactEntry> Classifiers { get; set; }
}

public sealed record RuleEntry {
    [JsonPropertyName("os")] public Os System { get; set; }
    [JsonPropertyName("action")] public string Action { get; set; }
}

public sealed record Os {
    [JsonPropertyName("name")] public string Name { get; set; }
    [JsonPropertyName("arch")] public string Arch { get; set; }
    [JsonPropertyName("version")] public string Version { get; set; }
}

public class ForgeLibrary(string mavenName) : MinecraftLibrary(mavenName), IDownloadDependency, IVerifiableDependency {
    long? IVerifiableDependency.Size => Size;

    public required long? Size { get; init; }
    public required string Url { get; init; }
    public required Sha1Data? Sha1 { get; init; }
}

public sealed class VanillaLibrary(string mavenName) : MinecraftLibrary(mavenName), IDownloadDependency, IVerifiableDependency {
    private string _url;
    long? IVerifiableDependency.Size => Size;

    public required long? Size { get; init; }
    public required Sha1Data? Sha1 { get; init; }
    // 值是不变的,添加缓存
    public string Url => _url ??= $"https://libraries.minecraft.net/{GetLibraryPath().Replace('\\', '/')}";
}

public sealed class NeoForgeLibrary(string mavenName) : ForgeLibrary(mavenName);

public sealed class LegacyForgeLibrary(string mavenName, string url) : MinecraftLibrary(mavenName), IDownloadDependency {
    long? IDownloadDependency.Size => throw new NotSupportedException();

    public string Url { get; init; } = url;
    public required bool ClientRequest { get; init; }
}

public sealed class OptiFineLibrary(string mavenName) : MinecraftLibrary(mavenName);

public sealed class FabricLibrary(string mavenName) : MinecraftLibrary(mavenName), IDownloadDependency, IVerifiableDependency {
    private string _url;
    long? IVerifiableDependency.Size => Size;

    public long? Size { get; set; }
    public Sha1Data? Sha1 { get; set; }
    // 添加缓存
    public string Url => _url ??=$"https://maven.fabricmc.net/{GetLibraryPath().Replace('\\', '/')}";
}

public sealed class QuiltLibrary(string mavenName) : MinecraftLibrary(mavenName), IDownloadDependency, IVerifiableDependency {
    private string _url;
    long? IVerifiableDependency.Size => Size;

    public long? Size { get; set; }
    public Sha1Data? Sha1 { get; set; }
    public string Url => _url ??= $"https://maven.quiltmc.org/repository/release/{GetLibraryPath().Replace('\\', '/')}";
}

public sealed class DownloadableDependency(string mavenName, string url) : MinecraftLibrary(mavenName), IDownloadDependency {
    long? IDownloadDependency.Size => throw new NotSupportedException();

    public string Url { get; init; } = url;
}

public sealed class UnknownLibrary(string mavenName) : MinecraftLibrary(mavenName);

public sealed record AssetJsonEntry {
    [JsonPropertyName("size")] public int Size { get; set; }
    [JsonPropertyName("hash")] public Sha1Data Hash { get; set; }
}

[JsonSerializable(typeof(IEnumerable<LibraryEntry>))]
public sealed partial class LibraryEntryContext : JsonSerializerContext;

[JsonSerializable(typeof(Dictionary<string, AssetJsonEntry>))]
public sealed partial class AssetJsonEntryContext : JsonSerializerContext;