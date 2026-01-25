using MinecraftLaunch.Base.Enums;
using MinecraftLaunch.Base.Interfaces;

namespace MinecraftLaunch.Base.Models.Network;

public record ModrinthSearchResult : ISearchResult
{
    public int Index { get; init; }
    public int PageSize { get; init; }
    public long TotalCount { get; init; }

    public IEnumerable<ModrinthResource> Resources { get; init; }
}

public record ModrinthResource : IResource
{
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
    public IEnumerable<ModLoaderType> Loaders { get; init; }
    public string WebsiteUrl { get; init; }
    public ResourceType ResourceType => ProjectType switch
    {
        "mod" => ResourceType.Mod,
        "modpack" => ResourceType.Modpack,
        "resourcepack" => ResourceType.Resourcepack,
        "shader" => ResourceType.Shaderpack,
        _ => ResourceType.Mod
    };
}

public record ModrinthResourceFile : IResourceFile
{
    public string ChangeLog { get; init; }
    public string DisplayName { get; init; }
    public string VersionNumber { get; init; }

    public FileReleaseType ReleaseType { get; init; }

    public required string Sha1 { get; init; }
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

    public IEnumerable<string> GameVersions { get; init; }
    public IEnumerable<ModLoaderType> Loaders { get; init; }
    public IEnumerable<ModrinthFileDependency> Dependencies { get; init; }
}