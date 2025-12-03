using MinecraftLaunch.Base.Enums;

namespace MinecraftLaunch.Base.Models.Network;

public record CurseForgeFileDependency
{
    public required int ModId { get; init; }
    public required FileRelationType RelationType { get; init; }
}
