using MinecraftLaunch.Base.Enums;
using MinecraftLaunch.Base.Models.Game;

namespace MinecraftLaunch.Base.Models.Logging;

public record LogAnalyzerResult
{
    public MinecraftEntry Minecraft { get; init; }
    public IReadOnlyCollection<string> SuspiciousMods { get; init; }
    public IReadOnlyCollection<CrashReasons> CrashReasons { get; init; }
}