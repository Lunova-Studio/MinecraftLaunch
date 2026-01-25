using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace MinecraftLaunch.Base.Utilities;

public static class EnvironmentUtil
{
    private const ushort PE_SIGNATURE = 23117;
    private const ushort IMAGE_FILE_MACHINE_IA64 = 267;
    private const ushort IMAGE_FILE_MACHINE_AMD64 = 523;
    private const uint PE_OPTIONAL_HEADER_SIGNATURE = 17744;

    public static string Arch
        => Environment.Is64BitOperatingSystem ? "64" : "32";

    public static bool IsMac
        => RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

    public static bool IsLinux
        => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

    public static bool IsWindow
        => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    public static string GetPlatformName()
    {
        if (IsMac)
        {
            return "osx";
        }
        else if (IsLinux)
        {
            return "linux";
        }
        else if (IsWindow)
        {
            return "windows";
        }

        throw new NotSupportedException();
    }

    [SupportedOSPlatform("Windows")]
    public static bool Is64BitJavaForWindow(string path)
    {
        ushort architecture = 0;

        try
        {
            using var fileStream = new FileStream(path, FileMode.Open, FileAccess.Read);
            using var binaryReader = new BinaryReader(fileStream);

            if (binaryReader.ReadUInt16() == PE_SIGNATURE)
            {
                fileStream.Seek(0x3A, SeekOrigin.Current);
                fileStream.Seek(binaryReader.ReadUInt32(), SeekOrigin.Begin);

                if (binaryReader.ReadUInt32() == PE_OPTIONAL_HEADER_SIGNATURE)
                {
                    fileStream.Seek(20, SeekOrigin.Current);
                    architecture = binaryReader.ReadUInt16();
                }
            }
        }
        catch (Exception) { }

        return architecture is IMAGE_FILE_MACHINE_AMD64 or IMAGE_FILE_MACHINE_IA64;
    }
}