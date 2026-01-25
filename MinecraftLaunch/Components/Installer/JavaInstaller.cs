using Flurl.Http;
using MinecraftLaunch.Base.Enums;
using MinecraftLaunch.Base.EventArgs;
using MinecraftLaunch.Base.Models.Network;
using MinecraftLaunch.Components.Downloader;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;

namespace MinecraftLaunch.Components.Installer;

/// <summary>
/// 跨平台 Java 安装器
/// </summary>
public sealed class JavaInstaller
{
    public string JavaFolder { get; init; }
    public string MinecraftFolder { get; init; }

    public event EventHandler<EventArgs> Completed;

    public event EventHandler<InstallProgressChangedEventArgs> ProgressChanged;

    private void ReportCompleted()
    {
        Completed?.Invoke(this, EventArgs.Empty);
    }

    private void ReportProgress(InstallStep step, double progress, TaskStatus status, int totalCount, int finshedCount, double speed = -1d, bool isSupportStep = false)
    {
        ProgressChanged?.Invoke(this, new InstallProgressChangedEventArgs
        {
            Speed = speed,
            Status = status,
            StepName = step,
            Progress = progress,
            TotalStepTaskCount = totalCount,
            IsStepSupportSpeed = isSupportStep,
            FinishedStepTaskCount = finshedCount
        });
    }

    public static JavaInstaller Create(string javaFolder)
    {
        return new JavaInstaller
        {
            JavaFolder = javaFolder,
        };
    }

    /// <summary>
    /// 异步安装
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns></returns>

    public async Task InstallAsync(CancellationToken cancellationToken = default)
    {
        ReportProgress(InstallStep.Started, 0.0d, TaskStatus.WaitingToRun, 1, 1);
        // 先汇报进度，避免 UI 卡死
        try
        {
            var javaInfo = await FetchJavaInfoAsync(cancellationToken); // 获取 Java 信息
            var javaFile = await DownloadJavaAsync(javaInfo, cancellationToken); // 异步下载
            await ExtractJavaAsync(javaFile, cancellationToken); // 异步解压缩

            ReportProgress(InstallStep.RanToCompletion, 1.0d, TaskStatus.RanToCompletion, 1, 1);// 完成
            ReportCompleted(); // 汇报完成
        }
        catch (Exception ex)
        {
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
    private async Task<JsonNode> FetchJavaInfoAsync(CancellationToken cancellationToken)
    {
        ReportProgress(InstallStep.FetchingMetadata, 0.1d, TaskStatus.Running, 1, 0);

        string url = "https://launchermeta.mojang.com/v1/products/java-runtime/2ec0cc96c44e5a76b9c8b7c39df7210883d12871/all.json";
        string json = await url.GetStringAsync(cancellationToken: cancellationToken); // 获取 Java 元数据

        string platformKey = GetPlatformKey(); // 获取平台
        var javaInfo = JsonNode.Parse(json)?[platformKey]?["java-runtime-gamma"]?.AsArray()
            ?? throw new InvalidOperationException($"无法获取 Java 元数据，平台：{platformKey}"); // 意思：解析Json内容，如果不是数组就报错
        if (javaInfo == null)
        {
            throw new InvalidOperationException($"无法获取 Java 元数据，平台：{platformKey}");
        }
        ReportProgress(InstallStep.FetchingMetadata, 0.2d, TaskStatus.Running, 1, 1); // 汇报进度
        return javaInfo;
    }

    private async Task<FileInfo> DownloadJavaAsync(JsonNode javaInfo, CancellationToken cancellationToken)
    {
        ReportProgress(InstallStep.DownloadPackage, 0.3d, TaskStatus.Running, 1, 0); // 汇报进度

        string javaUrl = javaInfo[0]["manifest"]?["url"]?.ToString() // [0] 代表第一个元素，[""manifest"] 代表 manifest 属性
            ?? javaInfo[0]["url"]?.ToString() // ["url"] 代表 url 属性
            ?? throw new InvalidOperationException("无法解析 Java manifest 下载地址"); // 意思：如果没有这个地址就报错

        string fileName = Path.Combine(JavaFolder, "java-runtime-filelist.json"); // 拼接路径
        var downloadRequest = new DownloadRequest(javaUrl, fileName); // 创建下载请求

        await new DefaultDownloader()
            .DownloadAsync(downloadRequest, cancellationToken); // 异步下载 manifest

        ReportProgress(InstallStep.DownloadPackage, 0.6d, TaskStatus.Running, 1, 1); // 汇报进度
        return new FileInfo(fileName);
    }

    private async Task ExtractJavaAsync(FileInfo javaFile, CancellationToken cancellationToken)
    {
        ReportProgress(InstallStep.ExtractingFiles, 0.7d, TaskStatus.Running, 1, 0); // 汇报进度

        string extractPath = Path.Combine(JavaFolder, "runtime");
        if (!Directory.Exists(extractPath))
        {
            Directory.CreateDirectory(extractPath);
        }

        await Task.Run(() => ZipFile.ExtractToDirectory(javaFile.FullName, extractPath, true), cancellationToken);

        ReportProgress(InstallStep.ExtractingFiles, 0.9d, TaskStatus.Running, 1, 1);
    }

    private string GetPlatformKey()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return RuntimeInformation.OSArchitecture == Architecture.X64 ? "windows-x64" : "windows-x86";
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return "linux";
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return "mac-os";
        }
        else
        {
            throw new PlatformNotSupportedException("不支持的操作系统平台");
        }
    }
}