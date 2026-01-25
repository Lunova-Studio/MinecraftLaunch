using Flurl.Http;
using MinecraftLaunch.Base.Enums;
using MinecraftLaunch.Base.EventArgs;
using MinecraftLaunch.Base.Interfaces;
using MinecraftLaunch.Base.Models.Network;
using MinecraftLaunch.Utilities;
using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;

namespace MinecraftLaunch.Components.Downloader;

public class DefaultDownloader : IDownloader
{
    private readonly SemaphoreSlim _globalSemaphore;

    private const int BufferSize = 4096;
    private const long SegmentThreshold = 1048576;
    private const double kilobyte = 1024.0;
    private const double megabyte = kilobyte * 1024.0;
    private const double gigabyte = megabyte * 1024.0;

    public DefaultDownloader()
    {
        _globalSemaphore = new SemaphoreSlim(DownloadManager.MaxThread, DownloadManager.MaxThread);
    }

    public static string FormatSize(double bytes, bool includePerSecond = false)
    {
        string suffix;
        if (bytes < kilobyte)
            suffix = "B";
        else if (bytes < megabyte)
        {
            bytes /= kilobyte;
            suffix = "KB";
        }
        else if (bytes < gigabyte)
        {
            bytes /= megabyte;
            suffix = "MB";
        }
        else
        {
            bytes /= gigabyte;
            suffix = "GB";
        }

        string format = bytes < 100 ? "0.00" : "0.0";
        string result = bytes.ToString(format) + " " + suffix;
        return includePerSecond ? result + "/s" : result;
    }

    public async Task<DownloadResult> DownloadAsync(DownloadRequest request, CancellationToken cancellationToken = default)
    {
        await _globalSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            for (int attempt = 0; attempt < DownloadManager.MaxRetryCount; attempt++)
            {
                try
                {
                    await DownloadFileDriverAsync(request, cancellationToken);
                    request.Completed?.Invoke(EventArgs.Empty);
                    return new DownloadResult(DownloadResultType.Successful);
                }
                catch (OperationCanceledException)
                {
                    return new DownloadResult(DownloadResultType.Cancelled);
                }
                catch (Exception ex)
                {
                    if (attempt == DownloadManager.MaxRetryCount - 1)
                        return new DownloadResult(DownloadResultType.Failed) { Exception = ex };

                    await Task.Delay(1000 * (attempt + 1), cancellationToken).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            _globalSemaphore.Release();
        }

        return new DownloadResult(DownloadResultType.Failed);
    }

    public async Task<GroupDownloadResult> DownloadManyAsync(GroupDownloadRequest requests, CancellationToken cancellationToken = default)
    {
        var downloadStates = new GroupDownloadStates
        {
            DownloadedCount = 0,
            TotalCount = requests.Files.Count(),
            TotalBytes = requests.Files.Sum(x => x.Size)
        };

        List<(DownloadRequest, DownloadResult)> failed = [];
        List<Task> downloadTasks = [];

        _ = ReportGroupProgressAsync(downloadStates, requests, cancellationToken);
        foreach (var req in requests.Files)
            downloadTasks.Add(DownloadInGroupAsync(downloadStates, req, cancellationToken));

        await Task.WhenAll(downloadTasks);

        var type = DownloadResultType.Successful;

        requests.ProgressChanged?.Invoke(new ResourceDownloadProgressChangedEventArgs
        {
            Speed = 0,
            TotalBytes = downloadStates.TotalBytes,
            EstimatedRemaining = TimeSpan.Zero,
            DownloadedBytes = downloadStates.TotalBytes,
            TotalCount = downloadStates.TotalCount,
            CompletedCount = downloadStates.TotalCount
        });

        return new GroupDownloadResult
        {
            Failed = [],
            Type = type
        };
    }

    private async Task DownloadInGroupAsync(GroupDownloadStates downloadStates, DownloadRequest request, CancellationToken cancellationToken = default)
    {
        await _globalSemaphore.WaitAsync(cancellationToken);

        try
        {
            if (!request.FileInfo.Directory.Exists)
                request.FileInfo.Directory.Create();

            for (int attempt = 0; attempt < DownloadManager.MaxRetryCount; attempt++)
            {
                try
                {
                    string url = request.Url;
                    var (response, finalUrl) = await PrepareForDownloadAsync(url, cancellationToken).ConfigureAwait(false);

                    var states = new DownloadStates
                    {
                        Url = finalUrl,
                        FragmentSize = SegmentThreshold,
                        Stopwatch = Stopwatch.StartNew(),
                        LocalPath = request.FileInfo.FullName,
                    };

                    downloadStates.States.Add(states);
                    if (response.Content.Headers.ContentLength is long contentLength)
                        states.TotalBytes = contentLength;
                    else
                        states.TotalBytes = request.Size;

                    if (DownloadManager.IsEnableFragment)
                    {
                        bool supportsRange = await ValidateRangeSupport(finalUrl, cancellationToken).ConfigureAwait(false);
                        if (supportsRange)
                        {
                            await DownloadMultiPartAsync(states, request, cancellationToken).ConfigureAwait(false);
                            Interlocked.Increment(ref downloadStates.DownloadedCount);
                            return;
                        }
                    }

                    await DownloadSinglePartAsync(states, request, cancellationToken).ConfigureAwait(false);
                    Interlocked.Increment(ref downloadStates.DownloadedCount);
                    request.Completed?.Invoke(EventArgs.Empty);
                    break;
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception)
                {
                    await Task.Delay(1000 * (attempt + 1), cancellationToken).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            _globalSemaphore.Release();
        }
    }

    private static async Task<bool> ValidateRangeSupport(string url, CancellationToken cancellationToken)
    {
        using var rangeRequest = new HttpRequestMessage(HttpMethod.Get, url);
        rangeRequest.Headers.Range = new RangeHeaderValue(0, 0);
        var response = await HttpUtil.DownloaderClient.SendAsync(rangeRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        return response.StatusCode == HttpStatusCode.PartialContent;
    }

    private static async Task DownloadFileDriverAsync(DownloadRequest request, CancellationToken cancellationToken)
    {
        string url = request.Url;
        var (response, finalUrl) = await PrepareForDownloadAsync(url, cancellationToken).ConfigureAwait(false);

        var states = new DownloadStates
        {
            Url = finalUrl,
            FragmentSize = SegmentThreshold,
            Stopwatch = Stopwatch.StartNew(),
            LocalPath = request.FileInfo.FullName
        };

        if (!request.FileInfo.Directory.Exists)
            request.FileInfo.Directory.Create();

        if (response.Content.Headers.ContentLength is long contentLength)
            states.TotalBytes = contentLength;
        else
            states.TotalBytes = request.Size;

        if (DownloadManager.IsEnableFragment)
        {
            bool supportsRange = await ValidateRangeSupport(finalUrl, cancellationToken);
            if (supportsRange)
            {
                var progressTask = ReportProgressAsync(states, request, cancellationToken);
                var downloadTask = DownloadMultiPartAsync(states, request, cancellationToken);
                await Task.WhenAll(progressTask, downloadTask).ConfigureAwait(false);
                return;
            }
        }

        await DownloadSinglePartAsync(states, request, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<(HttpResponseMessage, string)> PrepareForDownloadAsync(string url, CancellationToken cancellationToken)
    {
        var response = await HttpUtil.FlurlClient.Request(url)
            .AllowAnyHttpStatus()
            .HeadAsync(HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode == 302) // 即 302
            return await PrepareForDownloadAsync(response.ResponseMessage.Headers.Location.AbsoluteUri, cancellationToken)
                .ConfigureAwait(false);

        response.ResponseMessage.EnsureSuccessStatusCode();
        return (response.ResponseMessage, url);
    }

    private static async Task DownloadMultiPartAsync(DownloadStates states, DownloadRequest request, CancellationToken cancellationToken)
    {
        long fileSize = states.TotalBytes;
        long totalSegments = (fileSize + SegmentThreshold - 1) / SegmentThreshold;
        states.TotalFragments = totalSegments;

        await using var fileStream = new FileStream(states.LocalPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite, BufferSize, true);
        fileStream.SetLength(fileSize);

        var tasks = new List<Task>();
        int workers = Math.Min(DownloadManager.MaxThread, (int)totalSegments);
        for (int i = 0; i < workers; i++)
            tasks.Add(MultipartDownloadWorker(states, request, cancellationToken));

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private static async Task DownloadSinglePartAsync(DownloadStates states, DownloadRequest request, CancellationToken cancellationToken)
    {
        using var response = await HttpUtil.DownloaderClient.GetAsync(states.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var fileStream = new FileStream(states.LocalPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite, BufferSize, true);

        if (states.TotalBytes is long size)
            fileStream.SetLength(size);

        byte[] buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            await WriteStreamToFile(contentStream, fileStream, buffer, request, states, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        request.ProgressChanged?.Invoke(new ResourceDownloadProgressChangedEventArgs
        {
            Speed = 0,
            EstimatedRemaining = TimeSpan.Zero,
            TotalBytes = states.TotalBytes,
            DownloadedBytes = states.TotalBytes,
            TotalCount = 1,
            CompletedCount = 1
        });
    }

    private static async Task MultipartDownloadWorker(DownloadStates states, DownloadRequest request, CancellationToken cancellationToken)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(BufferSize);

        try
        {
            while (states.NextFragment() is (long start, long end))
            {
                using var httpRequest = new HttpRequestMessage(HttpMethod.Get, states.Url);
                httpRequest.Headers.Range = new RangeHeaderValue(start, end);
                var response = await HttpUtil.FlurlClient.HttpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                await using var fileStream = new FileStream(states.LocalPath, FileMode.Open, FileAccess.Write, FileShare.Write);
                fileStream.Seek(start, SeekOrigin.Begin);
                await WriteStreamToFile(contentStream, fileStream, buffer, request, states, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static async Task ReportProgressAsync(DownloadStates states, DownloadRequest request, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        long prevBytes = 0;
        double prevTime = 0;

        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(1000));
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            long nowBytes = Interlocked.Read(ref states.DownloadedBytes);
            long totalBytes = Interlocked.Read(ref states.TotalBytes);

            if (totalBytes > 0 && nowBytes >= totalBytes)
                break;

            double nowTime = sw.Elapsed.TotalSeconds;
            double deltaB = nowBytes - prevBytes;
            double deltaT = nowTime - prevTime;
            long speed = deltaT > 0 ? (long)(deltaB / deltaT) : 0;

            TimeSpan eta = speed > 0
                ? TimeSpan.FromSeconds((totalBytes - nowBytes) / (double)speed)
                : TimeSpan.Zero;

            prevTime = nowTime;
            prevBytes = nowBytes;

            request.ProgressChanged?.Invoke(new ResourceDownloadProgressChangedEventArgs
            {
                Speed = speed,
                EstimatedRemaining = eta,
                TotalBytes = totalBytes,
                DownloadedBytes = nowBytes,
                TotalCount = 1,
                CompletedCount = 0
            });
        }

        request.ProgressChanged?.Invoke(new ResourceDownloadProgressChangedEventArgs
        {
            Speed = 0,
            TotalBytes = states.TotalBytes,
            EstimatedRemaining = TimeSpan.Zero,
            DownloadedBytes = states.DownloadedBytes,
            TotalCount = 1,
            CompletedCount = 1
        });
    }

    private static async Task ReportGroupProgressAsync(GroupDownloadStates states, GroupDownloadRequest request, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        long prevBytes = 0;
        double prevTime = 0;

        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(1000));
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            Interlocked.Exchange(ref states.TotalBytes, states.States.Sum(x => x.TotalBytes));
            Interlocked.Exchange(ref states.DownloadedBytes, states.States.Sum(x => x.DownloadedBytes));

            var totalBytes = Interlocked.Read(ref states.TotalBytes);
            var nowBytes = Interlocked.Read(ref states.DownloadedBytes);

            if (states.TotalCount is 0)
                break;

            if (states.TotalBytes is not 0 && states.TotalCount == states.DownloadedCount)
                break;

            if (totalBytes > 0 && nowBytes >= totalBytes)
                break;

            double nowTime = sw.Elapsed.TotalSeconds;
            double deltaB = nowBytes - prevBytes;
            double deltaT = nowTime - prevTime;
            long speed = deltaT > 0 ? (long)(deltaB / deltaT) : 0;

            TimeSpan eta = speed > 0
                ? TimeSpan.FromSeconds((totalBytes - nowBytes) / (double)speed)
                : TimeSpan.Zero;

            prevTime = nowTime;
            prevBytes = nowBytes;

            request.ProgressChanged?.Invoke(new ResourceDownloadProgressChangedEventArgs
            {
                Speed = speed,
                EstimatedRemaining = eta,
                TotalBytes = totalBytes,
                DownloadedBytes = nowBytes,
                TotalCount = states.TotalCount,
                CompletedCount = states.DownloadedCount
            });
        }
    }

    private static async Task WriteStreamToFile(Stream contentStream, FileStream fileStream, byte[] buffer, DownloadRequest request, DownloadStates states, CancellationToken cancellationToken)
    {
        int bytesRead;
        while ((bytesRead = await contentStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
            Interlocked.Add(ref states.DownloadedBytes, bytesRead);
        }
    }

    private class DownloadStates
    {
        private long _fragmentScheduled = -1;

        public long TotalBytes;
        public long DownloadedBytes;

        public long FragmentSize;
        public long TotalFragments;

        public string Url;
        public string LocalPath;

        public Stopwatch Stopwatch = new();

        public (long start, long end)? NextFragment()
        {
            long index = Interlocked.Increment(ref _fragmentScheduled);
            if (index >= TotalFragments)
                return null;

            long start = index * FragmentSize;
            long end = Math.Min(start + FragmentSize, TotalBytes) - 1;
            return (start, end);
        }
    }

    private class GroupDownloadStates
    {
        public long TotalBytes;
        public long DownloadedBytes;

        public int TotalCount;
        public int DownloadedCount;

        public ConcurrentBag<DownloadStates> States = [];
        public ConcurrentBag<DownloadRequest> FailedRequests = [];
    }
}