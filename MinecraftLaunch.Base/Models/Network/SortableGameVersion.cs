namespace MinecraftLaunch.Base.Models.Network;

public record SortableGameVersion
{
    public required string GameVersionName { get; init; }
    public required string GameVersionPadded { get; init; }
    public required string GameVersion { get; init; }
    public required DateTime GameVersionReleaseDate { get; init; }
    public required int? GameVersionTypeId { get; init; }
}
