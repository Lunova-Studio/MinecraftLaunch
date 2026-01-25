namespace MinecraftLaunch.Base.Interfaces;

public interface IDownloadMirror
{
    public string TryFindUrl(string sourceUrl);
}