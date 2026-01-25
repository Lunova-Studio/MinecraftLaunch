using MinecraftLaunch.Base.Enums;

namespace MinecraftLaunch.Base.Models.Network;

public record ModrinthSearchOptions
{
    public string SearchFilter { get; set; }
    public string Version { get; set; } = "";
    public string Category { get; set; } = "";
    public string ProjectType { get; set; } = "mod";
    public ModLoaderType ModLoader { get; set; } = ModLoaderType.Any;
    public ModrinthSearchIndex Index { get; set; } = ModrinthSearchIndex.Relevance;
    public int Limit { get; set; } = 10;
    public int Offset { get; set; } = 0;
}