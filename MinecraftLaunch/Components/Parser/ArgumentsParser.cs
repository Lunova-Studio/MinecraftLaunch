using MinecraftLaunch.Base.Enums;
using MinecraftLaunch.Base.Models.Game;
using MinecraftLaunch.Base.Utilities;
using MinecraftLaunch.Extensions;
using MinecraftLaunch.Utilities;
using System.Collections.Immutable;
using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MinecraftLaunch.Components.Parser;

public sealed class ArgumentsParser
{
    private IReadOnlyList<MinecraftLibrary> _natives;
    private IReadOnlyList<MinecraftLibrary> _libraries;

    public LaunchConfig LaunchConfig { get; init; }
    public MinecraftEntry MinecraftEntry { get; init; }

    public ArgumentsParser(MinecraftEntry minecraftEntry, LaunchConfig launchConfig)
    {
        LaunchConfig = launchConfig;
        MinecraftEntry = minecraftEntry;

        LoadLibraries();
    }

    [MemberNotNullWhen(true, nameof(LaunchConfig), nameof(LaunchConfig.Account), nameof(LaunchConfig.JavaPath), nameof(LaunchConfig.MaxMemorySize), nameof(LaunchConfig.MinMemorySize))]
    private bool CanParse() =>
        LaunchConfig != null && LaunchConfig.Account != null && LaunchConfig.JavaPath != null && LaunchConfig.MaxMemorySize > 0 && LaunchConfig.MinMemorySize > 0;

    internal IReadOnlyList<MinecraftLibrary> GetNatives() => _natives;

    private void LoadLibraries()
    {
        var natives = new List<MinecraftLibrary>();
        var libraries = new List<MinecraftLibrary>();

        if (MinecraftEntry is ModifiedMinecraftEntry { HasInheritance: true } modifiedMinecraftInstance)
        {
            (var inheritedLibs, var inheritedNatives) = modifiedMinecraftInstance.InheritedMinecraft.GetRequiredLibraries();

            libraries.AddRange(inheritedLibs);
            natives.AddRange(inheritedNatives);
        }

        (var libs, var nats) = MinecraftEntry.GetRequiredLibraries();
        natives.AddRange(nats);

        foreach (var lib in libs)
        {
            MinecraftLibrary existsEqualLib = null;
            MinecraftLibrary sameNameLib = null;

            foreach (var containedLib in libraries)
            {
                if (lib.Equals(containedLib))
                {
                    existsEqualLib = containedLib;
                    break;
                }
                else if (lib.Name == containedLib.Name
                      && lib.Classifier == containedLib.Classifier
                      && lib.Domain == lib.Domain)
                {
                    sameNameLib = containedLib;
                    break;
                }
            }

            if (existsEqualLib == null)
            {
                libraries.Add(lib);

                if (sameNameLib != null)
                    libraries.Remove(sameNameLib);
            }
        }

        _libraries = libraries;
        _natives = natives;
    }

    public IEnumerable<string> Parse()
    {
        if (!CanParse())
            throw new InvalidOperationException("Missing required parameters");

        if (MinecraftEntry.ClientJarPath is null)
            throw new InvalidOperationException("Invalid GameInfo");

        // Build arguments
        var versionJsonNode = JsonNode.Parse(File.ReadAllText(MinecraftEntry.ClientJsonPath))
            ?? throw new JsonException("Failed to parse version.json");

        var entity = versionJsonNode.Deserialize(new MinecraftJsonEntryContext(JsonSerializerUtil.GetDefaultOptions()).MinecraftJsonEntry)
            ?? throw new JsonException("Failed to parse version.json");

        var vmParameters = JvmArgumentParser.Parse(entity);
        var gameParameters = GameArgumentParser.Parse(entity);

        if (MinecraftEntry is ModifiedMinecraftEntry { HasInheritance: true } inst)
        {
            var inheritedVersionEntry = JsonNode.Parse(File.ReadAllText(inst.InheritedMinecraft.ClientJsonPath))
                .Deserialize(MinecraftJsonEntryContext.Default.MinecraftJsonEntry)
                ?? throw new JsonException("Failed to parse version.json");

            vmParameters = JvmArgumentParser
                .Parse(inheritedVersionEntry)
                .Union(vmParameters);

            gameParameters = GameArgumentParser
                .Parse(inheritedVersionEntry)
                .Union(gameParameters);
        }

        var classPath = string.Join(Path.PathSeparator, _libraries.Select(lib => lib.FullPath));
        if (!string.IsNullOrEmpty(MinecraftEntry.ClientJarPath))
            classPath += Path.PathSeparator + MinecraftEntry.ClientJarPath;

        var vmParametersReplace = new Dictionary<string, string>()
        {
                { "${launcher_name}", "MinecraftLaunch" },
                { "${launcher_version}", "4" },
                { "${classpath_separator}", Path.PathSeparator.ToString() },
                { "${library_directory}", MinecraftEntry.ToLibrariesPath().ToPath() },
                { "${classpath}", classPath.ToPath() },
                {
                    "${version_name}", MinecraftEntry is ModifiedMinecraftEntry { HasInheritance: true } instance
                        ? instance.InheritedMinecraft.Id.ToPath()
                        : MinecraftEntry.Id.ToPath()
                },
                {
                    "${natives_directory}", string.IsNullOrEmpty(LaunchConfig.NativesFolder)
                    ? MinecraftEntry.ToNativesPath().ToPath()
                    : LaunchConfig.NativesFolder.ToPath()
                },
            };

        string assetIndexPath = MinecraftEntry is ModifiedMinecraftEntry { HasInheritance: true } instance2 ?
            instance2.InheritedMinecraft.AssetIndexJsonPath :
            MinecraftEntry.AssetIndexJsonPath;

        string assetIndexFilename = Path.GetFileNameWithoutExtension(assetIndexPath)
            ?? throw new InvalidOperationException("Invalid asset index path");

        string versionType = string.IsNullOrEmpty(LaunchConfig.LauncherName)
            ? MinecraftEntry.Version.Type switch
            {
                MinecraftVersionType.Release => "release",
                MinecraftVersionType.Snapshot => "snapshot",
                MinecraftVersionType.OldBeta => "old_beta",
                MinecraftVersionType.OldAlpha => "old_alpha",
                _ => ""
            }
            : LaunchConfig.LauncherName;

        var gameParametersReplace = new Dictionary<string, string>() {
                { "${auth_player_name}" , LaunchConfig.Account.Name },
                { "${auth_access_token}" , LaunchConfig.Account.AccessToken },
                { "${auth_session}" , LaunchConfig.Account.AccessToken },
                { "${auth_uuid}" ,LaunchConfig.Account.Uuid.ToString("N") },
                { "${user_type}" , LaunchConfig.Account.Type.Equals(AccountType.Microsoft) ? "MSA" : "Mojang" },
                { "${user_properties}" , "{}" },
                { "${version_name}" , MinecraftEntry.Id.ToPath() },
                { "${version_type}" , versionType },
                { "${game_assets}" , Path.Combine(MinecraftEntry.MinecraftFolderPath, "assets").ToPath() },
                { "${assets_root}" , Path.Combine(MinecraftEntry.MinecraftFolderPath, "assets").ToPath() },
                { "${game_directory}" , MinecraftEntry.ToWorkingPath(LaunchConfig.IsEnableIndependency).ToPath() },
                { "${assets_index_name}" , assetIndexFilename },
        };

        var parentFolderPath = Directory.GetParent(MinecraftEntry.MinecraftFolderPath)?.FullName
            ?? throw new InvalidOperationException("Invalid Minecraft folder path");

        yield return $"-Xms{LaunchConfig.MinMemorySize}M";
        yield return $"-Xmx{LaunchConfig.MaxMemorySize}M";
        yield return $"-Dminecraft.client.jar={MinecraftEntry.ClientJarPath.ToPath()}";
        yield return "-Dlog4j2.formatMsgNoLookups=true";

        foreach (var arg in JvmArgumentParser.GetEnvironmentJvmArguments()) yield return arg;
        foreach (var arg in vmParameters) yield return arg.ReplaceFromDictionary(vmParametersReplace);
        foreach (var arg in LaunchConfig.JvmArguments) yield return arg;

        yield return entity.MainClass!;

        foreach (var arg in gameParameters) yield return arg.ReplaceFromDictionary(gameParametersReplace);

        if (LaunchConfig.Width != 0 && LaunchConfig.Height != 0)
        {
            yield return $"--width {LaunchConfig.Width}";
            yield return $"--height {LaunchConfig.Height}";
        }

        bool isHighVersion = MinecraftEntry.ReleaseTime > new DateTime(2023, 4, 4);
        if (!string.IsNullOrWhiteSpace(LaunchConfig.SaveName) && isHighVersion)
            yield return $"--quickPlaySingleplayer {LaunchConfig.SaveName.ToPath()}";

        if (LaunchConfig.ServerInfo is not null)
        {
            if (isHighVersion)
            {
                yield return $"--quickPlayMultiplayer {LaunchConfig.ServerInfo.Address.ToPath()}";
                yield break;
            }

            yield return $"--server {LaunchConfig.ServerInfo.Address.ToPath()}";
            yield return $"--port {LaunchConfig.ServerInfo.Port}";
        }
    }
}

/// <summary>
/// 游戏参数解析器
/// </summary>
internal sealed class GameArgumentParser
{
    /// <summary>
    /// 解析参数
    /// </summary>
    /// <returns></returns>
    public static IEnumerable<string> Parse(MinecraftJsonEntry gameJsonEntry)
    {
        if (!string.IsNullOrEmpty(gameJsonEntry.MinecraftArguments))
        {
            foreach (var arg in gameJsonEntry.MinecraftArguments.Split(' ').GroupArguments())
            {
                yield return arg;
            }
        }

        if (gameJsonEntry.Arguments?.GetEnumerable("game") is null)
        {
            yield break;
        }

        var game = gameJsonEntry.Arguments.GetEnumerable("game")
            .Where(x => x.GetValueKind() is JsonValueKind.String)
            .Select(x => x.GetString().ToPath())
            .GroupArguments();

        foreach (var item in game.ToImmutableArray())
            yield return item;
    }
}

/// <summary>
/// Jvm 虚拟机参数解析器
/// </summary>
internal sealed class JvmArgumentParser
{
    public static IEnumerable<string> Parse(MinecraftJsonEntry gameJsonEntry)
    {
        var jvm = new List<string>();

        if (gameJsonEntry.Arguments.GetEnumerable("jvm") is null)
        {
            yield return "-Djava.library.path=${natives_directory}";
            yield return "-Dminecraft.launcher.brand=${launcher_name}";
            yield return "-Dminecraft.launcher.version=${launcher_version}";
            yield return "-cp ${classpath}";
            yield break;
        }

        foreach (var arg in gameJsonEntry.Arguments.GetEnumerable("jvm"))
        {
            if (arg.GetValueKind() is JsonValueKind.String)
            {
                var argValue = arg.GetString().Trim();

                if (argValue.Contains(' '))
                    jvm.AddRange(argValue.Split(' '));
                else
                    jvm.Add(argValue);
            }
        }

        foreach (var arg in jvm.GroupArguments())
            yield return arg;

        //有些沟槽的带加载器的版本的 Json 里可能没有 -cp 键，加一个判断以防启动失败
        if (!jvm.Contains("-cp"))
            yield return "-cp ${classpath}";
    }

    /// <summary>
    /// 获取虚拟机环境参数
    /// </summary>
    /// <returns></returns>
    public static IEnumerable<string> GetEnvironmentJvmArguments()
    {
        switch (EnvironmentUtil.GetPlatformName())
        {
            case "windows":
                yield return "-XX:HeapDumpPath=MojangTricksIntelDriversForPerformance_javaw.exe_minecraft.exe.heapdump";
                if (Environment.OSVersion.Version.Major == 10)
                {
                    yield return "-Dos.name=\"Windows 10\"";
                    yield return "-Dos.version=10.0";
                }
                break;

            case "osx":
                yield return "-XstartOnFirstThread";
                break;
        }

        if (EnvironmentUtil.Arch == "32")
            yield return "-Xss1M";
    }
}