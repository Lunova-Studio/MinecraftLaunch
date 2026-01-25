using MinecraftLaunch.Base.Enums;
using MinecraftLaunch.Base.Models.Game;
using MinecraftLaunch.Base.Models.Logging;
using MinecraftLaunch.Extensions;
using System.Collections.Frozen;
using System.Text.RegularExpressions;

namespace MinecraftLaunch.Components.Logging;

public sealed partial class LogAnalyzer
{
    public MinecraftEntry Minecraft { get; set; }
    public IReadOnlyList<string> LogFiles { get; set; }

    public LogAnalyzer(MinecraftEntry minecraft, IReadOnlyList<string> logFiles = default)
    {
        Minecraft = minecraft;
        LogFiles = logFiles ?? [];
    }

    public LogAnalyzerResult Analyze()
    {
        var logs = GetAllLogs();
        var reasons = new List<CrashReasons>();
        var suspiciousMods = new List<string>();

        foreach (var log in logs)
        {
            //roughly match the crash reason
            foreach (var (key, value) in RoughCrashReasons)
                if (log.Contains(key))
                    reasons.Add(value);

            if (log.Contains("Could not reserve enough space"))
            {
                reasons.Add(log.Contains("for 1048576KB object heap")
                    ? CrashReasons.Using32BitJavaCausedInsufficientJVMMemory
                    : CrashReasons.InsufficientMemory);
            }

            if (log.Contains("Caught exception from"))
            {
                reasons.Add(CrashReasons.ModCausedGameCrash);
                suspiciousMods.AddRange(FindSuspiciousModId(ModCrashIdentifier()
                    .Match(log).Value.TrimEnd('\r', '\n', ' '), log));
            }

            if (log.Contains("Failed to create mod instance."))
            {
                reasons.Add(CrashReasons.ModInitializationFailed);
                suspiciousMods.AddRange(FindSuspiciousModId((FailedModInstanceIdentifier1().IsMatch(log)
                    ? FailedModInstanceIdentifier1().Match(log).Value
                    : FailedModInstanceIdentifier2().Match(log).Value).TrimEnd('\r', '\n'), log));
            }

            if (MixinInjectionFailureIdentifier().IsMatch(log) ||
                log.Contains("mixin.injection.throwables.") ||
                log.Contains(".mixins.json] FAILED during )"))
            {
                var mod = MixinFailureIdentifierRegex().Match(log).Value;
                if (string.IsNullOrWhiteSpace(mod))
                    mod = FallbackModIdentifier().Match(log).Value;

                if (string.IsNullOrWhiteSpace(mod))
                    mod = FailedDuringIdentifier().Match(log).Value;

                if (string.IsNullOrWhiteSpace(mod))
                    mod = MixinCallbackFailureIdentifier().Match(log).Value;

                reasons.Add(CrashReasons.ModMixinFailed);
                suspiciousMods.AddRange(FindSuspiciousModId(mod.TrimEnd(("\r\n" + " ").ToCharArray()), log));
            }

            if (log.Contains("-- MOD "))
            {
                var loglast = log.Split("-- MOD")
                    .LastOrDefault();

                reasons.Add(loglast.Contains("Failure message: MISSING")
                    ? CrashReasons.ModCausedGameCrash
                    : CrashReasons.ModLoaderError);
            }
        }

        return new LogAnalyzerResult
        {
            Minecraft = Minecraft,
            CrashReasons = reasons.Distinct().ToList(),
            SuspiciousMods = []
        };
    }

    #region Privates

    private readonly FrozenDictionary<string, CrashReasons> RoughCrashReasons = new Dictionary<string, CrashReasons> {
        { "Unable to make protected final java.lang.Class java.lang.ClassLoader.defineClass", CrashReasons.JavaVersionTooHigh },
        { "Unsupported class file major version", CrashReasons.JavaVersionTooHigh },
        { "because module java.base does not export", CrashReasons.JavaVersionTooHigh },
        { "jdk.nashorn.api.scripting.NashornScriptEngineFactory", CrashReasons.JavaVersionTooHigh },
        { "java.lang.invoke.LambdaMetafactory", CrashReasons.JavaVersionTooHigh },
        { "Found multiple arguments for option fml.forgeVersion, but you asked for only one", CrashReasons.MultipleForgeInVersionJson },
        { "The driver does not appear to support OpenGL", CrashReasons.GraphicsCardDoesNotSupportOpenGL },
        { "java.lang.ClassCastException: java.base/jdk", CrashReasons.UsingJDK },
        { "java.lang.ClassCastException: class jdk", CrashReasons.UsingJDK },
        { "Cannot read field \"ofTelemetry\" because \"net.optifine.Config.gameSettings\" is null", CrashReasons.OptiFineIncompatibleWithForge },
        { "TRANSFORMER/net.optifine/net.optifine.reflect.Reflector.<clinit>(Reflector.java", CrashReasons.OptiFineIncompatibleWithForge },
        { "Open J9 is not supported", CrashReasons.UsingOpenJ9 },
        { "OpenJ9 is incompatible", CrashReasons.UsingOpenJ9 },
        { ".J9VMInternals.", CrashReasons.UsingOpenJ9 },
        { "The directories below appear to be extracted jar files. Fix this before you continue.", CrashReasons.ModFileDecompressed },
        { "Extracted mod jars found, loading will NOT continue", CrashReasons.ModFileDecompressed },
        { "java.lang.OutOfMemoryError", CrashReasons.InsufficientMemory },
        { "java.lang.NoSuchMethodError: sun.security.util.ManifestEntryVerifie", CrashReasons.LowVersionForgeIncompatibleWithHighVersionJava },
        { "1282: Invalid operation", CrashReasons.ShaderOrResourcePackCausedOpenGL1282Error },
        { "signer information does not match signer information of other classes in the same package", CrashReasons.FileOrContentCheckFailed },
        { "An exception was thrown, the game will display an error screen and halt.", CrashReasons.ForgeError },
        { "A potential solution has been determined", CrashReasons.FabricErrorWithSolution },
        { "Maybe try a lower resolution resourcepack", CrashReasons.TextureTooLargeOrInsufficientGraphicsConfig },
        { "java.lang.NoSuchMethodError: net.minecraft.world.server.ChunkManager$ProxyTicketManager.shouldForceTicks(J)Z", CrashReasons.OptiFineCausedWorldLoadingFailure },
        { "ModResolutionException: Duplicate", CrashReasons.ModInstalledRepeatedly },
        { "Found a duplicate mod", CrashReasons.ModInstalledRepeatedly },
        { "DuplicateModsFoundException", CrashReasons.ModInstalledRepeatedly },
        { "maximum id range exceeded", CrashReasons.TooManyModsExceededIdLimit },
        { "Manually triggered debug crash", CrashReasons.PlayerTriggeredDebugCrash },
    }.ToFrozenDictionary();

    [GeneratedRegex(@"\t\tfabric[\w-]*: Fabric")]
    private static partial Regex FabricModIdentifier();

    [GeneratedRegex(@"(?<=: )[^\n]+(?= [^\n]+)")]
    private static partial Regex ExtractFabricModIdentifier();

    [GeneratedRegex(@"(?<=\()[^\t]+.jar(?=\))|(?<=(\t\t)|(\| ))[^\t\|]+.jar", RegexOptions.IgnoreCase, "zh-CN")]
    private static partial Regex ExtractOtherModIdentifier();

    [GeneratedRegex("[^\n]+?()")]
    private static partial Regex ModCrashIdentifier();

    [GeneratedRegex("(?<=Failed to create mod instance. ModID: )[^,]+")]
    private static partial Regex FailedModInstanceIdentifier1();

    [GeneratedRegex(@"(?<=Failed to create mod instance. ModId )[^\n]+(?= for )")]
    private static partial Regex FailedModInstanceIdentifier2();

    [GeneratedRegex("(?<=in )[^./ ]+(?=.mixins.json.+failed injection check)")]
    private static partial Regex MixinInjectionFailureIdentifier();

    [GeneratedRegex("(?<=in )[^./ ]+(?=.mixins.json.+failed injection check)")]
    private static partial Regex MixinFailureIdentifierRegex();

    [GeneratedRegex("(?<= failed .+ in )[^./ ]+(?=.mixins.json)")]
    private static partial Regex FallbackModIdentifier();

    [GeneratedRegex(@"(?<= in config \[)[^./ ]+(?=.mixins.json\] FAILED during )")]
    private static partial Regex FailedDuringIdentifier();

    [GeneratedRegex("(?<= in callback )[^./ ]+(?=.mixins.json:)")]
    private static partial Regex MixinCallbackFailureIdentifier();

    private IEnumerable<string> GetAllLogs()
    {
        ArgumentException.ThrowIfNullOrEmpty(nameof(Minecraft));

        List<string> logFiles = [];

        if (LogFiles is { Count: > 0 })
            logFiles.AddRange(LogFiles);

        logFiles.Add(Path.Combine(Minecraft.ToLogsPath(true), "debug.log"));
        logFiles.Add(Path.Combine(Minecraft.ToLogsPath(true), "latest.log"));

        logFiles = logFiles.Distinct().Where(File.Exists)
            .ToList();

        foreach (var logFile in logFiles)
        {
            var log = File.ReadAllText(logFile);
            if (string.IsNullOrWhiteSpace(log))
                continue;

            yield return log;
        }
    }

    private IEnumerable<string> FindSuspiciousModId(string log, string fullLog)
    {
        if (string.IsNullOrWhiteSpace(log))
            yield break;

        foreach (var modId in TryFindSuspiciousModId([log], fullLog))
            yield return modId;
    }

    private IEnumerable<string> TryFindSuspiciousModId(IEnumerable<string> logs, string fullLog)
    {
        var realLogs = logs.SelectMany(x => x.Split('('))
            .Select(x => x.Trim(' ', ')'));

        if (!fullLog.Contains("A detailed walkthrough of the error"))
            yield break;

        var details = fullLog.Replace("A detailed walkthrough of the error", "¨");
        var isFabricMod = details.Contains("Fabric Mods");

        if (isFabricMod)
            details = details.Replace("Fabric Mods", "¨");

        details = details.Split('¨').LastOrDefault();

        //The FoegeMod is get all has the ".jar" lines and
        //the fabricmod is get all has the "Mod" lines.
        var modLines = new List<string>();
        foreach (var detail in details.Split(Environment.NewLine))
            if (detail.Contains(".jar", StringComparison.CurrentCultureIgnoreCase) || (isFabricMod && detail.StartsWith("\t" + "\t") && !FabricModIdentifier().IsMatch(detail)))
                modLines.Add(detail);

        var hintLines = new List<string>();
        foreach (var log in realLogs)
            foreach (var modLine in modLines)
            {
                var realMod = modLine.ToLower().Replace("_", "");
                if (!realMod.Contains(modLine.ToLower().Replace("_", "")))
                    continue;

                if (realMod.Contains("minecraft.jar") || realMod.Contains(" forge-"))
                    continue;

                hintLines.Add(modLine.Trim("\r\n".ToCharArray()));
                break;
            }

        hintLines = hintLines.Distinct().ToList();

        //cnm regex
        foreach (var line in hintLines)
        {
            var modId = isFabricMod
                 ? ExtractFabricModIdentifier().Match(line).Value
                 : ExtractOtherModIdentifier().Match(line).Value;

            if (!string.IsNullOrWhiteSpace(modId))
                yield return modId;
        }
    }

    #endregion Privates
}