namespace MinecraftLaunch.Base.Models.Game;

public record SaveEntry
{
    public long Seed { get; set; }
    public int GameType { get; set; }
    public string Folder { get; set; }
    public string Version { get; set; }
    public string LevelName { get; set; }
    public string FolderName { get; set; }
    public string IconFilePath { get; set; }
    public bool AllowCommands { get; set; }
    public DateTime LastPlayed { get; set; }
}