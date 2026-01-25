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

namespace MinecraftLaunch.Components.Provider;

public sealed class ModrinthProvider
{
    public readonly string ModrinthApi = "https://api.modrinth.com/v2";

    public async Task<IEnumerable<ModrinthResourceFile>> GetModFilesByHashAsync(
        string[] hashes,
        string version,
        ModLoaderType modLoaderType,
        HashType type = HashType.SHA1,
        CancellationToken cancellationToken = default)
    {
        var url = new Url(ModrinthApi)
            .AppendPathSegments("version_files", "update");

        var request = HttpUtil.Request(url);
        var payload = new ModrinthFilesUpdateCheckRequestPayload(hashes,
            [version],
            [modLoaderType switch {
                ModLoaderType.Quilt => "quilt",
                ModLoaderType.Forge => "forge",
                ModLoaderType.Fabric => "fabric",
                ModLoaderType.NeoForge => "neoforge",
                _ => "",
            }], type is HashType.SHA1 ? "sha1" : "sha512");

        using var responseMessage = await request.PostAsync(JsonContent.Create(payload,
            ModrinthProviderContext.Default.ModrinthFilesUpdateCheckRequestPayload),
                cancellationToken: cancellationToken);

        var jsonNode = (await responseMessage.GetStringAsync())
            .AsNode();

        return hashes.Select(x => ParseFile(jsonNode.Select(x)))
            .Where(x => x is not null);
    }

    public async Task<IEnumerable<ModrinthResource>> GetFeaturedResourcesAsync(CancellationToken cancellationToken = default)
    {
        var request = HttpUtil.Request(ModrinthApi, "search");

        var json = await request.GetStringAsync(cancellationToken: cancellationToken);
        var jsonNode = json.AsNode();

        if (jsonNode is null)
            return [];

        return jsonNode.GetEnumerable("hits").Select(x => ParseResource(x));
    }

    public async Task<IEnumerable<ModrinthResourceFile>> GetModFilesByProjectIdAsync(string projectId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(projectId);

        var request = HttpUtil.Request(ModrinthApi, "project", projectId, "version");

        var json = await request.GetStringAsync(cancellationToken: cancellationToken);
        var jsonArray = json.AsNode().AsArray();

        if (jsonArray is null)
            return null;

        return jsonArray.Select(ParseFile);
    }

    public async Task<ModrinthResource> SearchByProjectIdAsync(string projectId, CancellationToken cancellationToken = default)
    {
        var url = new Url(ModrinthApi)
            .AppendPathSegments("project", projectId);

        var request = HttpUtil.Request(url);
        var responseMessage = await request.GetStringAsync(cancellationToken: cancellationToken);
        return ParseResource(responseMessage.AsNode());
    }

    public async Task<IEnumerable<ModrinthResource>> SearchByProjectIdsAsync(IEnumerable<string> projectIds, CancellationToken cancellationToken = default)
    {
        var idsJson = projectIds.Serialize(ModrinthProviderContext.Default.IEnumerableString);

        var url = new Url(ModrinthApi).AppendPathSegment("projects")
            .AppendQueryParam("ids", idsJson, true);

        var request = HttpUtil.Request(url);
        var responseMessage = await request.GetStringAsync(cancellationToken: cancellationToken);
        var jsonNode = responseMessage.AsNode();

        return jsonNode.GetEnumerable().Select(x => ParseResource(x, true));
    }

    public async Task<IEnumerable<ModrinthResource>> SearchByUserAsync(string user, CancellationToken cancellationToken = default)
    {
        var request = HttpUtil.Request(ModrinthApi, "user", user, "projects");

        var json = await request.GetStringAsync(cancellationToken: cancellationToken);
        var jsonNode = json.AsNode();

        if (jsonNode is null)
            return [];

        return jsonNode.GetEnumerable().Select(x =>
        {
            var resource = ParseResource(x, true);
            resource.Author = user;
            return resource;
        });
    }

    /*
    public async Task<ModrinthSearchResult> SearchAsync(
        string searchFilter,
        string version = "",
        string category = "",
        string projectType = "mod",
        ModLoaderType modLoader = ModLoaderType.Any,
        ModrinthSearchIndex index = ModrinthSearchIndex.Relevance,
        int limit = 10,
        int offset = 0,
        CancellationToken cancellationToken = default)
    {
        List<List<string>> facetsList = [[$"project_type:{projectType}"]];

        if (!string.IsNullOrEmpty(version))
            facetsList.Add([$"versions:{version}"]);

        // 构建 categories
        var categories = new List<string>();
        if (!string.IsNullOrEmpty(category))
            categories.Add($"categories:{category}");

        if (modLoader is not ModLoaderType.Any)
        {
            var loaderCategory = modLoader switch
            {
                ModLoaderType.Quilt => "quilt",
                ModLoaderType.Forge => "forge",
                ModLoaderType.Fabric => "fabric",
                ModLoaderType.NeoForge => "neoforge",
                _ => throw new ArgumentOutOfRangeException(nameof(modLoader), modLoader, null)
            };

            categories.Add($"categories:{loaderCategory}");
        }

        if (categories.Count > 0)
            facetsList.Add(categories);

        var facets = facetsList.Serialize(ModrinthProviderContext.Default.ListListString);

        // 构建 URL
        var url = new Url(ModrinthApi)
            .AppendPathSegment("search")
            .SetQueryParams(new
            {
                query = searchFilter,
                facets,
                index = index switch
                {
                    ModrinthSearchIndex.Follows => "follows",
                    ModrinthSearchIndex.Downloads => "downloads",
                    ModrinthSearchIndex.Relevance => "relevance",
                    ModrinthSearchIndex.DateUpdated => "updated",
                    ModrinthSearchIndex.DatePublished => "newest",
                    _ => "relevance"
                },
                limit,
                offset
            });

        var request = HttpUtil.Request(url);

        var json = await request.GetStringAsync(cancellationToken: cancellationToken);
        var jsonNode = json.AsNode();

        return ParseResult(jsonNode);
    }
    */

    public async Task<ModrinthSearchResult> SearchAsync(
        ModrinthSearchOptions searchOptions,
        CancellationToken cancellationToken = default)
    {
        List<List<string>> facetsList = [[$"project_type:{searchOptions.ProjectType}"]];

        if (!string.IsNullOrEmpty(searchOptions.Version))
            facetsList.Add([$"versions:{searchOptions.Version}"]);

        // 构建 categories
        var categories = new List<string>();
        if (!string.IsNullOrEmpty(searchOptions.Category))
            categories.Add($"categories:{searchOptions.Category}");

        if (searchOptions.ModLoader is not ModLoaderType.Any)
        {
            var loaderCategory = searchOptions.ModLoader switch
            {
                ModLoaderType.Quilt => "quilt",
                ModLoaderType.Forge => "forge",
                ModLoaderType.Fabric => "fabric",
                ModLoaderType.NeoForge => "neoforge",
                _ => throw new ArgumentOutOfRangeException(nameof(searchOptions.ModLoader), searchOptions.ModLoader, null)
            };

            categories.Add($"categories:{loaderCategory}");
        }

        if (categories.Count > 0)
            facetsList.Add(categories);

        var facets = facetsList.Serialize(ModrinthProviderContext.Default.ListListString);

        // 构建 URL
        var url = new Url(ModrinthApi)
            .AppendPathSegment("search")
            .SetQueryParams(new
            {
                query = searchOptions.SearchFilter,
                facets,
                index = searchOptions.Index switch
                {
                    ModrinthSearchIndex.Follows => "follows",
                    ModrinthSearchIndex.Downloads => "downloads",
                    ModrinthSearchIndex.Relevance => "relevance",
                    ModrinthSearchIndex.DateUpdated => "updated",
                    ModrinthSearchIndex.DatePublished => "newest",
                    _ => "relevance"
                },
                limit = searchOptions.Limit,
                offset = searchOptions.Offset
            });

        var request = HttpUtil.Request(url);

        var json = await request.GetStringAsync(cancellationToken: cancellationToken);
        var jsonNode = json.AsNode();

        return ParseResult(jsonNode);
    }

    public async Task<IEnumerable<ModrinthCategoryEntry>> GetCategories(CancellationToken cancellationToken = default)
    {
        var request = HttpUtil.Request(ModrinthApi, "tag", "category");

        var json = await request.GetStringAsync(cancellationToken: cancellationToken);
        var jsonNode = json.AsNode();

        if (jsonNode is null)
            return [];

        return jsonNode.GetEnumerable().Select(ParseCategory);
    }

    #region Private

    private static ModrinthResource ParseResource(JsonNode jsonNode, bool isDetail = false)
    {
        var gCategories = jsonNode.GetEnumerable<string>("categories");
        List<string> categories = [];
        List<ModLoaderType> loaders = [];
        foreach (var category in gCategories)
        {
            if (Enum.TryParse<ModLoaderType>(category, true, out var loader))
            {
                loaders.Add(loader);
            }
            else
            {
                categories.Add(category);
            }
        }

        var projectType = jsonNode.GetString("project_type");
        var slug = jsonNode.GetString("slug");
        return new ModrinthResource
        {
            Slug = slug,
            Name = jsonNode.GetString("title"),
            ProjectId = jsonNode.GetString("project_id"),
            Author = jsonNode.GetString("author"),
            IconUrl = jsonNode.GetString("icon_url"),
            WebsiteUrl = $"https://modrinth.com/{projectType}/{slug}",
            Summary = jsonNode.GetString("description"),
            ProjectType = projectType,
            DownloadCount = jsonNode.GetInt32("downloads"),
            Categories = categories,
            Screenshots = isDetail
                ? jsonNode?.GetEnumerable<string>("gallery", "url")
                : jsonNode?.GetEnumerable<string>("gallery"),
            MinecraftVersions = isDetail
                ? jsonNode?.GetEnumerable<string>("game_versions")
                : jsonNode?.GetEnumerable<string>("versions"),
            Updated = jsonNode.TryGetValue<DateTime>("date_modified", out var updated)
                ? updated
                : jsonNode.GetDateTime("updated"),
            DateModified = jsonNode.TryGetValue<DateTime>("date_created", out var published)
                ? published
                : jsonNode.GetDateTime("published"),
            Loaders = loaders
        };
    }

    private static ModrinthCategoryEntry ParseCategory(JsonNode node)
    {
        return new()
        {
            Name = node.GetString("name"),
            ProjectType = node.GetString("project_type")
        };
    }

    private static ModrinthSearchResult ParseResult(JsonNode node)
    {
        return new ModrinthSearchResult
        {
            Index = node.GetInt32("offset"),
            PageSize = node.GetInt32("limit"),
            TotalCount = node.GetInt32("total_hits"),
            Resources = node.GetEnumerable("hits").Select(x => ParseResource(x))
        };
    }

    private static ModrinthResourceFile ParseFile(JsonNode node)
    {
        var file = node.GetEnumerable("files");
        var primaryFileNode = file.FirstOrDefault(x => x.GetBool("primary")) ?? file.FirstOrDefault();

        return new()
        {
            VersionId = node.GetString("id"),
            AuthorId = node.GetString("author_id"),
            ProjectId = node.GetString("project_id"),
            Published = node.GetDateTime("date_published"),
            DownloadCount = node.GetInt64("downloads").Value,

            DisplayName = node.GetString("name"),
            ChangeLog = node.GetString("changelog"),
            VersionNumber = node.GetString("version_number"),
            GameVersions = node.GetEnumerable<string>("game_versions"),

            DownloadUrl = primaryFileNode.GetString("url"),
            IsPrimary = primaryFileNode.GetBool("primary"),
            FileName = primaryFileNode.GetString("filename"),
            FileSize = primaryFileNode.GetInt64("size").Value,
            Sha1 = primaryFileNode.Select("hashes").GetString("sha1"),
            Sha512 = primaryFileNode.Select("hashes").GetString("sha512"),

            ReleaseType = node.GetString("version_type") switch
            {
                "release" => FileReleaseType.Release,
                "beta" => FileReleaseType.Beta,
                "alpha" => FileReleaseType.Alpha,
                _ => throw new NotImplementedException()
            },

            Dependencies = node.GetEnumerable("dependencies").Select(x => new ModrinthFileDependency
            {
                FileName = x.GetString("file_name"),
                VersionId = x.GetString("version_id"),
                ProjectId = x.GetString("project_id"),
                Type = x.GetString("dependency_type") switch
                {
                    "required" => DependencyType.Required,
                    "optional" => DependencyType.Optional,
                    "incompatible" => DependencyType.Incompatible,
                    "embedded" => DependencyType.Embedded,
                    _ => throw new NotImplementedException()
                }
            }),

            Loaders = node.GetEnumerable<string>("loaders").Select(x => x switch
            {
                "fabric" => ModLoaderType.Fabric,
                "forge" => ModLoaderType.Forge,
                "quilt" => ModLoaderType.Quilt,
                "neoforge" => ModLoaderType.NeoForge,
                _ => ModLoaderType.Any
            })
        };
    }

    #endregion Private
}

internal record ModrinthFilesUpdateCheckRequestPayload(string[] hashes, string[] game_versions, string[] loaders, string algorithm = "sha1");

[JsonSerializable(typeof(List<List<string>>))]
[JsonSerializable(typeof(IEnumerable<string>))]
[JsonSerializable(typeof(ModrinthFilesUpdateCheckRequestPayload))]
internal sealed partial class ModrinthProviderContext : JsonSerializerContext;