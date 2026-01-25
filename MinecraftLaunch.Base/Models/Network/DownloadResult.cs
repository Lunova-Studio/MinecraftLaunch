using MinecraftLaunch.Base.Enums;

namespace MinecraftLaunch.Base.Models.Network;

public record DownloadResult
{
    public Exception Exception { get; init; }
    public DownloadResultType Type { get; init; }

    public DownloadResult(DownloadResultType type)
    {
        Type = type;
    }
}

public record GroupDownloadResult
{
    public required DownloadResultType Type { get; init; }
    public required IEnumerable<DownloadRequest> Failed { get; init; }
}