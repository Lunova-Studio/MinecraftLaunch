using MinecraftLaunch.Base.Enums;

namespace MinecraftLaunch.Base.Models.Network;

public record FileHash
{
    public required string Value { get; init; }
    public required HashAlgo Algo { get; init; }
}
