namespace MinecraftLaunch.Base.Interfaces;

public interface ISearchResult
{
    public int Index { get; init; }
    public int PageSize { get; init; }
    public long TotalCount { get; init; }
}