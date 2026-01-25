using MinecraftLaunch.Base.Models.Game;
using NbtToolkit.Binary;

namespace MinecraftLaunch.Base.Interfaces;

public interface INbtParser
{
    NbtReader GetReader(NbtCompression compression = NbtCompression.None);

    NbtWriter GetWriter(NbtCompression compression = NbtCompression.None);

    Task<SaveEntry> ParseSaveAsync(string saveName, bool @bool = true, CancellationToken cancellationToken = default);
}