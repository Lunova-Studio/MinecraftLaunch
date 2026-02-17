using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Flurl;
using Flurl.Http;
using MinecraftLaunch.Base.Enums;
using MinecraftLaunch.Base.Models.Network;
using MinecraftLaunch.Extensions;
using MinecraftLaunch.Utilities;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using MinecraftLaunch.Base.Models.SHA1;

namespace MinecraftLaunch.Components.Provider;

public sealed class ModrinthProvider {
    public readonly string ModrinthApi = "https://api.modrinth.com/v2";

    public async Task<IEnumerable<ModrinthResourceFile>> GetModFilesByHashAsync(
        string[] hashes,
        string version,
        ModLoaderType modLoaderType,
        HashType type = HashType.SHA1,
        CancellationToken cancellationToken = default) {
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

        await using var stream = await responseMessage.GetStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = doc.RootElement;
        var result = new ModrinthResourceFile[hashes.Length];
        for (var i = 0; i < hashes.Length; i++)
        {
            result[i] = ParseFile(root.GetProperty(hashes[i]));
        }
        return result;
    }

    public async Task<IEnumerable<ModrinthResource>> GetFeaturedResourcesAsync(CancellationToken cancellationToken = default) {
        var request = HttpUtil.Request(ModrinthApi, "search");

        await using var json = await request.GetStreamAsync(cancellationToken: cancellationToken);
        using var doc = await JsonDocument.ParseAsync(json, cancellationToken: cancellationToken);
        Debug.Assert(doc.RootElement.ValueKind == JsonValueKind.Object);
        return doc.RootElement.GetProperty("hits"u8).EnumerateArrayThenSelectToArray(static x => Parse(x));
    }

    public async Task<IEnumerable<ModrinthResourceFile>> GetModFilesByProjectIdAsync(string projectId, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrEmpty(projectId);

        var request = HttpUtil.Request(ModrinthApi, "project", projectId, "version");

        await using var json = await request.GetStreamAsync(cancellationToken: cancellationToken);
        using var jsonArray = await JsonDocument.ParseAsync(json, cancellationToken: cancellationToken);
        Debug.Assert(jsonArray.RootElement.ValueKind == JsonValueKind.Array);
        
        return jsonArray.RootElement.EnumerateArrayThenSelectToArray(ParseFile);
    }

    public async Task<ModrinthResource> SearchByProjectIdAsync(string projectId, CancellationToken cancellationToken = default) {
        var url = new Url(ModrinthApi)
            .AppendPathSegments("project", projectId);

        var request = HttpUtil.Request(url);
        await using var responseMessage = await request.GetStreamAsync(cancellationToken: cancellationToken);
        using var doc =  await JsonDocument.ParseAsync(responseMessage, cancellationToken: cancellationToken);
        return Parse(doc.RootElement);
    }

    public async Task<IEnumerable<ModrinthResource>> SearchByProjectIdsAsync(IEnumerable<string> projectIds, CancellationToken cancellationToken = default) {
        var idsJson = projectIds.Serialize(ModrinthProviderContext.Default.IEnumerableString);

        var url = new Url(ModrinthApi).AppendPathSegment("projects")
            .AppendQueryParam("ids", idsJson, true);

        var request = HttpUtil.Request(url);
        await using var responseMessage = await request.GetStreamAsync(cancellationToken: cancellationToken);
        using var doc = await JsonDocument.ParseAsync(responseMessage, cancellationToken: cancellationToken);
        return doc.RootElement.EnumerateArrayThenSelectToArray(static x => Parse(x, true));
    }

    public async Task<IEnumerable<ModrinthResource>> SearchByUserAsync(string user, CancellationToken cancellationToken = default) {
        var request = HttpUtil.Request(ModrinthApi, "user", user, "projects");

        await using var json = await request.GetStreamAsync(cancellationToken: cancellationToken);
        using var doc = await JsonDocument.ParseAsync(json, cancellationToken: cancellationToken);
        

        return doc.RootElement.EnumerateArrayThenSelectToArray(x => {
            var resource = Parse(x, true);
            resource.Author = user;
            return resource;
        });
    }

    public async Task<IEnumerable<ModrinthResource>> SearchAsync(
        string searchFilter,
        string version = "",
        string category = "",
        string projectType = "mod",
        ModLoaderType modLoader = ModLoaderType.Any,
        ModrinthSearchIndex index = ModrinthSearchIndex.Relevance,
        CancellationToken cancellationToken = default) {
        List<List<string>> facetsList = [[$"project_type:{projectType}"]];

        if (!string.IsNullOrEmpty(version))
            facetsList.Add([$"versions:{version}"]);

        // 构建 categories
        var categories = new List<string>();
        if (!string.IsNullOrEmpty(category))
            categories.Add($"categories:{category}");

        if (modLoader is not ModLoaderType.Any) {
            var loaderCategory = modLoader switch {
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
            .SetQueryParams(new {
                query = searchFilter,
                facets,
                index = index switch {
                    ModrinthSearchIndex.Follows => "follows",
                    ModrinthSearchIndex.Downloads => "downloads",
                    ModrinthSearchIndex.Relevance => "relevance",
                    ModrinthSearchIndex.DateUpdated => "updated",
                    ModrinthSearchIndex.DatePublished => "newest",
                    _ => "relevance"
                }
            });

        var request = HttpUtil.Request(url);

        await using var json = await request.GetStreamAsync(cancellationToken: cancellationToken);
        using var doc = await JsonDocument.ParseAsync(json, cancellationToken: cancellationToken);
        return doc.RootElement.GetProperty("hits"u8).EnumerateArrayThenSelectToArray(static x=>Parse(x));
    }

    #region Private

    private static ModrinthResource Parse(JsonElement jsonNode, bool isDetail = false) {
        Debug.Assert(jsonNode.ValueKind == JsonValueKind.Object);
        return new ModrinthResource {
            Slug = jsonNode.GetProperty("slug"u8).GetString(),
            Name = jsonNode.GetProperty("title"u8).GetString(),
            ProjectId = jsonNode.GetProperty("project_id"u8).GetString(),
            Author = jsonNode.TryGetProperty("author"u8, out var authorElement) ? authorElement.GetString() : null,
            IconUrl = jsonNode.GetProperty("icon_url"u8).GetString(),
            Summary = jsonNode.GetProperty("description"u8).GetString(),
            ProjectType = jsonNode.GetProperty("project_type"u8).GetString(),
            DownloadCount = jsonNode.GetProperty("downloads"u8).GetInt32(),
            Categories = jsonNode.GetProperty("categories"u8)
                .EnumerateArrayThenSelectToArray(x => x.GetString()),
            Screenshots = isDetail
                ? jsonNode.TryGetProperty("gallery"u8, out var galleryDetail)
                    ? galleryDetail.EnumerateArrayThenSelectToArray(static x => x.GetProperty("url"u8).GetString())
                    : null
                : jsonNode.TryGetProperty("gallery"u8, out var gallery)
                    ? gallery.EnumerateArrayThenSelectToArray(static x => x.GetString())
                    : null,
            MinecraftVersions = isDetail
                ? jsonNode.TryGetProperty("game_versions"u8, out var gameVersions)
                    ? gameVersions.EnumerateArrayThenSelectToArray(static x => x.GetString())
                    : null
                : jsonNode.TryGetProperty("versions"u8, out var versions)
                    ? versions.EnumerateArrayThenSelectToArray(static x => x.GetString())
                    : null,
            Updated = jsonNode.TryGetProperty("date_modified"u8, out var updatedEl) && updatedEl.TryGetDateTime(out var
                updated)
                ? updated
                : jsonNode.GetProperty("updated"u8).GetDateTime(),
            DateModified = jsonNode.TryGetProperty("date_created"u8, out var publishedEl) &&
                           publishedEl.TryGetDateTime(out var published)
                ? published
                : jsonNode.GetProperty("published"u8).GetDateTime()
        };
    }
    [return: NotNull]
    private static ModrinthResourceFile ParseFile(JsonElement node)
    {
        Debug.Assert(node.ValueKind == JsonValueKind.Object);
        var filesElement = node.GetProperty("files"u8);
        Debug.Assert(filesElement.ValueKind == JsonValueKind.Array);
        var primaryFileNode = filesElement[0];
        foreach (var item in filesElement.EnumerateArray())
        {
            if (!item.TryGetProperty("primary"u8, out var isPrimaryElement) || !isPrimaryElement.GetBoolean()) continue;
            primaryFileNode = item;
            break;
        }
        return new ModrinthResourceFile
        {
            VersionId = node.GetProperty("id"u8).GetString(),
            AuthorId = node.GetProperty("author_id"u8).GetString(),
            ProjectId = node.GetProperty("project_id"u8).GetString(),
            Published = node.GetProperty("date_published"u8).GetDateTime(),
            DownloadCount = node.GetProperty("downloads"u8).GetInt64(),

            DisplayName = node.GetProperty("name"u8).GetString(),
            ChangeLog = node.GetProperty("changelog"u8).GetString(),
            VersionNumber = node.GetProperty("version_number"u8).GetString(),
            MinecraftVersions = node.GetProperty("game_versions"u8)
                .EnumerateArrayThenSelectToArray(x => x.GetString()),

            DownloadUrl = primaryFileNode.GetProperty("url"u8).GetString(),
            IsPrimary = primaryFileNode.GetProperty("primary"u8).GetBoolean(),
            FileName = primaryFileNode.GetProperty("filename"u8).GetString(),
            FileSize = primaryFileNode.GetProperty("size"u8).GetInt64(),
            Sha1 = primaryFileNode.GetProperty("hashes"u8).GetProperty("sha1"u8).Deserialize(Sha1Data.Sha1DataSerializerContext.Default.Sha1Data),
            Sha512 = primaryFileNode.GetProperty("hashes"u8).GetProperty("sha512"u8).GetString(),

            ReleaseType = node.GetProperty("version_type"u8).GetString() switch
            {
                "release" => FileReleaseType.Release,
                "beta" => FileReleaseType.Beta,
                "alpha" => FileReleaseType.Alpha,
                _ => throw new NotImplementedException()
            },

            Dependencies = node.GetProperty("dependencies"u8).EnumerateArrayThenSelectToArray(x => new ModrinthFileDependency
            {
                FileName = x.GetProperty("file_name"u8).GetString(),
                VersionId = x.GetProperty("version_id"u8).GetString(),
                ProjectId = x.GetProperty("project_id"u8).GetString(),
                Type = x.GetProperty("dependency_type"u8).GetString() switch
                {
                    "required" => DependencyType.Required,
                    "optional" => DependencyType.Optional,
                    "incompatible" => DependencyType.Incompatible,
                    "embedded" => DependencyType.Embedded,
                    _ => throw new NotImplementedException()
                }
            }),

            ModLoaders = node.GetProperty("loaders"u8).EnumerateArrayThenSelectToArray(x => x.GetString() switch
            {
                "fabric" => ModLoaderType.Fabric,
                "forge" => ModLoaderType.Forge,
                "quilt" => ModLoaderType.Quilt,
                "neoforge" => ModLoaderType.NeoForge,
                _ => ModLoaderType.Any
            })
        };
    }

    #endregion
}

internal record ModrinthFilesUpdateCheckRequestPayload(string[] hashes, string[] game_versions, string[] loaders, string algorithm = "sha1");

[JsonSerializable(typeof(List<List<string>>))]
[JsonSerializable(typeof(IEnumerable<string>))]
[JsonSerializable(typeof(ModrinthFilesUpdateCheckRequestPayload))]
internal sealed partial class ModrinthProviderContext : JsonSerializerContext;
