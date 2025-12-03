using MinecraftLaunch.Base.Enums;
using MinecraftLaunch.Base.Interfaces;

namespace MinecraftLaunch.Base.Models.Network;

public record ModrinthResource : IResource {
    public string Id { get; init; }
    public string Slug { get; init; }
    public string Name { get; init; }
    public string Author { get; set; }
    public string Summary { get; init; }
    public string IconUrl { get; init; }
    public string ProjectType { get; init; }

    public int DownloadCount { get; init; }

    public DateTime Updated { get; init; }
    public DateTime DateModified { get; init; }
    public IEnumerable<string> Categories { get; init; }
    public IEnumerable<string> Screenshots { get; init; }
    public IEnumerable<string> MinecraftVersions { get; init; }

    public string WebLink => $"https://modrinth.com/{ProjectType}/{Slug}";
}

public record ModrinthResourceFiles {
    public required string Id { get; init; }
    public required string ProjectId { get; init; }
    public required string AuthorId { get; init; }
    public required DateTime DatePublished { get; init; }
    public required int Downloads { get; init; }
    public required IEnumerable<ModrinthResourceFile> Files { get; init; }
    public string ChangelogUrl { get; init; }
    public string Name { get; init; }
    public string VersionNumber { get; init; }
    public string Changelog { get; init; }
    public IEnumerable<ModrinthFileDependency> Dependencies { get; init; }
    public IEnumerable<string> GameVersions { get; init; }
    public FileReleaseType VersionType { get; init; }
    public IEnumerable<string> Loaders { get; init; }
    public bool Featured { get; init; }
    public ModrinthFileStatus Status { get; init; }
    public RequestedStatus? RequestedStatus { get; init; }
}

public record ModrinthResourceFile {
    public required FileHashes Hashes { get; init; }
    public required string Url { get; init; }
    public required string FileName { get; init; }
    public required bool Primary { get; init; }
    public int Size { get; init; }
    public AdditionalFileType? FileType { get; init; }
}