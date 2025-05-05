using Flurl;
using Flurl.Http;
using MinecraftLaunch.Base.Enums;
using MinecraftLaunch.Base.Models.Network;
using MinecraftLaunch.Utilities;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Frozen;
using Timer = System.Timers.Timer;

namespace MinecraftLaunch.Components.Downloader;


/// <summary>
/// 文件下载器，支持单文件和批量文件下载，支持多线程分块下载。
/// </summary>
public sealed class FileDownloader
{
    private class DownloadStates
    {
        public required string Url { get; init; }
        public required string LocalPath { get; init; }
        public long? TotalBytes { get; set; }

        public required long ChunkSize { get; init; }

        public long _chunkScheduled = 0;
        public long TotalChunks { get; set; } = 0;
        private readonly object _chunkOrganizerLock = new();

        /// <summary>
        /// 获取下一个需要下载的分块范围。
        /// (english) Get the next chunk range to download.
        /// </summary>
        public (long start, long end)? NextChunk()
        {
            long totalBytes = (long)TotalBytes!;
            long start, end;
            lock (_chunkOrganizerLock)
            {
                if (_chunkScheduled == TotalChunks)
                    return null;
                start = _chunkScheduled * ChunkSize;
                _chunkScheduled++;
            }
            // 处理最后一个分块
            end = Math.Min(start + ChunkSize, totalBytes) - 1;
            return (start, end);
        }
    }

    private record class DownloaderConfig(
        long ChunkSize,
        int WorkersPerDownloadTask,
        int ConcurrentDownloadTasks);

    public long ChunkSize => _config.ChunkSize;
    public int WorkersPerDownloadTask => _config.WorkersPerDownloadTask;
    public int ConcurrentDownloadTasks => _config.ConcurrentDownloadTasks;

    internal IFlurlClient FlurlClient => HttpUtil.FlurlClient;

    private const int DEFAULT_DOWNLOAD_BUFFER_SIZE = 8192; // 增大缓冲区大小
    private readonly DownloaderConfig _config;
    private readonly SemaphoreSlim _globalDownloadTasksSemaphore;

    /// <summary>
    /// 构造函数，初始化文件下载器。
    /// Initialize file downloader.
    /// </summary>
    /// <param name="maxThread">最大并发线程数。</param>
    public FileDownloader(int maxThread = 64)
    {
        _config = new DownloaderConfig(1024 * 1024, maxThread, maxThread);
        _globalDownloadTasksSemaphore = new SemaphoreSlim(maxThread, maxThread);
    }

    /// <summary>
    /// 将下载进度格式化为可读字符串。
    /// Format download speed to a readable string.
    /// </summary>
    public static string GetSpeedText(double speed)
    {
        const double kilobyte = 1024.0;
        const double megabyte = kilobyte * 1024.0;
        const double gigabyte = megabyte * 1024.0;

        if (speed < kilobyte)
        {
            return speed.ToString("0") + " B/s"; // 字节
        }
        else if (speed < megabyte)
        {
            return (speed / kilobyte).ToString("0.0") + " KB/s"; // 千字节
        }
        else if (speed < gigabyte)
        {
            return (speed / megabyte).ToString("0.00") + " MB/s"; // 兆字节
        }
        else
        {
            return (speed / gigabyte).ToString("0.00") + " GB/s"; // 千兆字节
        }
    }

    /// <summary>
    /// 异步下载单个文件。
    /// Async download single file.
    /// </summary>
    public async Task<DownloadResult> DownloadFileAsync(DownloadRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _globalDownloadTasksSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return new DownloadResult(DownloadResultType.Cancelled);
        }

        try
        {
            await DownloadFileDriverAsync(request, cancellationToken).ConfigureAwait(false);
            return new DownloadResult(DownloadResultType.Successful);
        }
        catch (TaskCanceledException)
        {
            return new DownloadResult(DownloadResultType.Cancelled);
        }
        catch (Exception e)
        {
            return new DownloadResult(DownloadResultType.Failed)
            {
                Exception = e
            };
        }
        finally
        {
            _globalDownloadTasksSemaphore.Release();
        }
    }

    /// <summary>
    /// 异步批量下载多个文件。
    /// (english) Async download multiple files.
    /// </summary>
    public async Task<GroupDownloadResult> DownloadFilesAsync(GroupDownloadRequest request, CancellationToken cancellationToken = default)
    {
        Timer timer = new(TimeSpan.FromSeconds(1));
        List<Task> downloadTasks = new();
        ConcurrentDictionary<DownloadRequest, DownloadResult> failed = new();
        long bytesReceived = 0;
        long previousBytesReceived = 0;

        timer.Elapsed += (_, _) => {
            long diffBytes = bytesReceived - previousBytesReceived;
            previousBytesReceived = bytesReceived;

            double speed = diffBytes / 1.0;
            request.DownloadSpeedChanged?.Invoke(speed);
        };

        timer.Start();
        foreach (var req in request.Files)
        {
            var r = req;
            r.BytesDownloaded = b => {
                Interlocked.Add(ref bytesReceived, b);
            };

            string url = DownloadMirrorManager.BmclApi.TryFindUrl(req.Url);
            downloadTasks.Add(DownloadFileInGroupAsync(r, request, failed, cancellationToken));
        }

        await Task.WhenAll(downloadTasks).ConfigureAwait(false);
        timer.Stop();

        DownloadResultType type = cancellationToken.IsCancellationRequested
            ? DownloadResultType.Cancelled
            : failed.Count > 0 ? DownloadResultType.Failed : DownloadResultType.Successful;

        return new GroupDownloadResult
        {
            Failed = failed.ToFrozenDictionary(),
            Type = type
        };
    }

    #region Privates

    /// <summary>
    /// 下载文件的核心方法。
    /// (english) File downloader core driver method.
    /// </summary>
    private async Task DownloadFileDriverAsync(DownloadRequest request, CancellationToken cancellationToken = default)
    {
        string url = DownloadMirrorManager.BmclApi.TryFindUrl(request.Url);
        string localPath = request.FileInfo.FullName;

        (var flurlResponse, url) = await PrepareForDownloadAsync(url, cancellationToken).ConfigureAwait(false);
        var httpResponse = flurlResponse.ResponseMessage;

        DownloadStates states = new()
        {
            Url = url,
            LocalPath = localPath,
            ChunkSize = _config.ChunkSize
        };

        bool useMultiPart = false;
        if (httpResponse.Content.Headers.ContentLength is long contentLength)
        {
            request.Size = contentLength;
            states.TotalBytes = contentLength;

            var rangeResponse = await FlurlClient.Request(url)
                .GetAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

            useMultiPart = rangeResponse.StatusCode is 206;
        }

        string destinationDir = Path.GetDirectoryName(localPath);
        if (destinationDir is not null)
            Directory.CreateDirectory(destinationDir);

        if (useMultiPart)
        {
            await DownloadMultiPartAsync(states, request, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await DownloadSinglePartAsync(states, request, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 准备下载并检查 URL 是否重定向。
    /// (english) Ready for download and check if the URL is redirected.
    /// </summary>
    private async Task<(IFlurlResponse Response, string RedirectedUrl)> PrepareForDownloadAsync(string url, CancellationToken cancellationToken = default)
    {
        var response = await FlurlClient.Request(url)
            .HeadAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

        if (response.StatusCode is 302)
        {
            var redirectUrl = response.ResponseMessage?.Headers?.Location?.AbsoluteUri;
            if (redirectUrl is not null)
                return await PrepareForDownloadAsync(redirectUrl, cancellationToken).ConfigureAwait(false);
        }

        response.ResponseMessage.EnsureSuccessStatusCode();
        return (response, url);
    }

    /// <summary>
    /// 单线程下载文件。
    /// (english) Single-threaded download file.
    /// </summary>
    private async Task DownloadSinglePartAsync(DownloadStates states, DownloadRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await FlurlClient.Request(states.Url)
            .GetAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

        using var contentStream = await response.ResponseMessage.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var fileStream = new FileStream(states.LocalPath, FileMode.Create, FileAccess.Write);

        if (states.TotalBytes is long size)
            fileStream.SetLength(size);

        byte[] downloadBufferArr = ArrayPool<byte>.Shared.Rent(DEFAULT_DOWNLOAD_BUFFER_SIZE);
        Memory<byte> downloadBuffer = downloadBufferArr.AsMemory(0, DEFAULT_DOWNLOAD_BUFFER_SIZE);
        await WriteStreamToFile(contentStream, fileStream, downloadBuffer, request, cancellationToken).ConfigureAwait(false);
        ArrayPool<byte>.Shared.Return(downloadBufferArr);
    }

    /// <summary>
    /// 多线程下载文件。
    /// (english) Multi-threaded download file.
    /// </summary>
    private async Task DownloadMultiPartAsync(DownloadStates states, DownloadRequest request, CancellationToken cancellationToken = default)
    {
        long fileSize = (long)states.TotalBytes!;

        long totalChunks = Math.DivRem(fileSize, ChunkSize, out long remainder);
        if (remainder > 0)
            totalChunks++;
        states.TotalChunks = totalChunks;

        using var fileStream = new FileStream(states.LocalPath, FileMode.Create, FileAccess.Write, FileShare.Write);
        fileStream.SetLength(fileSize);

        int numberOfWorkers = (int)Math.Min(WorkersPerDownloadTask, totalChunks);
        Task[] workers = new Task[numberOfWorkers];
        for (int i = 0; i < numberOfWorkers; i++)
        {
            workers[i] = MultipartDownloadWorker(states, request, cancellationToken);
        }
        await Task.WhenAll(workers).ConfigureAwait(false);
    }

    /// <summary>
    /// 用于分块下载的工作线程。
    /// (english) Multi part download worker.
    /// </summary>
    private async Task MultipartDownloadWorker(DownloadStates states, DownloadRequest downloadRequest, CancellationToken cancellationToken = default)
    {
        using var fileStream = new FileStream(states.LocalPath, FileMode.Open, FileAccess.Write, FileShare.Write);

        byte[] downloadBufferArr = ArrayPool<byte>.Shared.Rent(DEFAULT_DOWNLOAD_BUFFER_SIZE);
        Memory<byte> downloadBuffer = downloadBufferArr.AsMemory(0, DEFAULT_DOWNLOAD_BUFFER_SIZE);

        while (states.NextChunk() is (long start, long end))
        {
            fileStream.Seek(start, SeekOrigin.Begin);

            var response = await FlurlClient.Request(states.Url)
                .WithHeader("Range", $"bytes={start}-{end}")
                .GetAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

            response.ResponseMessage.EnsureSuccessStatusCode();

            using var contentStream = await response.GetStreamAsync().ConfigureAwait(false);
            await WriteStreamToFile(contentStream, fileStream, downloadBuffer, downloadRequest, cancellationToken).ConfigureAwait(false);
        }

        ArrayPool<byte>.Shared.Return(downloadBufferArr);
    }

    /// <summary>
    /// 将流写入文件。
    /// (english) Write stream into file.
    /// </summary>
    private async Task WriteStreamToFile(Stream contentStream, FileStream fileStream, Memory<byte> buffer, DownloadRequest request, CancellationToken cancellationToken = default)
    {
        int bytesRead;
        while ((bytesRead = await contentStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            await fileStream.WriteAsync(buffer[0..bytesRead], cancellationToken).ConfigureAwait(false);
            request.BytesDownloaded?.Invoke(bytesRead);
        }
    }

    /// <summary>
    /// 批量下载中的单个文件下载任务。
    /// (english) 
    /// </summary>
    private async Task DownloadFileInGroupAsync(DownloadRequest request, GroupDownloadRequest groupRequest, ConcurrentDictionary<DownloadRequest, DownloadResult> failed, CancellationToken cancellationToken)
    {
        DownloadResult result = await DownloadFileAsync(request, cancellationToken).ConfigureAwait(false);
        if (result.Type == DownloadResultType.Failed)
            failed.TryAdd(request, result);

        groupRequest.SingleRequestCompleted?.Invoke(request, result);
    }

    #endregion
}