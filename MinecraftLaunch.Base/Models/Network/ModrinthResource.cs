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