namespace MinecraftLaunch.Base.EventArgs;

public sealed class ResourceDownloadProgressChangedEventArgs : System.EventArgs
{
    public double Speed { get; set; }
    public int TotalCount { get; set; }
    public int CompletedCount { get; set; }
    public long DownloadedBytes { get; set; }
    public long TotalBytes { get; set; }
    public TimeSpan EstimatedRemaining { get; set; }

    public double Percentage => TotalBytes > 0
        ? DownloadedBytes * 100d / TotalBytes
        : 0;
}