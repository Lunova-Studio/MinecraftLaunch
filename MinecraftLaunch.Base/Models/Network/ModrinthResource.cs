namespace MinecraftLaunch.Base.Models.Network;

public record ModrinthResource {
    public string Id { get; set; }
    public string Slug { get; set; }
    public string Name { get; set; }
    public string Author { get; set; }
    public string Summary { get; set; }
    public string IconUrl { get; set; }
    public string ProjectType { get; set; }

    public int DownloadCount { get; set; }

    public DateTime Updated { get; set; }
    public DateTime Published { get; set; }
    public IEnumerable<string> Categories { get; set; }
    public IEnumerable<string> ScreenshotUrls { get; set; }

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