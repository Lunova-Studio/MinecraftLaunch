using MinecraftLaunch.Base.Enums;

namespace MinecraftLaunch.Base.Models.Network;

public record CurseforgeSearchOptions {
    public int ClassId { get; set; } = 6; // 默认为模组类别
    public int CategoryId { get; set; } = 0;

    public string GameVersion { get; set; }
    public required string SearchFilter { get; set; }

    public SortOrder SortOrder { get; set; } = SortOrder.Desc;
    public SortField SortField { get; set; } = SortField.Featured;
    public ModLoaderType ModLoaderType { get; set; }
}

public enum SortOrder {
    /// <summary>
    /// 升序排序
    /// </summary>
    Asc,

    /// <summary>
    /// 降序排序
    /// </summary>
    Desc
}

public enum SortField {
    Featured = 1,
    Popularity,
    LastUpdated,
    Name,
    Author,
    TotalDownloads,
    Category,
    GameVersion,
    EarlyAccess,
    FeaturedReleased,
    ReleasedDate,
    Rating
}