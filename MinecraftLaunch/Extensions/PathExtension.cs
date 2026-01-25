using MinecraftLaunch.Base.Models.Game;

namespace MinecraftLaunch.Extensions;

public static class PathExtension
{
    public static string ToPath(this string raw)
    {
        if (!Enumerable.Contains(raw, ' '))
        {
            return raw;
        }
        return "\"" + raw + "\"";
    }

    /// <summary>
    /// Gets the path ofthe libraries directory.
    /// </summary>
    /// <param name="entry">The game entry.</param>
    /// <returns>The path of the libraries directory.</returns>
    public static string ToLibrariesPath(this MinecraftEntry entry) =>
        Path.Combine(entry.MinecraftFolderPath, "libraries");

    public static string ToNativesPath(this MinecraftEntry entry) =>
        Path.Combine(entry.MinecraftFolderPath, "versions", entry.Id, "natives");

    public static string ToWorkingPath(this MinecraftEntry entry, bool isEnableIndependency) => isEnableIndependency
        ? Path.Combine(entry.MinecraftFolderPath, "versions", entry.Id)
        : entry.MinecraftFolderPath;

    public static string ToLogsPath(this MinecraftEntry entry, bool isEnableIndependency) =>
        Path.Combine(entry.ToWorkingPath(isEnableIndependency), "logs");
}