using MinecraftLaunch.Base.Models.Game;

namespace MinecraftLaunch.Base.Interfaces;

public interface IDataProcessor
{
    Dictionary<string, object> Datas { get; set; }

    void Handle(IEnumerable<MinecraftEntry> data);

    Task SaveAsync(CancellationToken cancellationToken = default);
}