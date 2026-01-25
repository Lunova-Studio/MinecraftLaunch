using MinecraftLaunch.Base.Enums;
using MinecraftLaunch.Base.EventArgs;
using MinecraftLaunch.Base.Interfaces;
using MinecraftLaunch.Base.Models.Game;

namespace MinecraftLaunch.Components.Installer;

public abstract class InstallerBase : IInstaller
{
    public abstract string MinecraftFolder { get; init; }

    public event EventHandler<InstallComplatedEventArgs> Completed;

    public event EventHandler<InstallProgressChangedEventArgs> ProgressChanged;

    public abstract Task<MinecraftEntry> InstallAsync(CancellationToken cancellationToken = default);

    internal void ReportCompleted(bool isSuccessful, Exception exception = default)
    {
        Completed?.Invoke(this, new InstallComplatedEventArgs
        {
            Exception = exception,
            IsSuccessful = isSuccessful
        });
    }

    protected internal virtual void ReportProgress(InstallStep step, double progress, TaskStatus status, int totalCount, int finshedCount, double speed = -1d, bool isSupportSpeed = false)
    {
        ProgressChanged?.Invoke(this, new InstallProgressChangedEventArgs
        {
            Speed = speed,
            Status = status,
            StepName = step,
            Progress = progress,
            TotalStepTaskCount = totalCount,
            IsStepSupportSpeed = isSupportSpeed,
            FinishedStepTaskCount = finshedCount
        });
    }
}