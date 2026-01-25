using MinecraftLaunch.Base.Models.Game;
using MinecraftLaunch.Components.Parser;
using MinecraftLaunch.Extensions;

namespace MinecraftLaunch.Launch;

public sealed class MinecraftRunner
{
    private readonly MinecraftParser _minecraftParser;

    public LaunchConfig LaunchConfig { get; set; }

    public MinecraftRunner(LaunchConfig launchConfig, MinecraftParser parser)
    {
        _minecraftParser = parser;
        LaunchConfig = launchConfig;
    }

    public MinecraftProcess Run(string id)
    {
        MinecraftEntry minecraft = default;
        IEnumerable<string> arguments = [];

        try
        {
            minecraft = _minecraftParser.GetMinecraft(id);
            ArgumentsParser parser = new(minecraft, LaunchConfig);
            arguments = parser.Parse();

            if (string.IsNullOrEmpty(LaunchConfig.NativesFolder))
                minecraft.ExtractNatives(parser.GetNatives());
        }
        catch (Exception) { }

        return new MinecraftProcess(LaunchConfig, minecraft, arguments);
    }

    public MinecraftProcess Run(MinecraftEntry minecraft) => Run(minecraft.Id);

    public async Task<MinecraftProcess> RunAsync(string id, CancellationToken cancellationToken = default)
    {
        MinecraftEntry minecraft = default;
        IEnumerable<string> arguments = [];

        try
        {
            minecraft = _minecraftParser.GetMinecraft(id);
            ArgumentsParser parser = new(minecraft, LaunchConfig);
            arguments = parser.Parse();

            if (string.IsNullOrEmpty(LaunchConfig.NativesFolder))
                await minecraft.ExtractNativesAsync(parser.GetNatives(), cancellationToken);
        }
        catch (Exception) { }

        return new MinecraftProcess(LaunchConfig, minecraft, arguments);
    }

    public Task<MinecraftProcess> RunAsync(MinecraftEntry minecraft, CancellationToken cancellationToken = default) => RunAsync(minecraft.Id, cancellationToken);
}