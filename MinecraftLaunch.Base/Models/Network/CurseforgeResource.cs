using MinecraftLaunch.Base.Enums;
using MinecraftLaunch.Base.Interfaces;

namespace MinecraftLaunch.Base.Models.Network;

public record CurseForgeSearchResult : ISearchResult
{
    public int Index { get; init; }
    public int PageSize { get; init; }
    public long TotalCount { get; init; }

    public IEnumerable<CurseforgeResource> Resources { get; init; }
}

public record CurseforgeResource : IResource
{
    public required int Id { get; init; }
    public required int ClassId { get; init; }
    public required int DownloadCount { get; init; }
    public required string Name { get; init; }
    public required string Slug { get; init; }
    public required string IconUrl { get; init; }
    public required string Summary { get; init; }
    public required string WebsiteUrl { get; init; }
    public required DateTime DateModified { get; init; }
    public required IEnumerable<string> Authors { get; init; }
    public required IEnumerable<string> MinecraftVersions { get; init; }
    public required IEnumerable<string> Categories { get; init; }
    public required IEnumerable<string> Screenshots { get; init; }
    public IEnumerable<CurseforgeResourceFile> LatestFiles { get; init; }
    public IEnumerable<ModLoaderType> Loaders { get; init; }
    public ResourceType ResourceType => (ClassId)ClassId switch
    {
        Enums.ClassId.Mods => ResourceType.Mod,
        Enums.ClassId.Modpacks => ResourceType.Modpack,
        Enums.ClassId.ResourcePacks => ResourceType.Resourcepack,
        Enums.ClassId.Shaders => ResourceType.Shaderpack,
        Enums.ClassId.DataPacks => ResourceType.Datapack,
        Enums.ClassId.Worlds => ResourceType.World,
        _ => ResourceType.Mod
    };
}

public record CurseforgeResourceFile : IResourceFile
{
    public required int Id { get; init; }
    public required int ModId { get; init; }
    public required int GameId { get; init; }
    public required int AlternateFileId { get; init; }

    public required uint FileFingerprint { get; init; }

    public required bool IsApproved { get; init; }
    public required bool IsAvailable { get; init; }
    public required bool IsServerPack { get; init; }

    public string Sha1 { get; init; }
    public required string FileName { get; init; }
    public required string DisplayName { get; init; }
    public required string DownloadUrl { get; init; }

    public required long FileSize { get; init; }
    public required long DownloadCount { get; init; }

    public required DateTime Published { get; init; }
    public required FileReleaseType ReleaseType { get; init; }

    public required IEnumerable<string> GameVersions { get; init; }
    public required IDictionary<int, DependencyType> Dependencies { get; init; }
    public IEnumerable<ModLoaderType> Loaders { get; init; }
}