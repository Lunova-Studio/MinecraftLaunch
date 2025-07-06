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

    public string WebLink => $"https://modrinth.com/{ProjectType}/{Slug}";
}

public record ModrinthResourceFiles {
    public string Id { get; set; }
    public string ChangeLog { get; set; }
    public string SourceHash { get; set; }

    public bool IsFeatured { get; set; }

    public int DownloadCount { get; set; }

    public DateTime Published { get; set; }
    public IEnumerable<ModrinthResourceFile> Files { get; set; }
}

public record ModrinthResourceFile {
    public string Sha1 { get; set; }
    public string Sha512 { get; set; }
    public string FileName { get; set; }
    public string DownloadUrl { get; set; }

    public bool IsPrimary { get; set; }

    public long FileSize { get; set; }
}