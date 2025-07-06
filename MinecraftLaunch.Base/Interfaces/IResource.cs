namespace MinecraftLaunch.Base.Interfaces;

public interface IResource {
    string Name { get; init; }
    string Summary { get; init; }
    string IconUrl { get; init; }
    int DownloadCount { get; init; }
    DateTime DateModified { get; init; }
    IEnumerable<string> Categories { get; init; }
    IEnumerable<string> Screenshots { get; init; }
}