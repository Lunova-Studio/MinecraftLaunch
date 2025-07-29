using MinecraftLaunch.Base.Models.Network;

namespace MinecraftLaunch.Base.Interfaces;

public interface IDownloader {
    Task DownloadAsync(DownloadRequest request, CancellationToken cancellationToken);
    Task DownloadManyAsync(IEnumerable<DownloadRequest> requests, CancellationToken cancellationToken);
}

public sealed class DownloadProgressEventArgs : System.EventArgs {
    public long Progress { get; set; }
}