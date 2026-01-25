using MinecraftLaunch.Base.Enums;

namespace MinecraftLaunch.Base.Interfaces;

public interface IResource
{
    public string Name { get; init; }
    public string Summary { get; init; }
    public string IconUrl { get; init; }
    public string WebsiteUrl { get; init; }
    public int DownloadCount { get; init; }
    public DateTime DateModified { get; init; }
    public IEnumerable<string> Categories { get; init; }
    public IEnumerable<string> Screenshots { get; init; }
    public IEnumerable<string> MinecraftVersions { get; init; }
    public IEnumerable<ModLoaderType> Loaders { get; init; }
    public ResourceType ResourceType { get; }
}