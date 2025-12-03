
namespace MinecraftLaunch.Base.Models.Network;

public record FileModule
{
    public required string Name { get; init; }
    public required long Fingerprint { get; init; }
}
