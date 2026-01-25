using MinecraftLaunch.Base.Interfaces;
using MinecraftLaunch.Base.Models.Game;
using MinecraftLaunch.Extensions;
using MinecraftLaunch.Utilities;
using System.Text;
using System.Text.Json;

namespace MinecraftLaunch.Components.Parser;

/// <summary>
/// 官方游戏配置解析器
/// </summary>
/// <remarks>
/// 取自 launcher_profile.json
/// </remarks>
public sealed class DefaultLauncherProfileParser : IDataProcessor
{
    private readonly Guid _clientToken;
    private string _filePath = string.Empty;

    private LauncherProfileEntry _launcherProfile = new();
    public Dictionary<string, object> Datas { get; set; } = [];

    public DefaultLauncherProfileParser(Guid clientToken = default)
    {
        _clientToken = clientToken;
    }

    public void Handle(IEnumerable<MinecraftEntry> minecrafts)
    {
        Datas.Clear();

        var mcList = minecrafts as IList<MinecraftEntry> ?? [.. minecrafts];
        if (mcList.Count == 0)
            return;

        _filePath = Path.Combine(mcList[0].MinecraftFolderPath, "launcher_profiles.json");

        if (File.Exists(_filePath))
        {
            var launcherProfileJson = File.ReadAllText(_filePath, Encoding.UTF8);
            _launcherProfile = launcherProfileJson.Deserialize(new LauncherProfileEntryContext(JsonSerializerUtil
                .GetDefaultOptions()).LauncherProfileEntry) ?? new LauncherProfileEntry();
        }
        else
        {
            _launcherProfile = new LauncherProfileEntry
            {
                Profiles = [],
                ClientToken = _clientToken.ToString("N"),
                LauncherVersion = new LauncherVersionEntry
                {
                    Format = 6,
                    Name = "MinecraftLaunch"
                }
            };
        }

        foreach (var minecraft in mcList)
        {
            _launcherProfile.Profiles.TryAdd(minecraft.Id, new GameProfileEntry
            {
                Type = "custom",
                Name = minecraft.Id,
                Created = DateTime.Now,
                LastVersionId = minecraft.Id,
                GameFolder = minecraft.ToWorkingPath(true),
                Resolution = new()
            });
        }

        Datas = _launcherProfile.Profiles.ToDictionary(x => x.Key, x1 => x1.Value as object);
    }

    public Task SaveAsync(CancellationToken cancellationToken = default)
    {
        _launcherProfile.Profiles = Datas.ToDictionary(x => x.Key, x1 => x1.Value as GameProfileEntry);
        var json = _launcherProfile.Serialize(
            new LauncherProfileEntryContext(JsonSerializerUtil.GetDefaultOptions()).LauncherProfileEntry
        );

        return File.WriteAllTextAsync(_filePath, json, cancellationToken);
    }
}