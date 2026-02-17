using Flurl;
using Flurl.Http;
using MinecraftLaunch.Base.Enums;
using MinecraftLaunch.Base.Models.Network;
using MinecraftLaunch.Extensions;
using MinecraftLaunch.Utilities;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Web;
using MinecraftLaunch.Base.Models.SHA1;

namespace MinecraftLaunch.Components.Provider;

public sealed class CurseforgeProvider {
    public readonly static string CurseforgeApi = "https://api.curseforge.com/v1";

    public async Task<IDictionary<CurseforgeResourceFile, IEnumerable<CurseforgeResourceFile>>> GetResourceFilesByFingerprintsAsync(uint[] modFingerprints, CancellationToken cancellationToken = default) {
        var request = CreateRequest("fingerprints", "432");
        var payload = new CurseforgeFingerprintsRequestPayload(modFingerprints);

        using var responseMessage = await request.PostAsync(JsonContent.Create(payload,
            CurseforgeRequestPayloadContext.Default.CurseforgeFingerprintsRequestPayload),
                cancellationToken: cancellationToken);

        await using var stream = await responseMessage.GetStreamAsync();
        using var doc  = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var exactMatches = doc.RootElement.GetPropertyNullable("data"u8)?.GetPropertyNullable("exactMatches"u8);
        
        // 此处对API行为有变更,document会释放所以需要立即终结迭代器,不能延迟执行
        return exactMatches?
            .EnumerateObject()
            .ToDictionary(
                // key为null那本来也要抛出了,所以就不做检查
                keySelector: x => ParseFile(x.Value.GetProperty("file"u8)),
                // 这里需要立即终结迭代器,不能延迟
                elementSelector: IEnumerable<CurseforgeResourceFile> /*写明返回类型*/
                    (y) => ProvideResources(y.Value)
            );

        static CurseforgeResourceFile[] ProvideResources(JsonElement propertyElement)
        {
            var latestFilesElement = propertyElement.GetProperty("latestFiles"u8);
            var length = latestFilesElement.GetArrayLength();
            var result = new CurseforgeResourceFile[length];
            var offset = 0;
            foreach (var item in latestFilesElement.EnumerateArray())
            {
                result[offset] = ParseFile(item);
                offset++;
            }

            return result;
        }
    }

    public Task<IEnumerable<CurseforgeResource>> GetResourcesByModIdsAsync(IEnumerable<long> modIds,
        CancellationToken cancellationToken = default) =>
        /*转移到long[]重载*/GetResourcesByModIdsAsync([..modIds], cancellationToken);
    public async Task<IEnumerable<CurseforgeResource>> GetResourcesByModIdsAsync(long[] modIds, CancellationToken cancellationToken = default) {
        var request = CreateRequest("mods");
        var payload = new CurseforgeResourcesRequestPayload(modIds);

        using var responseMessage = await request.PostAsync(JsonContent.Create(payload,
            CurseforgeRequestPayloadContext.Default.CurseforgeResourcesRequestPayload),
                cancellationToken: cancellationToken);

        await using var stream = await responseMessage.GetStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var dataElement = doc.RootElement.GetProperty("data"u8);
        // 终结迭代器操作,Document不适用LINQ
        return dataElement.EnumerateArrayThenSelectToArray(Parse);
    }

    public async Task<IEnumerable<CurseforgeResource>> GetFeaturedResourcesAsync(CancellationToken cancellationToken = default) {
        var request = CreateRequest("mods", "featured");
        var payload = new CurseforgeFeaturedRequestPayload(432, [0]);

        using var responseMessage = await request.PostAsync(JsonContent.Create(payload,
            CurseforgeRequestPayloadContext.Default.CurseforgeFeaturedRequestPayload),
                cancellationToken: cancellationToken);
        await using var stream = await responseMessage.GetStreamAsync();
        using var doc  = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var dataElement = doc.RootElement.GetProperty("data"u8);

        var popular = dataElement.GetPropertyNullable("popular"u8);
        var featured = dataElement.GetPropertyNullable("featured"u8);

        
        if (popular is null || featured is null) return [];
        // 使用HashSet去重
        var length = popular.Value.GetArrayLength() + featured.Value.GetArrayLength();
        var source = new HashSet<CurseforgeResource>(length);
        foreach (var item in popular.Value.EnumerateArray())
        {
            source.Add(Parse(item));
        }
        foreach (var item in featured.Value.EnumerateArray())
        {
            source.Add(Parse(item));
        }

        return source;
    }

    public async Task<IEnumerable<CurseforgeResource>> SearchResourcesAsync(
        string searchFilter,
        int classId = 6,
        int category = 0,
        string gameVersion = null,
        ModLoaderType modLoaderType = ModLoaderType.Any,
        CancellationToken cancellationToken = default) {
        var url = new Url(CurseforgeApi)
            .AppendPathSegment("mods/search")
            .SetQueryParams(new {
                gameId = 432,
                sortField = "Featured",
                sortOrder = "desc",
                cid = category,
                classId,
                gameVersion,
                searchFilter = HttpUtility.UrlEncode(searchFilter)
            });

        if (modLoaderType != ModLoaderType.Any && modLoaderType != ModLoaderType.Unknown)
            url.SetQueryParam("modLoaderType", (int)modLoaderType);

        try
        {
            await using var stream = await CreateRequest(url).GetStreamAsync(cancellationToken: cancellationToken);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            return doc.RootElement.GetProperty("data"u8).EnumerateArrayThenSelectToArray(Parse);
        }
        // 这个只是为了和原API保持一致加的
        catch
        {
            return [];
        }
    }

    public async Task<IEnumerable<CurseforgeResource>> SearchResourcesAsync(
        CurseforgeSearchOptions searchOptions,
        CancellationToken cancellationToken = default) {

        var url = new Url(CurseforgeApi)
            .AppendPathSegment("mods/search")
            .SetQueryParams(new {
                gameId = 432,
                sortOrder = searchOptions.SortOrder is SortOrder.Desc ? "desc" : "asc",
                cid = searchOptions.CategoryId,
                sortField = searchOptions.SortField,
                classId = searchOptions.ClassId,
                gameVersion = searchOptions.GameVersion,
                searchFilter = HttpUtility.UrlEncode(searchOptions.SearchFilter)
            });

        var modLoaderType = searchOptions.ModLoaderType;
        if (modLoaderType != ModLoaderType.Any && modLoaderType != ModLoaderType.Unknown)
            url.SetQueryParam("modLoaderType", (int)modLoaderType);

        try
        {
            await using var stream = await CreateRequest(url).GetStreamAsync(cancellationToken: cancellationToken);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            return doc.RootElement.GetProperty("data"u8).EnumerateArrayThenSelectToArray(Parse);
        }
        // 这个只是为了和原API保持一致加的
        catch
        {
            return [];
        }
    }

    #region Private and internals

    internal static async Task<JsonElement> GetModFileEntryAsync(long modId, long fileId,
        CancellationToken cancellationToken = default)
    {
        CheckApiKey();

        try
        {
            using var responseMessage = await CreateRequest("mods", "files", $"{fileId}")
                .GetAsync(cancellationToken: cancellationToken);
            await using var json = await responseMessage.GetStreamAsync();
            using var doc = await JsonDocument.ParseAsync(json, cancellationToken: cancellationToken);
            return doc.RootElement.GetProperty("data").Clone();

        }
        catch (Exception e)
        {
            throw new InvalidModpackFileException("The modpack file could not be read.", e);
        }
               
    }

    internal static async Task<string> GetModDownloadUrlAsync(long modId, long fileId,
        CancellationToken cancellationToken = default)
    {
        CheckApiKey();


        using var responseMessage = await CreateRequest("mods", $"{modId}", "files", $"{fileId}", "download-url")
            .GetAsync(cancellationToken: cancellationToken);
        await using var stream = await responseMessage.GetStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        // get data
        if (!doc.RootElement.TryGetProperty("data", out var dataElement)) return string.Empty;
        return dataElement.GetString() ?? throw new InvalidModpackFileException();

    }

    internal static async Task<string> TestDownloadUrlAsync(long fileId, string fileName, CancellationToken cancellationToken = default) {
        CheckApiKey();

        var fileIdStr = fileId.ToString();
        List<string> urls = [
            $"https://edge.forgecdn.net/files/{fileIdStr[..4]}/{fileIdStr[4..]}/{fileName}",
            $"https://mediafiles.forgecdn.net/files/{fileIdStr[..4]}/{fileIdStr[4..]}/{fileName}"
        ];

        try {
            foreach (var url in urls) {
                var response = await HttpUtil.Request(url)
                    .HeadAsync(cancellationToken: cancellationToken);

                if (!response.ResponseMessage.IsSuccessStatusCode)
                    continue;

                return url;
            }
        } catch (Exception) { }

        throw new InvalidOperationException();
    }

    private static CurseforgeResource Parse(JsonElement resElement)
    {
        return new CurseforgeResource {
            Id = resElement.GetProperty("id"u8).GetInt32(),
            ClassId = resElement.GetProperty("classId"u8).GetInt32(),
            DownloadCount = resElement.GetProperty("downloadCount"u8).GetInt32(),
            Name = resElement.GetProperty("name"u8).GetString(),
            Slug = resElement.GetProperty("slug"u8).GetString(),
            Summary = resElement.GetProperty("summary"u8).GetString(),
            DateModified = resElement.GetProperty("dateModified"u8).GetDateTime(),
            IconUrl = resElement.GetProperty("logo"u8).GetProperty("thumbnailUrl"u8).GetString(),
            WebsiteUrl = resElement.GetProperty("links"u8).GetProperty("websiteUrl"u8).GetString(),
            Authors = resElement.GetProperty("authors"u8).EnumerateArrayThenSelectNotNullStringPropertyWithNotNullAndNotEmptyValueThenToArray("name"u8),
            Categories = resElement.GetProperty("categories"u8).EnumerateArrayThenSelectNotNullStringPropertyWithNotNullAndNotEmptyValueThenToArray("name"u8),
            Screenshots = resElement.GetProperty("screenshots"u8).EnumerateArrayThenSelectNotNullStringPropertyWithNotNullAndNotEmptyValueThenToArray("url"u8),
            LatestFiles = resElement.GetProperty("latestFiles"u8).EnumerateArrayThenSelectToArray(ParseFile),
            MinecraftVersions = resElement.GetProperty("latestFilesIndexes"u8)
                .EnumerateArrayThenSelectToArray(x => x.GetProperty("gameVersion"u8).GetString())
                .Distinct()
        };
        

        
    }

    private static CurseforgeResourceFile ParseFile(JsonElement node) {
        return new CurseforgeResourceFile {
            Id = node.GetProperty("id"u8).GetInt32(),
            ModId = node.GetProperty("modId"u8).GetInt32(),
            GameId = node.GetProperty("gameId"u8).GetInt32(),
            FileName = node.GetProperty("fileName"u8).GetString(),
            Published = node.GetProperty("fileDate"u8).GetDateTime(),
            IsAvailable = node.GetProperty("isAvailable"u8).GetBoolean(),
            DisplayName = node.GetProperty("displayName"u8).GetString(),
            IsServerPack = node.GetProperty("isServerPack"u8).GetBoolean(),
            DownloadUrl = node.GetProperty("downloadUrl"u8).GetString(),
            DownloadCount = node.GetProperty("downloadCount"u8).GetInt32(),
            AlternateFileId = node.GetProperty("alternateFileId"u8).GetInt32(),
            FileFingerprint = node.GetProperty("fileFingerprint"u8).GetUInt32(),
            GameVersions = node.GetProperty("gameVersions"u8).EnumerateArrayThenSelectToArray(static i=>i.GetString()),
            IsApproved = node.GetProperty("fileStatus"u8).GetInt32() is 4,
            FileLength = node.GetProperty("fileLength"u8).GetInt64(),
            ReleaseType = (FileReleaseType)node.GetProperty("releaseType"u8).GetInt32(),
            // 手写目的在于防止LINQ访问被释放的资源,必须全量加载
            Sha1 = ProvideSha(node.GetProperty("hashes"u8)),
            Dependencies = ProvideDependencies(node.GetProperty("dependencies"u8))
                
        };

        
        static Dictionary<int, DependencyType> ProvideDependencies(JsonElement dependenciesArrayElement)
        {
            return dependenciesArrayElement.EnumerateArray()
                .DistinctBy(x => x.GetProperty("modId"u8).GetInt32())
                .ToDictionary(
                    x => x.GetProperty("modId"u8).GetInt32(),
                    x => (DependencyType)x.GetProperty("relationType"u8).GetInt32()
                );
        }
        static Sha1Data? ProvideSha(JsonElement hashesArrayNode)
        {
            foreach (var node in hashesArrayNode.EnumerateArray())
            {
                
                if (!node.TryGetProperty("algo"u8,out var algoElement))continue;
                if (algoElement.GetInt32() is 1) return node.GetProperty("value"u8).Deserialize(Sha1Data.Sha1DataSerializerContext.Default.Sha1Data);
            }

            return null;
        }
    }

    private static IFlurlRequest CreateRequest(Url url) {
        CheckApiKey();

        return HttpUtil.Request(url)
            .WithHeader("x-api-key", DownloadManager.CurseforgeApiKey);
    }

    private static IFlurlRequest CreateRequest(params string[] path) {
        CheckApiKey();

        return HttpUtil.Request(CurseforgeApi, path)
            .WithHeader("x-api-key", DownloadManager.CurseforgeApiKey);
    }

    private static void CheckApiKey() {
        if (string.IsNullOrWhiteSpace(DownloadManager.CurseforgeApiKey))
            throw new InvalidOperationException("Curseforge API key is not set.");
    }

    #endregion
}

[Serializable]
public class InvalidModpackFileException : Exception {
    public long ProjectId { get; set; }

    public InvalidModpackFileException() { }
    public InvalidModpackFileException(string message) : base(message) { }
    public InvalidModpackFileException(string message, Exception inner) : base(message, inner) { }
}

internal record CurseforgeResourcesRequestPayload(long[] modIds);
internal record CurseforgeFingerprintsRequestPayload(uint[] fingerprints);
internal record CurseforgeFeaturedRequestPayload(int gameId, int[] excludedModIds, string gameVersionTypeId = null);

[JsonSerializable(typeof(CurseforgeFeaturedRequestPayload))]
[JsonSerializable(typeof(CurseforgeResourcesRequestPayload))]
[JsonSerializable(typeof(CurseforgeFingerprintsRequestPayload))]
internal sealed partial class CurseforgeRequestPayloadContext : JsonSerializerContext;