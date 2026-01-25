using MinecraftLaunch.Base.Enums;

namespace MinecraftLaunch.Base.Models.Network;

public record CurseforgeCategoryEntry
{
    public int Id { get; init; }
    public string Name { get; init; }
    public ClassId ClassId { get; init; }
}