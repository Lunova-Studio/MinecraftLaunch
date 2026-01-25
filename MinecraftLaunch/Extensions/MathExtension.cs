using MinecraftLaunch.Base.EventArgs;

namespace MinecraftLaunch.Extensions;

public static class MathExtension
{
    public static double Normalize(this double value) => value / 100.0;

    /// <summary>
    /// Converts the download progress to a percentage.
    /// </summary>
    /// <param name="args">The download progress arguments.</param>
    /// <returns>The download progress as a percentage.</returns>
    public static double ToPercentage(this ResourceDownloadProgressChangedEventArgs args)
    {
        return (double)args.CompletedCount / (double)args.TotalCount;
    }

    /// <summary>
    /// Converts the specified progress value to a percentage within the specified range.
    /// </summary>
    /// <param name="progress">The progress value to be converted.</param>
    /// <param name="mini">The minimum value of the range.</param>
    /// <param name="max">The maximum value of the range.</param>
    /// <returns>The progress value as a percentage within the specified range.</returns>
    public static double ToPercentage(this double progress, double mini, double max)
    {
        if (progress > 1)
            progress = progress.Normalize();

        return mini + (max - mini) * progress;
    }
}