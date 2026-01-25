using MinecraftLaunch.Base.Models.Authentication;

namespace MinecraftLaunch.Base.Models.Game;

public record LaunchConfig
{
    public Account Account { get; set; }

    public bool IsFullscreen { get; set; }
    public bool IsEnableIndependency { get; set; } = true;

    public int Width { get; set; } = 854;
    public int Height { get; set; } = 480;
    public int MinMemorySize { get; set; } = 512;
    public int MaxMemorySize { get; set; } = 1024;

    public JavaEntry JavaPath { get; set; }
    public string LauncherName { get; set; }
    public string NativesFolder { get; set; }
    public ServerInfo ServerInfo { get; set; }
    public string SaveName { get; set; }

    public IEnumerable<string> JvmArguments { get; set; } = [];
}

public record ServerInfo
{
    public int Port { get; set; } = 25565;
    public string Address { get; set; }
}