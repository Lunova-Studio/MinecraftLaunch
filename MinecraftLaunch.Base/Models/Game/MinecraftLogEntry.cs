using MinecraftLaunch.Base.Enums;

namespace MinecraftLaunch.Base.Models.Game;

public readonly record struct MinecraftLogEntry
{
    public string Log { get; init; }
    public string Time { get; init; }
    public string Source { get; init; }
    public string SourceText { get; init; }
    public MinecraftLogLevel LogLevel { get; init; }

    public override string ToString()
    {
        return SourceText;
    }
}