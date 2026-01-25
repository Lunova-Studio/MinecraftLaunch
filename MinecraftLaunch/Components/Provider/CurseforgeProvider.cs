using Flurl;
using Flurl.Http;
using MinecraftLaunch.Base.Enums;
using MinecraftLaunch.Base.Models.Network;
using MinecraftLaunch.Extensions;
using MinecraftLaunch.Utilities;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Web;

namespace MinecraftLaunch.Components.Provider;

public sealed class CurseforgeProvider
{
    public static readonly string CurseforgeApi = "https://api.curseforge.com/v1";

    public async Task<IDictionary<CurseforgeResourceFile, IEnumerable<CurseforgeResourceFile>>> GetResourceFilesByFingerprintsAsync(uint[] modFingerprints, CancellationToken cancellationToken = default)
    {
        var request = CreateRequest("fingerprints", "432");
        var payload = new CurseforgeFingerprintsRequestPayload(modFingerprints);

        using var responseMessage = await request.PostAsync(JsonContent.Create(payload,
            CurseforgeRequestPayloadContext.Default.CurseforgeFingerprintsRequestPayload),
                cancellationToken: cancellationToken);

        var json = await responseMessage.GetStringAsync();
        var jsonNode = json.AsNode()
            .Select("data");

        var exactMatches = jsonNode.GetEnumerable("exactMatches");
        if (exactMatches is null)
            return null;

        return exactMatches.ToDictionary(x => ParseFile(x.Select("file")),
            x1 => x1.GetEnumerable("latestFiles").Select(ParseFile));
    }

    public async Task<IEnumerable<CurseforgeResourceFile>> GetResourceFilesByModIdAsync(int modId, int pageSize = 50, int delayBetweenRequests = 50, int maxRequests = 16, CancellationToken cancellationToken = default)
    {
        if (pageSize <= 0 || pageSize > 50)
        {
            throw new ArgumentException("pageSize must be between 1 and 50", nameof(pageSize));
        }

        var allFiles = new List<CurseforgeResourceFile>();

        var (firstPageFiles, totalCount) = await GetFirstPageWithTotalCountAsync(modId, pageSize, cancellationToken);

        if (firstPageFiles != null)
        {
            allFiles.AddRange(firstPageFiles);
        }

        if (totalCount <= pageSize)
        {
            return allFiles;
        }
        int totalPages = (int)Math.Ceiling((double)totalCount / pageSize);

        await ProcessRemainingPagesAsync(modId, totalPages, delayBetweenRequests, allFiles, cancellationToken, pageSize, maxRequests);

        return allFiles;
    }

    public async Task<IEnumerable<CurseforgeResource>> GetResourcesByModIdsAsync(IEnumerable<long> modIds, CancellationToken cancellationToken = default)
    {
        var request = CreateRequest("mods");
        var payload = new CurseforgeResourcesRequestPayload([.. modIds]);

        using var responseMessage = await request.PostAsync(JsonContent.Create(payload,
            CurseforgeRequestPayloadContext.Default.CurseforgeResourcesRequestPayload),
                cancellationToken: cancellationToken);

        var json = await responseMessage.GetStringAsync();
        var jsonNode = json.AsNode()
            .Select("data");

        return jsonNode.GetEnumerable().Select(ParseResource);
    }

    public async Task<IEnumerable<CurseforgeResource>> GetFeaturedResourcesAsync(CancellationToken cancellationToken = default)
    {
        var request = CreateRequest("mods", "featured");
        var payload = new CurseforgeFeaturedRequestPayload(432, [0]);

        using var responseMessage = await request.PostAsync(JsonContent.Create(payload,
            CurseforgeRequestPayloadContext.Default.CurseforgeFeaturedRequestPayload),
                cancellationToken: cancellationToken);

        var jsonNode = (await responseMessage.GetStringAsync())
            .AsNode()
            .Select("data");

        var popular = jsonNode.GetEnumerable("popular");
        var featured = jsonNode.GetEnumerable("featured");

        IEnumerable<JsonNode> resources;
        if (popular is not null && featured is not null)
            resources = popular.Union(featured);
        else
            return [];

        return resources.Select(ParseResource);
    }

    /*
    public async Task<CurseForgeSearchResult> SearchResourcesAsync(
        string searchFilter,
        int classId = 6,
        int category = 0,
        string gameVersion = null,
        ModLoaderType modLoaderType = ModLoaderType.Any,
        CancellationToken cancellationToken = default)
    {
        var url = new Url(CurseforgeApi)
            .AppendPathSegment("mods/search")
            .SetQueryParams(new
            {
                gameId = 432,
                sortField = "Featured",
                sortOrder = "desc",
                categoryId = category,
                classId,
                gameVersion,
                searchFilter = HttpUtility.UrlEncode(searchFilter)
            });

        if (modLoaderType != ModLoaderType.Any && modLoaderType != ModLoaderType.Unknown)
            url.SetQueryParam("modLoaderType", (int)modLoaderType);

        var json = await CreateRequest(url).GetStringAsync(cancellationToken: cancellationToken);
        var jsonNode = json.AsNode();

        if (jsonNode == null)
            return null;

        return ParseResult(jsonNode);
    }
    */

    public async Task<IEnumerable<CurseforgeCategoryEntry>> GetCategoriesAsync(CancellationToken cancellationToken = default)
    {
        using var reponseMessage = await CreateRequest("categories").GetAsync(cancellationToken: cancellationToken);
        var json = await reponseMessage.GetStringAsync();
        var jsonNode = json.AsNode()
            .Select("data");

        return jsonNode.GetEnumerable().Select(ParseCategory);
    }

    public async Task<CurseForgeSearchResult> SearchResourcesAsync(
        CurseforgeSearchOptions searchOptions,
        CancellationToken cancellationToken = default)
    {
        var url = new Url(CurseforgeApi)
            .AppendPathSegment("mods/search")
            .SetQueryParams(new
            {
                gameId = 432,
                sortOrder = searchOptions.SortOrder is SortOrder.Desc ? "desc" : "asc",
                categoryId = searchOptions.CategoryId,
                sortField = searchOptions.SortField,
                classId = (int)searchOptions.ClassId,
                gameVersion = searchOptions.GameVersion,
                pageSize = searchOptions.PageSize,
                index = searchOptions.Index,
                searchFilter = HttpUtility.UrlEncode(searchOptions.SearchFilter)
            });

        var modLoaderType = searchOptions.ModLoaderType;
        if (modLoaderType != ModLoaderType.Any && modLoaderType != ModLoaderType.Unknown)
            url.SetQueryParam("modLoaderType", (int)modLoaderType);

        var json = await CreateRequest(url).GetStringAsync(cancellationToken: cancellationToken);
        var jsonNode = json.AsNode();

        if (jsonNode == null)
            return null;

        return ParseResult(jsonNode);
    }

    #region Private and internals

    internal async Task<(IEnumerable<CurseforgeResourceFile> files, int totalCount)> GetFirstPageWithTotalCountAsync(int modId, int pageSize, CancellationToken cancellationToken)
    {
        var url = new Url(CurseforgeApi)
                .AppendPathSegments("mods", modId.ToString(), "files")
                .SetQueryParams(new
                {
                    index = 0,
                    pageSize
                });

        var request = CreateRequest(url);
        var response = await request.GetStringAsync(cancellationToken: cancellationToken);

        var jsonNode = response.AsNode();

        // 获取分页信息
        var paginationNode = jsonNode.Select("pagination");
        int totalCount = paginationNode?.GetInt32("totalCount") ?? 0;

        // 获取文件数据
        var dataNode = jsonNode.Select("data");
        IEnumerable<CurseforgeResourceFile> files = [];

        if (dataNode != null)
        {
            var fileNodes = dataNode.GetEnumerable();
            if (fileNodes != null)
            {
                files = fileNodes.Select(ParseFile);
            }
        }

        return (files, totalCount);
    }

    internal async Task ProcessRemainingPagesAsync(int modId, int totalPages, int delayBetweenRequests, List<CurseforgeResourceFile> allFiles, CancellationToken cancellationToken = default, int pageSize = 50, int maxRequests = 16)
    {
        // 从第二页开始（第一页已经获取）
        var remainingPages = Enumerable.Range(1, totalPages - 1);

        // 使用 SemaphoreSlim 控制并发请求数
        var semaphore = new SemaphoreSlim(maxRequests); // 最多同时8个请求

        var tasks = remainingPages.Select(async pageIndex =>
        {
            await semaphore.WaitAsync(cancellationToken);
            // 添加延迟
            if (delayBetweenRequests > 0)
            {
                await Task.Delay(delayBetweenRequests, cancellationToken);
            }

            var pageFiles = await GetResourceFilesPageAsync(modId, pageIndex * pageSize, pageSize, cancellationToken);

            if (pageFiles != null && pageFiles.Any())
            {
                lock (allFiles)
                {
                    allFiles.AddRange(pageFiles);
                }
            }
            semaphore.Release();
        });

        await Task.WhenAll(tasks);
    }

    internal async Task<IEnumerable<CurseforgeResourceFile>> GetResourceFilesPageAsync(int modId, int pageIndex, int pageSize, CancellationToken cancellationToken, int maxRetries = 3)
    {
        var url = new Url(CurseforgeApi)
                    .AppendPathSegments("mods", modId.ToString(), "files")
                    .SetQueryParams(new
                    {
                        index = pageIndex,
                        pageSize
                    });

        var request = CreateRequest(url);
        var response = await request.GetStringAsync(cancellationToken: cancellationToken);

        var jsonNode = response.AsNode().Select("data").GetEnumerable();
        return jsonNode.Select(ParseFile);
    }

    internal static async Task<JsonNode> GetModFileEntryAsync(long modId, long fileId, CancellationToken cancellationToken = default)
    {
        CheckApiKey();

        string json = string.Empty;
        try
        {
            using var responseMessage = await CreateRequest("mods", "files", $"{fileId}")
                .GetAsync(cancellationToken: cancellationToken); ;

            json = await responseMessage.GetStringAsync();
        }
        catch (Exception) { }

        return json?.AsNode()?.Select("data") ??
            throw new InvalidModpackFileException();
    }

    internal static async Task<string> GetModDownloadUrlAsync(long modId, long fileId, CancellationToken cancellationToken = default)
    {
        CheckApiKey();

        string json = string.Empty;
        try
        {
            using var responseMessage = await CreateRequest("mods", $"{modId}", "files", $"{fileId}", "download-url")
                .GetAsync(cancellationToken: cancellationToken);

            json = await responseMessage.GetStringAsync();
        }
        catch (FlurlHttpException ex)
        {
            if (ex.StatusCode is 403)
                return string.Empty;
        }

        return json?.AsNode()?.GetString("data")
            ?? throw new InvalidModpackFileException();
    }

    internal static async Task<string> TestDownloadUrlAsync(long fileId, string fileName, CancellationToken cancellationToken = default)
    {
        CheckApiKey();

        var fileIdStr = fileId.ToString();
        List<string> urls = [
            $"https://edge.forgecdn.net/files/{fileIdStr[..4]}/{fileIdStr[4..]}/{fileName}",
            $"https://mediafiles.forgecdn.net/files/{fileIdStr[..4]}/{fileIdStr[4..]}/{fileName}"
        ];

        try
        {
            foreach (var url in urls)
            {
                var response = await HttpUtil.Request(url)
                    .HeadAsync(cancellationToken: cancellationToken);

                if (!response.ResponseMessage.IsSuccessStatusCode)
                    continue;

                return url;
            }
        }
        catch (Exception) { }

        throw new InvalidOperationException();
    }

    private static CurseforgeResource ParseResource(JsonNode node)
    {
        return new CurseforgeResource
        {
            Id = node.GetInt32("id"),
            ClassId = node.GetInt32("classId"),
            DownloadCount = node.GetInt32("downloadCount"),
            Name = node.GetString("name"),
            Slug = node.GetString("slug"),
            Summary = node.GetString("summary"),
            DateModified = node.GetDateTime("dateModified"),
            IconUrl = node.Select("logo").GetString("thumbnailUrl"),
            WebsiteUrl = node.Select("links").GetString("websiteUrl"),
            Authors = node.GetEnumerable<string>("authors", "name"),
            Categories = node.GetEnumerable<string>("categories", "name"),
            Screenshots = node.GetEnumerable<string>("screenshots", "url"),
            LatestFiles = node.GetEnumerable("latestFiles").Select(ParseFile),
            MinecraftVersions = node.GetEnumerable<string>("latestFilesIndexes", "gameVersion").Distinct(),
            Loaders = node.GetEnumerable<int>("latestFilesIndexes", "modLoader").Distinct().Select(x => (ModLoaderType)x)
        };
    }

    private static CurseForgeSearchResult ParseResult(JsonNode node)
    {
        var pagination = node.Select("pagination");
        var data = node.Select("data");
        return new CurseForgeSearchResult
        {
            Index = pagination.GetInt32("index"),
            PageSize = pagination.GetInt32("pageSize"),
            TotalCount = pagination.GetInt64("totalCount").Value,
            Resources = data.GetEnumerable().Select(ParseResource)
        };
    }

    private static CurseforgeCategoryEntry ParseCategory(JsonNode node)
    {
        return new CurseforgeCategoryEntry()
        {
            Id = node.GetInt32("id"),
            Name = node.GetString("name"),
            ClassId = (ClassId)node.GetInt32("classId")
        };
    }

    private static CurseforgeResourceFile ParseFile(JsonNode node)
    {
        var gGameVersions = node.GetEnumerable<string>("gameVersions").Where(x => x != "Client" && x != "Server");
        List<ModLoaderType> loaders = [];
        List<string> gameVersions = [];
        foreach (var ver in gGameVersions)
        {
            if (Enum.TryParse<ModLoaderType>(ver, out var loader))
            {
                loaders.Add(loader);
            }
            else
            {
                gameVersions.Add(ver);
            }
        }
        return node is null ? null : new CurseforgeResourceFile
        {
            Id = node.GetInt32("id"),
            ModId = node.GetInt32("modId"),
            GameId = node.GetInt32("gameId"),
            FileName = node.GetString("fileName"),
            Published = node.GetDateTime("fileDate"),
            IsAvailable = node.GetBool("isAvailable"),
            DisplayName = node.GetString("displayName"),
            IsServerPack = node.GetBool("isServerPack"),
            DownloadUrl = node.GetString("downloadUrl"),
            DownloadCount = node.GetInt32("downloadCount"),
            AlternateFileId = node.GetInt32("alternateFileId"),
            FileFingerprint = node.GetUInt32("fileFingerprint"),
            GameVersions = gameVersions,
            Loaders = loaders,
            IsApproved = node.GetInt32("fileStatus") is 4,
            FileSize = node.GetInt64("fileLength").Value,
            ReleaseType = (FileReleaseType)node.GetInt32("releaseType"),
            Sha1 = node.GetEnumerable("hashes").FirstOrDefault(x => x.GetInt32("algo") == 1)?.GetString("value"),
            Dependencies = node.GetEnumerable("dependencies").DistinctBy(x => x.GetInt32("modId")).ToDictionary(x => x.GetInt32("modId"), x => (DependencyType)x.GetInt32("relationType"))
        };
    }

    private static IFlurlRequest CreateRequest(Url url)
    {
        CheckApiKey();

        return HttpUtil.Request(url)
            .WithHeader("x-api-key", DownloadManager.CurseforgeApiKey);
    }

    private static IFlurlRequest CreateRequest(params string[] path)
    {
        CheckApiKey();

        return HttpUtil.Request(CurseforgeApi, path)
            .WithHeader("x-api-key", DownloadManager.CurseforgeApiKey);
    }

    private static void CheckApiKey()
    {
        if (string.IsNullOrWhiteSpace(DownloadManager.CurseforgeApiKey))
            throw new InvalidOperationException("Curseforge API key is not set.");
    }

    #endregion Private and internals
}

[Serializable]
public class InvalidModpackFileException : Exception
{
    public long ProjectId { get; set; }

    public InvalidModpackFileException()
    { }

    public InvalidModpackFileException(string message) : base(message)
    {
    }

    public InvalidModpackFileException(string message, Exception inner) : base(message, inner)
    {
    }
}

internal record CurseforgeResourcesRequestPayload(long[] modIds);
internal record CurseforgeFingerprintsRequestPayload(uint[] fingerprints);
internal record CurseforgeFeaturedRequestPayload(int gameId, int[] excludedModIds, string gameVersionTypeId = null);

[JsonSerializable(typeof(CurseforgeFeaturedRequestPayload))]
[JsonSerializable(typeof(CurseforgeResourcesRequestPayload))]
[JsonSerializable(typeof(CurseforgeFingerprintsRequestPayload))]
internal sealed partial class CurseforgeRequestPayloadContext : JsonSerializerContext;