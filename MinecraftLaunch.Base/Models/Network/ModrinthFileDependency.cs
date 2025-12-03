
using MinecraftLaunch.Base.Enums;

namespace MinecraftLaunch.Base.Models.Network;

public record ModrinthFileDependency
{
    public required DependencyType DependencyType { get; init; }
    public string VersionId { get; init; }
    public string ProjectId { get; init; }
    public string FileName { get; init; }
}
