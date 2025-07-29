namespace MinecraftLaunch.Base.Models;

public record ComponentSettings {
    public bool IsEnableMirror { get; set; }

    public int MaxThread { get; set; } = 64;
    public int MaxFragmented { get; set; } = 128;

    public string CurseForgeApiKey { get; set; } = null;
    public string UserAgent { get; set; } = "MinecraftLaunch/4.0";
}