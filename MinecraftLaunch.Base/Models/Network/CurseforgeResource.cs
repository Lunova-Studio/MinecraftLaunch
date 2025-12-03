using MinecraftLaunch.Base.Enums;
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
    public required IEnumerable<string> MinecraftVersions { get; init; }
    public required IEnumerable<string> Categories { get; init; }
    public required IEnumerable<string> Screenshots { get; init; }
    public IEnumerable<CurseforgeResourceFile> LatestFiles { get; init; }
}

public record CurseforgeResourceFile {
    public required int Id { get; init; }
    public required int GameId { get; init; }
    public required int ModId { get; init; }
    public bool IsAvailable { get; init; }
    public required string DisplayName { get; init; }
    public required string FileName { get; init; }
    public required FileReleaseType ReleaseType { get; init; }
    public required CurseForgeFileStatus FileStatus { get; init; }
    public required IEnumerable<FileHash> Hashes { get; init; }
    public required DateTime FileDate { get; init; }
    public required long FileLength { get; init; }
    public required long DownloadCount { get; init; }
    public required long? FileSizeOnDisk { get; init; }
    public required string DownloadUrl { get; init; }
    public required IEnumerable<string> GameVersions { get; init; }
    public required IEnumerable<SortableGameVersion> SortableGameVersions { get; init; }
    public required IEnumerable<CurseForgeFileDependency> Dependencies { get; init; }
    public required bool? ExposeAsAlternative { get; init; }
    public required int? ParentProjectFileId { get; init; }
    public required int? AlternateFileId { get; init; }
    public required bool? IsServerPack { get; init; }
    public required int? ServerPackFileId { get; init; }
    public required bool? IsEarlyAccessContent { get; init; }
    public required DateTime? EarlyAccessEndDate { get; init; }
    public required long FileFingerprint { get; init; }
    public required IEnumerable<FileModule> Modules { get; init; }
    
}