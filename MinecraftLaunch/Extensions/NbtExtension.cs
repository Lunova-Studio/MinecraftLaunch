using MinecraftLaunch.Base.Interfaces;
using MinecraftLaunch.Base.Models.Game;
using MinecraftLaunch.Components.Parser;

namespace MinecraftLaunch.Extensions;

public static class NbtExtension
{
    public static INbtParser GetNBTParser(this string nbtFilePath)
    {
        if (string.IsNullOrEmpty(nbtFilePath))
        {
            throw new ArgumentNullException(nameof(nbtFilePath));
        }

        return new NbtParser(nbtFilePath);
    }

    public static INbtParser GetNBTParser(this MinecraftEntry entry)
    {
        return new NbtParser(entry);
    }
}