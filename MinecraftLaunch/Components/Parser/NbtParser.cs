using MinecraftLaunch.Base.Interfaces;
using MinecraftLaunch.Base.Models.Game;
using MinecraftLaunch.Extensions;
using NbtToolkit.Binary;

namespace MinecraftLaunch.Components.Parser;

public sealed class NbtParser : INbtParser
{
    private NbtReader _reader;

    private readonly string _nbtFilePath;
    private readonly MinecraftEntry _entry;

    internal NbtParser(string nbtFile)
    {
        _nbtFilePath = nbtFile;
    }

    internal NbtParser(MinecraftEntry minecraftEntry)
    {
        _entry = minecraftEntry;
    }

    public NbtReader GetReader(NbtCompression compression = NbtCompression.None)
    {
        if (string.IsNullOrEmpty(_nbtFilePath))
        {
            throw new ArgumentNullException(nameof(_nbtFilePath));
        }

        var fileStream = new FileStream(_nbtFilePath, FileMode.Open, FileAccess.Read);
        return new NbtReader(fileStream, compression, true);
    }

    public NbtWriter GetWriter(NbtCompression compression = NbtCompression.None)
    {
        if (string.IsNullOrEmpty(_nbtFilePath))
        {
            throw new ArgumentNullException(nameof(_nbtFilePath));
        }

        var fileStream = new FileStream(_nbtFilePath, FileMode.Open, FileAccess.Write);
        return new NbtWriter(fileStream, compression, false);
    }

    public async Task<SaveEntry> ParseSaveAsync(string saveName, bool @bool = true, CancellationToken cancellationToken = default)
    {
        if (_entry is null)
            throw new InvalidOperationException("Initialization error");

        var saveFolder = Path.Combine(_entry.ToWorkingPath(@bool), "saves", saveName);
        var saveEntry = new SaveEntry
        {
            FolderName = new DirectoryInfo(saveFolder).Name,
            Folder = saveFolder
        };

        await Task.Run(() =>
        {
            var time = DateTime.Now;

            using var fileStream = new FileStream(Path.Combine(saveFolder, "level.dat"), FileMode.Open, FileAccess.Read);
            _reader = new NbtReader(fileStream, NbtCompression.GZip, true);

            var rootTag = _reader.ReadRootTag();
            var dataTagCompound = rootTag["Data"].AsTagCompound();

            saveEntry.LevelName = dataTagCompound["LevelName"].AsString();
            saveEntry.AllowCommands = dataTagCompound["allowCommands"].AsBool();
            saveEntry.GameType = dataTagCompound["GameType"].AsInt();
            saveEntry.Version = dataTagCompound["Version"].AsTagCompound()["Name"].AsString();

            if (dataTagCompound.ContainsKey("WorldGenSettings"))
                saveEntry.Seed = dataTagCompound["WorldGenSettings"].AsTagCompound()["seed"].AsLong();
            else if (dataTagCompound.ContainsKey("RandomSeed"))
                saveEntry.Seed = dataTagCompound["RandomSeed"].AsLong();

            saveEntry.LastPlayed = DateTimeOffset.FromUnixTimeMilliseconds(dataTagCompound["LastPlayed"].AsLong()).ToLocalTime().DateTime;

            if (File.Exists(Path.Combine(saveFolder, "icon.png")))
                saveEntry.IconFilePath = Path.Combine(saveFolder, "icon.png");
        }, cancellationToken);

        return saveEntry;
    }
}