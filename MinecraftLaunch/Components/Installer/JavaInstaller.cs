using System.Collections.Concurrent;
using Flurl.Http;
using MinecraftLaunch.Base.Enums;
using MinecraftLaunch.Base.Models.Network;
using MinecraftLaunch.Components.Downloader;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using MinecraftLaunch.Base.EventArgs;
namespace MinecraftLaunch.Components.Installer;

/// <summary>
/// 跨平台 Java 安装器
/// </summary>
public sealed class JavaInstaller {
    private string JavaFolder { get; init; }
    public  string MinecraftFolder { get; init; }

    public event EventHandler<EventArgs> Completed;
    public event EventHandler<InstallProgressChangedEventArgs> ProgressChanged;

    void ReportCompleted() {
        Completed?.Invoke(this, EventArgs.Empty);
    }

    void ReportProgress(InstallStep step, double progress, TaskStatus status, int totalCount, int finshedCount, double speed = -1d, bool isSupportStep = false) {
        ProgressChanged?.Invoke(this, new InstallProgressChangedEventArgs {
            Speed = speed,
            Status = status,
            StepName = step,
            Progress = progress,
            TotalStepTaskCount = totalCount,
            IsStepSupportSpeed = isSupportStep,
            FinishedStepTaskCount = finshedCount
        });
    }

    public static JavaInstaller Create(string javaFolder) {
        return new JavaInstaller {
            JavaFolder = javaFolder,
        };
    }

    /// <summary>
    /// 异步安装
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns></returns>
    
    public async Task InstallAsync(CancellationToken cancellationToken = default) {
        ReportProgress(InstallStep.Started, 0.0d, TaskStatus.WaitingToRun, 1, 1);
        // 先汇报进度，避免 UI 卡死
        try {
            var javaInfo = await FetchJavaInfoAsync(cancellationToken); // 获取 Java 信息
            var javaFile = await DownloadJavaAsync(javaInfo, cancellationToken); // 异步下载
            await ExtractJavaFromManifestAsync(javaFile, cancellationToken); // 异步下载

            ReportProgress(InstallStep.RanToCompletion, 1.0d, TaskStatus.RanToCompletion, 1, 1);// 完成
            ReportCompleted(); // 汇报完成
        } catch (Exception ex) {
            ReportProgress(InstallStep.Interrupted, 1.0d, TaskStatus.Canceled, 1, 1);
            ReportCompleted();
            throw new InvalidOperationException("Java 安装失败", ex);
        }
    }

    /// <summary>
    /// 获取 Java 信息
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns></returns>
    /// <exception cref="InvalidOperationException"></exception>
    private async Task<JsonElement> FetchJavaInfoAsync(CancellationToken cancellationToken) {
        ReportProgress(InstallStep.FetchingMetadata, 0.1d, TaskStatus.Running, 1, 0);

        string url = "https://launchermeta.mojang.com/v1/products/java-runtime/2ec0cc96c44e5a76b9c8b7c39df7210883d12871/all.json";
        await using var stream = await url.GetStreamAsync(cancellationToken:cancellationToken); // 获取 Java 元数据
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = doc.RootElement;
        
        var platformKey = GetPlatformKey(); // 获取平台
        if(!root.TryGetProperty(platformKey,out var platformElement)||
           !platformElement.TryGetProperty("java-runtime-gamma"u8,out var javaInfoElement)||
           javaInfoElement.ValueKind is not JsonValueKind.Array)
            throw new InvalidOperationException($"无法获取 Java 元数据，平台：{platformKey}"); // 意思：解析Json内容，如果不是数组就报错
        
        // 原逻辑没有判长度为0的条件
        ReportProgress(InstallStep.FetchingMetadata, 0.2d, TaskStatus.Running, 1, 1); // 汇报进度
        // 此处无法进行所有权传递,Clone
        return javaInfoElement.Clone();
    }

    private async Task<FileInfo> DownloadJavaAsync(/*ArrayElement*/JsonElement javaInfo, CancellationToken cancellationToken) {
        ReportProgress(InstallStep.DownloadPackage, 0.3d, TaskStatus.Running, 1, 0); // 汇报进度
        if (javaInfo.ValueKind != JsonValueKind.Array) throw new ArgumentException("value kind is not Array",nameof(javaInfo));

        JsonElement urlElement;
        if (!(javaInfo[0].TryGetProperty("manifest"u8, out var manifestElement) &&
             manifestElement.TryGetProperty("url"u8, out urlElement)
            ) &&
            !javaInfo[0].TryGetProperty("url"u8, out urlElement)) throw new InvalidOperationException("无法解析 Java manifest 下载地址");// 意思：如果没有这个地址就报错

        string fileName = Path.Combine(JavaFolder, "java-runtime-filelist.json"); // 拼接路径
        var sizeElement = manifestElement.GetProperty("size"u8);
        var url = urlElement.GetString();
        var downloadRequest = new DownloadRequest(urlElement.GetString(), fileName){
            Size = sizeElement.ValueKind switch
            {
                JsonValueKind.Number=>sizeElement.GetInt64(),
                JsonValueKind.String=>long.Parse(sizeElement.GetString()!),
                _=> throw new InvalidOperationException("意外的JSON数据:size")
            }
        }; // 创建下载请求

        Console.WriteLine(url);

        await new DefaultDownloader()
            .DownloadAsync(downloadRequest, cancellationToken); // 异步下载 manifest

        ReportProgress(InstallStep.DownloadPackage, 0.6d, TaskStatus.Running, 1, 1); // 汇报进度
        return new FileInfo(fileName);
    }
    
    private async Task ExtractJavaFromManifestAsync(FileInfo manifestFile, CancellationToken token) {
        string extractPath = Path.Combine(JavaFolder, "runtime");
        if (!Directory.Exists(extractPath))
            Directory.CreateDirectory(extractPath);
        await using var stream = File.OpenRead(manifestFile.FullName);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: token);
        var filesEnumeratorElement = doc.RootElement.GetProperty("files"u8);

        int totalFiles = 0;
        // count total,filesEnumerator咋没暴露计数API(
        foreach (var i in filesEnumeratorElement.EnumerateObject()) totalFiles++;

        var filesEnumerator = filesEnumeratorElement.EnumerateObject();
        
        int completedFiles = 0;
    
        // 并发数来自 DownloadManager
        int maxConcurrentFiles = Math.Max(1, DownloadManager.MaxThread);
    
        int perFileTimeoutMs = 60_000; // 每文件超时 60 秒
        int maxRetries = DownloadManager.MaxRetryCount; // 每文件最多重试次数与 DownloadManager.MaxRetryCount 保持一致
        int baseRetryDelayMs = 1000;    // 重试间隔指数退避基数
    
        using var semaphore = new SemaphoreSlim(maxConcurrentFiles);
        var failedFiles = new ConcurrentDictionary<string, string>();
        var tasks = new List<Task>(totalFiles);
    
        long globalDownloadedBytes = 0;
        var globalStopwatch = Stopwatch.GetTimestamp();
    
        foreach (var property in filesEnumerator) {
            var fileKey = property.Name;
            var fileElement = property.Value;
    
            tasks.Add(Task.Run(async () => {
                await semaphore.WaitAsync(token).ConfigureAwait(false);
                try {
                    string filePath = Path.Combine(extractPath, fileKey);
                    
    
                    // 目录直接创建
                    if (fileElement.TryGetProperty("type"u8, out var value) && value.GetString() == "directory")
                    {
                        Directory.CreateDirectory(filePath);
                        Interlocked.Increment(ref completedFiles);
                        ReportProgressWithSpeed();
                        return;
                    }
                    
                    if(!fileElement.TryGetProperty("downloads"u8,out var downloads) ||
                       !downloads.TryGetProperty("raw"u8,out var raw)||
                       !raw.TryGetProperty("url"u8,out var urlElement))throw new InvalidOperationException($"无法解析文件下载 URL: {fileKey}");
                    var url = urlElement.GetString();
                    Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
    
                    var success = false;
                    Exception lastEx = null;
    
                    for (var attempt = 1; attempt <= maxRetries && !token.IsCancellationRequested; attempt++) {
                        using var singleCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                        singleCts.CancelAfter(perFileTimeoutMs);
    
                        var req = new DownloadRequest(url, filePath);
    
                        Action<ResourceDownloadProgressChangedEventArgs> progressCallback = e => {
                            double fileProgress = e.TotalBytes > 0 ? (double)e.DownloadedBytes / e.TotalBytes : 0.0;
                            Interlocked.Exchange(ref globalDownloadedBytes, Interlocked.Read(ref globalDownloadedBytes) + e.DownloadedBytes);
    
                            ReportProgressWithSpeed(fileProgress);
                        };
    
                        if (req.ProgressChanged == null)
                            req.ProgressChanged = progressCallback;
                        else {
                            var prev = req.ProgressChanged;
                            req.ProgressChanged = e => { 
                                prev(e);
                                progressCallback(e);
                            };
                        }
    
                        try {
                            await new DefaultDownloader().DownloadAsync(req, singleCts.Token).ConfigureAwait(false);
                            success = true;
                            break;
                        }
                        catch (OperationCanceledException oce) {
                            lastEx = oce;
                            if (token.IsCancellationRequested) break;
                        }
                        catch (Exception ex) {
                            lastEx = ex;
                        }
                        finally {
                            req.ProgressChanged = null;
                        }
    
                        await Task.Delay(baseRetryDelayMs * attempt, token).ConfigureAwait(false);
                    }
    
                    if (!success) {
                        var reason = lastEx?.Message ?? "Unknown";
                        failedFiles[fileKey] = reason;
                        Console.WriteLine($"[Java] 下载失败: {fileKey} 原因: {reason} - JavaInstaller.cs:207");
                    }
                }
                finally {
                    Interlocked.Increment(ref completedFiles);
                    ReportProgressWithSpeed();
                    semaphore.Release();
                }
    
                void ReportProgressWithSpeed(double currentFileProgress = 0.0) {
                    int snap = Volatile.Read(ref completedFiles);
                    double overallProgress = 0.7 + (0.3 * Math.Min((snap + currentFileProgress) / totalFiles, 1.0));
                    double speed = globalDownloadedBytes / Math.Max(1.0, Stopwatch.GetElapsedTime(globalStopwatch).Seconds); // Bytes/s
    
                    ReportProgress(
                        InstallStep.DownloadJava,
                        overallProgress,
                        TaskStatus.Running,
                        snap,
                        totalFiles,
                        speed
                    );
                }
            }, CancellationToken.None));
        }
    
        await Task.WhenAll(tasks).ConfigureAwait(false);
    
        if (!failedFiles.IsEmpty) {
            Console.WriteLine("[Java] 以下文件最终失败： - JavaInstaller.cs:237");
            foreach (var kv in failedFiles)
                Console.WriteLine($"{kv.Key} : {kv.Value} - JavaInstaller.cs:239");
        }
        // 最终 100%
        ReportProgress(
            InstallStep.DownloadJava,
            1.0,
            TaskStatus.Running,
            totalFiles,
            totalFiles,
            globalDownloadedBytes / Math.Max(1.0, Stopwatch.GetElapsedTime(globalStopwatch).Seconds));
    }
    private string GetPlatformKey() {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
            return RuntimeInformation.OSArchitecture == Architecture.X64 ? "windows-x64" : "windows-x86";
        } else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) {
            return "linux";
        } else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
            return "mac-os";
        } else {
            throw new PlatformNotSupportedException("不支持的操作系统平台");
        }
    }
}
