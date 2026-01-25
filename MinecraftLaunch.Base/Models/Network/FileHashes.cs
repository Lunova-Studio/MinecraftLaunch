namespace MinecraftLaunch.Base.Models.Network;

public record FileHashes
{
    public required string Sha512 { get; init; }
    public required string Sha1 { get; init; }
}