using Flurl;
using Flurl.Http;

namespace MinecraftLaunch.Utilities;

public static class HttpUtil
{
    internal static HttpClient DownloaderClient { get; } = new();
    public static IFlurlClient FlurlClient { get; internal set; }

    public static IFlurlRequest Request(Url url)
    {
        if (FlurlClient is null)
            throw new InvalidOperationException("FlurlClient is not initialized.");

        return FlurlClient.Request(url);
    }

    public static IFlurlRequest Request(string url)
    {
        if (FlurlClient is null)
            throw new InvalidOperationException("FlurlClient is not initialized.");

        return FlurlClient.Request(url);
    }

    public static IFlurlRequest Request(Url baseUrl, params string[] paths)
    {
        if (FlurlClient is null)
            throw new InvalidOperationException("FlurlClient is not initialized.");

        return FlurlClient.Request(baseUrl.AppendPathSegments(paths));
    }
}