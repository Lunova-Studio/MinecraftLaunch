using MinecraftLaunch.Base.Models.Network;

namespace MinecraftLaunch.Base.Interfaces;

public interface IDownloader
{
    Task<DownloadResult> DownloadAsync(DownloadRequest request, CancellationToken cancellationToken);

    Task<GroupDownloadResult> DownloadManyAsync(GroupDownloadRequest requests, CancellationToken cancellationToken);
}