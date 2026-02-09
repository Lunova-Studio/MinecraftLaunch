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

    // Licensed to the .NET Foundation under one or more agreements.
    // The .NET Foundation licenses this file to you under the MIT license.
    // -> The .NET Foundation licenses this function to us under the MIT license.
    // 使用了.net标准库代码
    private static void ExtractToFileInitialize(ZipArchiveEntry source, string destinationFileName, bool overwrite, out FileStreamOptions fileStreamOptions)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destinationFileName);

        fileStreamOptions = new()
        {
            Access = FileAccess.Write,
            Mode = overwrite ? FileMode.Create : FileMode.CreateNew,
            Share = FileShare.None,
            BufferSize = 16384
        };

        const UnixFileMode OwnershipPermissions =
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;

        // Restore Unix permissions.
        // For security, limit to ownership permissions, and respect umask (through UnixCreateMode).
        // We don't apply UnixFileMode.None because .zip files created on Windows and .zip files created
        // with previous versions of .NET don't include permissions.
        UnixFileMode mode = (UnixFileMode)(source.ExternalAttributes >> 16) & OwnershipPermissions;
        if (mode != UnixFileMode.None && !OperatingSystem.IsWindows())
        {
            fileStreamOptions.UnixCreateMode = mode;
        }
    }
    // Licensed to the .NET Foundation under one or more agreements.
    // The .NET Foundation licenses this file to you under the MIT license.
    internal static async Task ExtractToFileAsync(this ZipArchiveEntry zipArchiveEntry, string destinationFile,bool overwrite,CancellationToken cts)
    {
        cts.ThrowIfCancellationRequested();
        ExtractToFileInitialize(zipArchiveEntry, destinationFile, overwrite, out var fileStreamOptions);
        await using var dst = new FileStream(destinationFile, fileStreamOptions);
        await using var src = zipArchiveEntry.Open(); // OpenAsync真搬不了吧(),等xilu速速换NET10单目标即可
        await src.CopyToAsync(dst, cts).ConfigureAwait(false);
        File.SetLastWriteTime(destinationFile,DateTime.Now);
    }
}