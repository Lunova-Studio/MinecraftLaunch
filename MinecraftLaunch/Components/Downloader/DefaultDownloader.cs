using MinecraftLaunch.Base.EventArgs;
using MinecraftLaunch.Base.Interfaces;
using MinecraftLaunch.Base.Models.Network;
using System.Buffers;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Threading.Channels;

namespace MinecraftLaunch.Components.Downloader;

public sealed class DefaultDownloader : IDownloader {
    private const int BufferSize = 128 * 1024;
    private const int MaxRetryPerSegment = 3;
    private const long SegmentThreshold = 5L * 1024 * 1024;

    private static HttpClient _httpClient;

    private long _totalBytes;
    private long _downloadedBytes;
    private int _downloadedFiles;

    public event EventHandler<ResourceDownloadProgressChangedEventArgs> ProgressChanged;

    public DefaultDownloader() {
        if (_httpClient is not null)
            return;

        var handler = new SocketsHttpHandler {
            MaxConnectionsPerServer = int.MaxValue,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            SslOptions = new SslClientAuthenticationOptions {
                ApplicationProtocols = [SslApplicationProtocol.Http2]
            }
        };

        _httpClient = new HttpClient(handler) {
            DefaultRequestVersion = HttpVersion.Version20,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrHigher
        };
    }

    public async Task DownloadAsync(DownloadRequest request, CancellationToken cancellationToken = default) {
        if (request.FileInfo.Directory is { Exists: false })
            request.FileInfo.Directory?.Delete();

        using var response = await _httpClient
            .GetAsync(request.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();
        await using var src = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var dst = new FileStream(request.FileInfo.FullName, FileMode.Create,
            FileAccess.Write, FileShare.None, BufferSize, useAsync: true);

        await CopyStreamAsync(src, dst, cancellationToken).ConfigureAwait(false);
        Interlocked.Increment(ref _downloadedFiles);
    }

    public async Task DownloadManyAsync(IEnumerable<DownloadRequest> requests, CancellationToken cancellationToken = default) {
        await PreProbeSizesAsync(requests, cancellationToken).ConfigureAwait(false);

        var totalCount = requests.Count();
        var channel = Channel.CreateBounded<DownloadRequest>(new BoundedChannelOptions(totalCount) {
            FullMode = BoundedChannelFullMode.Wait,
            SingleWriter = true,
            SingleReader = false
        });

        _ = ReportProgressAsync(totalCount, cancellationToken);
        var workers = Enumerable.Range(0, DownloadManager.MaxThread).Select(_ => Task.Run(async () => {
            await foreach (var req in channel.Reader.ReadAllAsync(cancellationToken)) {
                try {
                    if (req.Size >= SegmentThreshold && await GetIsSupportsRangeAsync(req, cancellationToken).ConfigureAwait(false))
                        await DownloadWithSegmentsAsync(req, cancellationToken).ConfigureAwait(false);
                    else
                        await DownloadAsync(req, cancellationToken).ConfigureAwait(false);
                } catch (Exception) { }
            }
        }));

        foreach (var req in requests)
            await channel.Writer.WriteAsync(req, cancellationToken)
                .ConfigureAwait(false);

        channel.Writer.Complete();
        await Task.WhenAll(workers)
            .ConfigureAwait(false);

        ProgressChanged?.Invoke(this, new ResourceDownloadProgressChangedEventArgs {
            Speed = 0,
            TotalBytes = _totalBytes,
            DownloadedBytes = _totalBytes,
            TotalCount = totalCount,
            CompletedCount = totalCount,
            EstimatedRemaining = TimeSpan.Zero
        });

        _totalBytes = 0;
        _downloadedBytes = 0;
        _downloadedFiles = 0;
    }

    private async Task CopyStreamAsync(Stream src, FileStream dst, CancellationToken cancellationToken) {
        var pool = MemoryPool<byte>.Shared;
        using var owner = pool.Rent(BufferSize);
        var buffer = owner.Memory;

        while (true) {
            var read = await src.ReadAsync(buffer, cancellationToken)
                .ConfigureAwait(false);

            if (read == 0)
                break;

            await dst.WriteAsync(buffer[..read], cancellationToken)
                .ConfigureAwait(false);

            Interlocked.Add(ref _downloadedBytes, read);
        }
    }

    private async Task PreProbeSizesAsync(IEnumerable<DownloadRequest> requests, CancellationToken cancellationToken) => await Parallel.ForEachAsync(requests, new ParallelOptions {
        CancellationToken = cancellationToken
    }, async (req, token) => {
        if (req.Size >= 0) {
            Interlocked.Add(ref _totalBytes, req.Size);
            return;
        }

        try {
            using var head = new HttpRequestMessage(HttpMethod.Head, req.Url);
            using var resp = await _httpClient.SendAsync(head, token).ConfigureAwait(false);

            if (resp.Content.Headers.ContentLength is long len) {
                req.Size = len;
                Interlocked.Add(ref _totalBytes, len);
            }
        } catch { }
    }).ConfigureAwait(false);

    private async ValueTask DownloadWithSegmentsAsync(DownloadRequest req, CancellationToken ct) {
        Directory.CreateDirectory(Path.GetDirectoryName(req.FileInfo.FullName)!);
        string path = req.FileInfo.FullName;
        long total = req.Size;

        await using var fs = new FileStream(path, FileMode.Create, FileAccess.Write,
            FileShare.ReadWrite, BufferSize, useAsync: true);

        fs.SetLength(total);

        var ranges = CalculateRanges(total, DownloadManager.MaxFragmented);
        using var limiter = new SemaphoreSlim(DownloadManager.MaxFragmented);
        var tasks = ranges.Select(range =>
            DownloadSegmentWithRetryAsync(req.Url, path, range.start, range.end, limiter, ct));

        await Task.WhenAll(tasks).ConfigureAwait(false);
        Interlocked.Increment(ref _downloadedFiles);
    }

    private async ValueTask WriteSegmentAsync(string filePath, long start, Stream src, CancellationToken cancellationToken) {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(BufferSize);

        try {
            await using var dst = new FileStream(filePath, FileMode.Open, FileAccess.Write,
                FileShare.ReadWrite, BufferSize, useAsync: true);

            dst.Seek(start, SeekOrigin.Begin);

            int read;
            while ((read = await src.ReadAsync(buffer.AsMemory(0, BufferSize), cancellationToken).ConfigureAwait(false)) > 0) {
                await dst.WriteAsync(buffer.AsMemory(0, read), cancellationToken)
                    .ConfigureAwait(false);

                Interlocked.Add(ref _downloadedBytes, read);
            }
        } finally {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private async Task ReportProgressAsync(int totalCount, CancellationToken cancellationToken) {
        var sw = Stopwatch.StartNew();
        long prevBytes = 0;
        double prevTime = 0;

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false)) {
            long nowBytes = Interlocked.Read(ref _downloadedBytes);
            if (_totalBytes == nowBytes && _totalBytes != 0)
                break;

            double nowTime = sw.Elapsed.TotalSeconds;
            double deltaB = nowBytes - prevBytes;
            double deltaT = nowTime - prevTime;
            long speed = deltaT > 0 ? (long)(deltaB / deltaT) : 0;
            var eta = speed > 0
                ? TimeSpan.FromSeconds(
                    (Interlocked.Read(ref _totalBytes) - nowBytes) / (double)speed)
                : TimeSpan.Zero;

            prevTime = nowTime;
            prevBytes = nowBytes;

            ProgressChanged?.Invoke(this, new ResourceDownloadProgressChangedEventArgs {
                Speed = speed,
                EstimatedRemaining = eta,
                TotalBytes = _totalBytes,
                DownloadedBytes = nowBytes,
                TotalCount = totalCount,
                CompletedCount = _downloadedFiles
            });
        }
    }

    private async Task DownloadSegmentWithRetryAsync(string url, string filePath, long start, long end, SemaphoreSlim limiter, CancellationToken cancellationToken) {
        const int TimeoutMinutes = 10;

        for (int attempt = 1; attempt <= MaxRetryPerSegment; attempt++) {
            await limiter.WaitAsync(cancellationToken).ConfigureAwait(false);

            try {
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.Range = new RangeHeaderValue(start, end);

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(TimeSpan.FromMinutes(TimeoutMinutes));

                using var resp = await _httpClient
                    .SendAsync(req, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token)
                    .ConfigureAwait(false);

                resp.EnsureSuccessStatusCode();

                await using var src = await resp.Content
                    .ReadAsStreamAsync(cancellationToken)
                    .ConfigureAwait(false);

                await WriteSegmentAsync(filePath, start, src, cancellationToken)
                    .ConfigureAwait(false);

                return;
            } catch (OperationCanceledException) when (attempt < MaxRetryPerSegment) {
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
            } catch (HttpRequestException) when (attempt < MaxRetryPerSegment) {
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
            } finally {
                limiter.Release();
            }
        }

        throw new IOException($"Segment {start}-{end} 下载失败超过重试次数");
    }

    private static async ValueTask<bool> GetIsSupportsRangeAsync(DownloadRequest req, CancellationToken cancellationToken) {
        try {
            using var head = new HttpRequestMessage(HttpMethod.Head, req.Url);
            using var resp = await _httpClient
                .SendAsync(head, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            return resp.Headers.AcceptRanges?.Contains("bytes") is true;
        } catch (Exception) {
            return false;
        }
    }

    private static List<(long start, long end)> CalculateRanges(long totalSize, int maxSegments) {
        if (totalSize <= 0)
            return [];

        var ratio = totalSize / (double)SegmentThreshold;
        var segments = Math.Min(maxSegments, (int)Math.Ceiling(ratio));
        var chunk = totalSize / segments;

        var list = new List<(long, long)>(segments);
        for (int i = 0; i < segments; i++) {
            long start = i * chunk;
            long end = (i == segments - 1)
                ? totalSize - 1
                : start + chunk - 1;

            list.Add((start, end));
        }

        return list;
    }
}