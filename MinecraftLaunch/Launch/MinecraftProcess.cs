using MinecraftLaunch.Base.Models.Game;
using MinecraftLaunch.Components.Parser;
using MinecraftLaunch.Extensions;
using System.Diagnostics;

namespace MinecraftLaunch.Launch;

public sealed class MinecraftProcess : IDisposable
{
    public Process Process { get; private set; }
    public IEnumerable<string> ArgumentList { get; init; }
    public IReadOnlyList<MinecraftLibrary> Natives { get; private set; }
    public nint MainWindowHandle => Process.MainWindowHandle;

    public event EventHandler Started;

    public event EventHandler<EventArgs> Exited;

    public event EventHandler<LogReceivedEventArgs> OutputLogReceived;

    public MinecraftProcess(LaunchConfig launchConfig, MinecraftEntry minecraft, IEnumerable<string> launchArgs)
    {
        ArgumentList = launchArgs;
        if (!ArgumentList.Any())
            return;

        Process = new Process
        {
            StartInfo = new ProcessStartInfo(launchConfig.JavaPath.JavaPath)
            {
                WorkingDirectory = minecraft.ToWorkingPath(launchConfig.IsEnableIndependency),
                Arguments = string.Join(' ', launchArgs),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            },
            EnableRaisingEvents = true,
        };

        Process.Exited += OnMinecraftProcessExited;
        Process.ErrorDataReceived += OnOutputDataReceived;
        Process.OutputDataReceived += OnOutputDataReceived;

        Start();
    }

    public void Start()
    {
        Process.Start();
        Process.BeginOutputReadLine();
        Process.BeginErrorReadLine();
        Started?.Invoke(this, EventArgs.Empty);
    }

    public void Close()
    {
        Process.Kill();
    }

    public void Dispose() => Process?.Dispose();

    private void OnMinecraftProcessExited(object sender, EventArgs e)
    {
        Exited?.Invoke(this, new());
    }

    private void OnOutputDataReceived(object sender, DataReceivedEventArgs e)
    {
        if (!string.IsNullOrEmpty(e.Data))
        {
            OutputLogReceived?.Invoke(this, new LogReceivedEventArgs(MinecraftLoggingParser.Parse(e.Data)));
        }
    }
}

public record LogReceivedEventArgs(MinecraftLogEntry Data);