namespace MinecraftLaunch.Extensions;

public static class DirectoryExtension
{
    public static IEnumerable<FileInfo> FindAll(this DirectoryInfo directory, string file)
    {
        foreach (var item in directory.EnumerateFiles())
            if (item.Name == file)
                yield return item;

        foreach (var item in directory.EnumerateDirectories())
            foreach (var info in item.FindAll(file))
                yield return info;
    }
}