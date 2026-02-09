using System.IO.Compression;

namespace MinecraftLaunch.Extensions;
public static class ZipArchiveExtension {
    public static string ReadAsString(this ZipArchiveEntry archiveEntry) {
        using var stream = archiveEntry.Open();
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public static void ExtractTo(this ZipArchiveEntry zipArchiveEntry, string destinationFile) {
        var file = new FileInfo(destinationFile);

        if (file.Directory is null)
            throw new DirectoryNotFoundException($"Directory of {destinationFile} not found");

        if (!file.Directory.Exists)
            file.Directory.Create();

        zipArchiveEntry.ExtractToFile(destinationFile, true);
    }

    public static async Task ExtractToFileAsync(this ZipArchiveEntry zipArchiveEntry, string destinationFile)
    {
        await using var dst = File.OpenWrite(destinationFile);
        await using var src = zipArchiveEntry.Open();
        await src.CopyToAsync(dst);
    }
}