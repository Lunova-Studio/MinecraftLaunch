using MinecraftLaunch.Base.EventArgs;

namespace MinecraftLaunch.Base.Models.Network;

public record DownloadRequest {
    public string Url { get; set; }
    public FileInfo FileInfo { get; set; }
    public long Size { get; set; } = -1;

    public Action<System.EventArgs> Completed { get; set; }
    public Action<ResourceDownloadProgressChangedEventArgs> ProgressChanged { get; set; }

    public DownloadRequest() { }
    public DownloadRequest(string url, string localPath) {
        Url = url;
        FileInfo = new(localPath);
    }
}

public record GroupDownloadRequest {
    public DateTime StartTime { get; init; }

    public IEnumerable<DownloadRequest> Files { get; set; }
    public Action<System.EventArgs> Completed { get; set; }
    public Action<ResourceDownloadProgressChangedEventArgs> ProgressChanged { get; set; }

    //public Action<double> DownloadSpeedChanged { get; set; }
    //public Action<DownloadRequest, DownloadResult> SingleRequestCompleted { get; set; }

    public GroupDownloadRequest(IEnumerable<DownloadRequest> files) {
        Files = files;
        StartTime = DateTime.Now;
    }
}