using MinecraftLaunch.Base.Enums;
using MinecraftLaunch.Base.Interfaces;
using MinecraftLaunch.Base.Models.SHA1;

namespace MinecraftLaunch.Base.Models.Network;

public record ModrinthResource : IResource {
    public string Slug { get; init; }
    public string Name { get; init; }
    public string Author { get; set; }
    public string Summary { get; init; }
    public string IconUrl { get; init; }
    public string ProjectId { get; init; }
    public string ProjectType { get; init; }

    public int DownloadCount { get; init; }

    public DateTime Updated { get; init; }
    public DateTime DateModified { get; init; }
    public IEnumerable<string> Categories { get; init; }
    public IEnumerable<string> Screenshots { get; init; }
    public IEnumerable<string> MinecraftVersions { get; init; }

    public string WebLink => $"https://modrinth.com/{ProjectType}/{Slug}";
}

public record ModrinthResourceFile {
    public string ChangeLog { get; init; }
    public string DisplayName { get; init; }
    public string VersionNumber { get; init; }

    public FileReleaseType ReleaseType { get; init; }

    public required Sha1Data Sha1 { get; init; }
    public required string Sha512 { get; init; }
    public required string FileName { get; init; }
    public required string DownloadUrl { get; init; }

    public required string AuthorId { get; init; }
    public required string ProjectId { get; init; }
    public required string VersionId { get; init; }

    public required DateTime Published { get; init; }

    public required bool IsPrimary { get; init; }

    public required long FileSize { get; init; }
    public required long DownloadCount { get; init; }

    public IEnumerable<string> MinecraftVersions { get; init; }
    public IEnumerable<ModLoaderType> ModLoaders { get; init; }
    public IEnumerable<ModrinthFileDependency> Dependencies { get; init; }
}