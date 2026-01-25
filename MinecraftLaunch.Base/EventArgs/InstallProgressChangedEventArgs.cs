using MinecraftLaunch.Base.Enums;

namespace MinecraftLaunch.Base.EventArgs;

public class InstallProgressChangedEventArgs : System.EventArgs
{
    public double Speed { get; set; }
    public double Progress { get; set; }

    public int TotalStepTaskCount { get; set; }
    public int FinishedStepTaskCount { get; set; }

    public TaskStatus Status { get; set; }
    public InstallStep StepName { get; set; }
    public bool IsStepSupportSpeed { get; set; }

    [Obsolete($"Replaced by {nameof(StepName)}")]
    public string ProgressStatus { get; set; }
}

public class InstallComplatedEventArgs : System.EventArgs
{
    public bool IsSuccessful { get; set; }
    public Exception Exception { get; set; }
}

public sealed class CompositeInstallProgressChangedEventArgs : InstallProgressChangedEventArgs
{
    public InstallStep PrimaryStepName { get; set; }
}