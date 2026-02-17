
using MinecraftLaunch.Base.Models.SHA1;

namespace MinecraftLaunch.Base.Models.Network;

public record FileHashes
{
    public required string Sha512 { get; init; }
    public required Sha1Data Sha1 { get; init; }
}
