namespace MinecraftLaunch.Base.Interfaces;

public interface IDownloadDependency
{
    string Url { get; }
    string FullPath { get; }
    long? Size { get; }
}