using MinecraftLaunch.Base.Enums;

namespace MinecraftLaunch.Base.Interfaces;

public interface IResourceFile
{
    public string DisplayName { get; init; }
    public string FileName { get; init; }
    public string DownloadUrl { get; init; }
    public long DownloadCount { get; init; }
    public DateTime Published { get; init; }
    public FileReleaseType ReleaseType { get; init; }
    public IEnumerable<string> GameVersions { get; init; }
    public IEnumerable<ModLoaderType> Loaders { get; init; }
    public long FileSize { get; init; }
}