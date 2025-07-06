using MinecraftLaunch.Base.Interfaces;

namespace MinecraftLaunch.Base.Models.Network;

public record CurseforgeResource : IResource {
    public required int Id { get; init; }
    public required int ClassId { get; init; }
    public required int DownloadCount { get; init; }
    public required string Name { get; init; }
    public required string IconUrl { get; init; }
    public required string Summary { get; init; }
    public required string WebsiteUrl { get; init; }
    public required DateTime DateModified { get; init; }
    public required IEnumerable<string> Authors { get; init; }
    public required IEnumerable<string> Categories { get; init; }
    public required IEnumerable<string> Screenshots { get; init; }
    public IEnumerable<CurseforgeResourceFile> LatestFiles { get; init; }
}

public record CurseforgeResourceFile {
    public required int Id { get; init; }
    public required int ModId { get; init; }
    public required int ReleaseType { get; init; }
    public required uint FileFingerprint { get; init; }
    public required bool IsAvailable { get; init; }
    public required string FileName { get; init; }
    public required string DisplayName { get; init; }
    public required string DownloadUrl { get; init; }
    public required DateTime Published { get; init; }
    public required IEnumerable<string> MinecraftVersions { get; init; }

    public bool IsReleased => ReleaseType is 1;
}