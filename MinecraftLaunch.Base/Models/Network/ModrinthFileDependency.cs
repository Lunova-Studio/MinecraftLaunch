using MinecraftLaunch.Base.Enums;

namespace MinecraftLaunch.Base.Models.Network;

public record ModrinthFileDependency
{
    public string FileName { get; init; }
    public string VersionId { get; init; }
    public string ProjectId { get; init; }

    public required DependencyType Type { get; init; }
}