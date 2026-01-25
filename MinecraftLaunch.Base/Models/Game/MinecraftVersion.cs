using MinecraftLaunch.Base.Enums;
using System.Text.RegularExpressions;

namespace MinecraftLaunch.Base.Models.Game;

public partial record struct MinecraftVersion(string VersionId, MinecraftVersionType Type)
{
    [GeneratedRegex(@"^\d+\.\d+(\.\d+)?$")]
    private static partial Regex ReleaseRegex();

    [GeneratedRegex(@"^\d{2}w\d{2}[a-z]$")]
    private static partial Regex SnapshotRegex();

    [GeneratedRegex(@"^\d+\.\d+(\.\d+)?-pre\d+$")]
    private static partial Regex PreReleaseRegex();

    public static MinecraftVersion Parse(string id)
    {
        if (ReleaseRegex().IsMatch(id))
            return new MinecraftVersion(id, MinecraftVersionType.Release);
        else if (PreReleaseRegex().IsMatch(id))
            return new MinecraftVersion(id, MinecraftVersionType.Snapshot);
        else if (SnapshotRegex().IsMatch(id))
            return new MinecraftVersion(id, MinecraftVersionType.Snapshot);
        else if (id.StartsWith("beta", StringComparison.OrdinalIgnoreCase))
            return new MinecraftVersion(id, MinecraftVersionType.OldBeta);
        else if (id.StartsWith("alpha", StringComparison.OrdinalIgnoreCase))
            return new MinecraftVersion(id, MinecraftVersionType.OldAlpha);
        else
            return new MinecraftVersion(id, MinecraftVersionType.Release);
    }
}