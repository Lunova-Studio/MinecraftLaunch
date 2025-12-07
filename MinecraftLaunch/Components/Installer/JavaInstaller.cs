using System.Collections.Concurrent;
using Flurl.Http;
using MinecraftLaunch.Base.Enums;
using MinecraftLaunch.Base.Models.Network;
using MinecraftLaunch.Components.Downloader;
using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Runtime.InteropServices;
using MinecraftLaunch.Base.EventArgs;
namespace MinecraftLaunch.Components.Installer;

/// <summary>
/// 跨平台 Java 安装器
/// </summary>
public sealed class JavaInstaller{
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
    private async Task<JsonNode> FetchJavaInfoAsync(CancellationToken cancellationToken) {
        ReportProgress(InstallStep.FetchingMetadata, 0.1d, TaskStatus.Running, 1, 0);

        string url = "https://launchermeta.mojang.com/v1/products/java-runtime/2ec0cc96c44e5a76b9c8b7c39df7210883d12871/all.json";
        string json = await url.GetStringAsync(cancellationToken: cancellationToken); // 获取 Java 元数据

        string platformKey = GetPlatformKey(); // 获取平台
        var javaInfo = JsonNode.Parse(json)?[platformKey]?["java-runtime-gamma"]?.AsArray()
            ?? throw new InvalidOperationException($"无法获取 Java 元数据，平台：{platformKey}"); // 意思：解析Json内容，如果不是数组就报错
        if (javaInfo == null) {
            throw new InvalidOperationException($"无法获取 Java 元数据，平台：{platformKey}");
        }
        ReportProgress(InstallStep.FetchingMetadata, 0.2d, TaskStatus.Running, 1, 1); // 汇报进度
        return javaInfo;
    }

    private async Task<FileInfo> DownloadJavaAsync(JsonNode javaInfo, CancellationToken cancellationToken) {
        ReportProgress(InstallStep.DownloadPackage, 0.3d, TaskStatus.Running, 1, 0); // 汇报进度

        var javaUrl = javaInfo [0] ["manifest"]?["url"]?.ToString() // [0] 代表第一个元素，[""manifest"] 代表 manifest 属性
            ?? javaInfo[0]["url"]?.ToString() // ["url"] 代表 url 属性
            ?? throw new InvalidOperationException("无法解析 Java manifest 下载地址"); // 意思：如果没有这个地址就报错

        string fileName = Path.Combine(JavaFolder, "java-runtime-filelist.json"); // 拼接路径
        var downloadRequest = new DownloadRequest(javaUrl, fileName){
            Size = long.Parse(javaInfo[0]["manifest"]["size"].ToString())
        }; // 创建下载请求

        Console.WriteLine(javaUrl);

        await new DefaultDownloader()
            .DownloadAsync(downloadRequest, cancellationToken); // 异步下载 manifest

        ReportProgress(InstallStep.DownloadPackage, 0.6d, TaskStatus.Running, 1, 1); // 汇报进度
        return new FileInfo(fileName);
    }
    
    private async Task ExtractJavaFromManifestAsync(FileInfo manifestFile, CancellationToken token)
{
    string extractPath = Path.Combine(JavaFolder, "runtime");
    if (!Directory.Exists(extractPath))
        Directory.CreateDirectory(extractPath);

    var json = JsonNode.Parse(await File.ReadAllTextAsync(manifestFile.FullName, token).ConfigureAwait(false));
    var files = json!["files"]!.AsObject();

    var entries = files.ToList();
    int totalFiles = entries.Count;
    int completedFiles = 0;

    // 并发数来自 DownloadManager
    int maxConcurrentFiles = Math.Max(1, DownloadManager.MaxThread);

    int perFileTimeoutMs = 60_000; // 每文件超时 60 秒
    int maxRetries = 3;             // 每文件最多重试 3 次
    int baseRetryDelayMs = 1000;    // 重试间隔指数退避基数

    using var semaphore = new SemaphoreSlim(maxConcurrentFiles);
    var failedFiles = new ConcurrentDictionary<string, string>();
    var tasks = new List<Task>(totalFiles);

    long globalDownloadedBytes = 0;
    var globalStopwatch = Stopwatch.StartNew();

    foreach (var kv in entries) {
        var fileKey = kv.Key;
        var fileNode = kv.Value!;

        tasks.Add(Task.Run(async () => {
            await semaphore.WaitAsync(token).ConfigureAwait(false);
            try {
                string filePath = Path.Combine(extractPath, fileKey);
                var fileInfo = fileNode.AsObject();

                // 目录直接创建
                if (fileInfo["type"]?.ToString() == "directory") {
                    Directory.CreateDirectory(filePath);
                    Interlocked.Increment(ref completedFiles);
                    ReportProgressWithSpeed();
                    return;
                }

                string url = fileInfo["downloads"]?["raw"]?["url"]?.ToString()
                             ?? throw new InvalidOperationException($"无法解析文件下载 URL: {fileKey}");
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
                double speed = globalDownloadedBytes / Math.Max(1.0, globalStopwatch.Elapsed.TotalSeconds); // Bytes/s

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
        globalDownloadedBytes / Math.Max(1.0, globalStopwatch.Elapsed.TotalSeconds));
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
