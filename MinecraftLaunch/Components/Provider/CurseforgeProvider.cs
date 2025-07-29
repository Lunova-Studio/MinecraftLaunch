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

public sealed class CurseforgeProvider {
    public readonly static string CurseforgeApi = "https://api.curseforge.com/v1";

    public async Task<IDictionary<CurseforgeResourceFile, IEnumerable<CurseforgeResourceFile>>> GetResourceFilesByFingerprintsAsync(uint[] modFingerprints, CancellationToken cancellationToken = default) {
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

    public async Task<IEnumerable<CurseforgeResource>> GetResourcesByModIdsAsync(IEnumerable<long> modIds, CancellationToken cancellationToken = default) {
        var request = CreateRequest("mods");
        var payload = new CurseforgeResourcesRequestPayload([.. modIds]);

        using var responseMessage = await request.PostAsync(JsonContent.Create(payload,
            CurseforgeRequestPayloadContext.Default.CurseforgeResourcesRequestPayload),
                cancellationToken: cancellationToken);

        var json = await responseMessage.GetStringAsync();
        var jsonNode = json.AsNode()
            .Select("data");

        return jsonNode.GetEnumerable().Select(Parse);
    }

    public async Task<IEnumerable<CurseforgeResource>> GetFeaturedResourcesAsync(CancellationToken cancellationToken = default) {
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

        return resources.Select(Parse);
    }

    public async Task<IEnumerable<CurseforgeResource>> SearchResourcesAsync(
        string searchFilter,
        int classId = 6,
        int category = 0,
        string gameVersion = null,
        ModLoaderType modLoaderType = ModLoaderType.Any) {
        var url = new Url(CurseforgeApi)
            .AppendPathSegment("mods/search")
            .SetQueryParams(new {
                gameId = 432,
                sortField = "Featured",
                sortOrder = "desc",
                categoryId = category,
                classId,
                gameVersion,
                searchFilter = HttpUtility.UrlEncode(searchFilter)
            });

        if (modLoaderType is not ModLoaderType.Any or ModLoaderType.Unknown)
            url.SetQueryParam("modLoaderType", (int)modLoaderType);

        var json = await CreateRequest(url).GetStringAsync();
        var jsonNode = json.AsNode();

        if (jsonNode == null)
            return [];

        return jsonNode.GetEnumerable("data").Select(Parse);
    }

    #region Private and internals

    internal static async Task<JsonNode> GetModFileEntryAsync(long modId, long fileId, CancellationToken cancellationToken = default) {
        CheckApiKey();

        string json = string.Empty;
        try {
            using var responseMessage = await CreateRequest("mods", "files", $"{fileId}")
                .GetAsync(cancellationToken: cancellationToken); ;

            json = await responseMessage.GetStringAsync();
        } catch (Exception) { }

        return json?.AsNode()?.Select("data") ??
            throw new InvalidModpackFileException();
    }

    internal static async Task<string> GetModDownloadUrlAsync(long modId, long fileId, CancellationToken cancellationToken = default) {
        CheckApiKey();

        string json = string.Empty;
        try {
            using var responseMessage = await CreateRequest("mods", $"{modId}", "files", $"{fileId}", "download-url")
                .GetAsync(cancellationToken: cancellationToken);

            json = await responseMessage.GetStringAsync();
        } catch (FlurlHttpException ex) {
            if (ex.StatusCode is 403)
                return string.Empty;
        }

        return json?.AsNode()?.GetString("data")
            ?? throw new InvalidModpackFileException();
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

    private static CurseforgeResource Parse(JsonNode node) {
        return new CurseforgeResource {
            Id = node.GetInt32("id"),
            ClassId = node.GetInt32("classId"),
            DownloadCount = node.GetInt32("downloadCount"),
            Name = node.GetString("name"),
            Summary = node.GetString("summary"),
            DateModified = node.GetDateTime("dateModified"),
            IconUrl = node.Select("logo").GetString("thumbnailUrl"),
            WebsiteUrl = node.Select("links").GetString("websiteUrl"),
            Authors = node.GetEnumerable<string>("authors", "name"),
            Categories = node.GetEnumerable<string>("categories", "name"),
            Screenshots = node.GetEnumerable<string>("screenshots", "url"),
            LatestFiles = node.GetEnumerable("latestFiles").Select(ParseFile),
            MinecraftVersions = node.GetEnumerable<string>("latestFilesIndexes", "gameVersion").Distinct()
        };

    }

    private static CurseforgeResourceFile ParseFile(JsonNode node) {
        if (node is null)
            return null;

        return new CurseforgeResourceFile {
            Id = node.GetInt32("id"),
            ModId = node.GetInt32("modId"),
            FileName = node.GetString("fileName"),
            Published = node.GetDateTime("fileDate"),
            IsAvailable = node.GetBool("isAvailable"),
            ReleaseType = node.GetInt32("releaseType"),
            DisplayName = node.GetString("displayName"),
            DownloadUrl = node.GetString("downloadUrl"),
            FileFingerprint = node.GetUInt32("fileFingerprint"),
            MinecraftVersions = node.GetEnumerable<string>("gameVersions")
        };
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