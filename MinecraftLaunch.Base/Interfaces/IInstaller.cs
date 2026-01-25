using MinecraftLaunch.Base.Models.Game;

namespace MinecraftLaunch.Base.Interfaces;

public interface IInstaller
{
    string MinecraftFolder { get; }

    Task<MinecraftEntry> InstallAsync(CancellationToken cancellationToken = default);
}