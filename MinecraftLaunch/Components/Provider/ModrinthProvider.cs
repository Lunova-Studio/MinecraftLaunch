using Flurl;
using Flurl.Http;
using MinecraftLaunch.Base.Enums;
using MinecraftLaunch.Base.Models.Network;
using MinecraftLaunch.Extensions;
using MinecraftLaunch.Utilities;
using System.Collections.Generic;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace MinecraftLaunch.Components.Provider;

public sealed class ModrinthProvider {
    public readonly string ModrinthApi = "https://api.modrinth.com/v2";

    public async Task<IEnumerable<ModrinthResourceFiles>> GetModFilesBySha1Async(
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

        var jsonNode = (await responseMessage.GetStringAsync())
            .AsNode();

        return hashes.Select(x => ParseFile(jsonNode.Select(x)))
            .Where(x => x is not null);
    }

    public async Task<ModrinthResource> SearchByProjectIdAsync(string projectId, CancellationToken cancellationToken = default) {
        var url = new Url(ModrinthApi)
            .AppendPathSegments("project", projectId);

        var request = HttpUtil.Request(url);
        var responseMessage = await request.GetStringAsync(cancellationToken: cancellationToken);
        return Parse(responseMessage.AsNode());
    }

    public async Task<IEnumerable<ModrinthResource>> SearchByProjectIdsAsync(IEnumerable<string> projectIds, CancellationToken cancellationToken = default) {
        var idsJson = projectIds.Serialize(ModrinthProviderContext.Default.IEnumerableString);

        var url = new Url(ModrinthApi).AppendPathSegment("projects")
            .AppendQueryParam("ids", idsJson, true);

        var request = HttpUtil.Request(url);
        var responseMessage = await request.GetStringAsync(cancellationToken: cancellationToken);
        var jsonNode = responseMessage.AsNode();

        return jsonNode.GetEnumerable().Select(x => Parse(x, true));
    }

    public async Task<IEnumerable<ModrinthResource>> GetFeaturedResourcesAsync(CancellationToken cancellationToken = default) {
        var request = HttpUtil.Request(ModrinthApi, "search");

        var json = await request.GetStringAsync(cancellationToken: cancellationToken);
        var jsonNode = json.AsNode();

        if (jsonNode is null)
            return [];

        return jsonNode.GetEnumerable("hits").Select(x => Parse(x));
    }

    public async Task<IEnumerable<ModrinthResource>> SearchByUserAsync(string user, CancellationToken cancellationToken = default) {
        var request = HttpUtil.Request(ModrinthApi, "user", user, "projects");

        var json = await request.GetStringAsync(cancellationToken: cancellationToken);
        var jsonNode = json.AsNode();

        if (jsonNode is null)
            return [];

        return jsonNode.GetEnumerable().Select(x => {
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

        var json = await request.GetStringAsync(cancellationToken: cancellationToken);
        var jsonNode = json.AsNode();

        return jsonNode.GetEnumerable("hits").Select(x => Parse(x));
    }

    #region Private

    private static ModrinthResource Parse(JsonNode jsonNode, bool isDetail = false) {
        return new ModrinthResource {
            Id = jsonNode.GetString("id"),
            Slug = jsonNode.GetString("slug"),
            Name = jsonNode.GetString("title"),
            Author = jsonNode.GetString("author"),
            IconUrl = jsonNode.GetString("icon_url"),
            Summary = jsonNode.GetString("description"),
            ProjectType = jsonNode.GetString("project_type"),
            DownloadCount = jsonNode.GetInt32("downloads"),
            Categories = jsonNode.GetEnumerable<string>("categories"),
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
                : jsonNode.GetDateTime("published")
        };
    }

    private static ModrinthResourceFiles ParseFile(JsonNode node) {
        if (node == null) return null;

        return new ModrinthResourceFiles {
            Id = node.GetString("id"),
            ProjectId = node.GetString("project_id"),
            AuthorId = node.GetString("author_id"),
            DatePublished = node.GetDateTime("date_published"),
            Downloads = node.GetInt32("downloads"),
            ChangelogUrl = node.GetString("changelog_url"),
            Files = node.GetEnumerable("files").Select(j => new ModrinthResourceFile()
            {
                Hashes = new FileHashes()
                {
                    Sha512 = j["hashes"].GetString("sha512"),
                    Sha1 = j["hashes"].GetString("sha1")
                },
                Url = j.GetString("url"),
                FileName = j.GetString("filename"),
                Primary = j.GetBool("primary"),
                Size = j.GetInt32("size"),
                FileType = j.GetString("file_type") switch
                {
                    "required-resource-pack" => AdditionalFileType.RequiredResourcePack,
                    "optional-resource-pack" => AdditionalFileType.OptionalResourcePack,
                    _ => throw new NotImplementedException()
                }
            }),
            Name = node.GetString("name"),
            VersionNumber = node.GetString("version_number"),
            Changelog = node.GetString("changelog"),
            Dependencies = node.GetEnumerable("dependencies").Select(j => new ModrinthFileDependency()
            {
                DependencyType = j.GetString("dependency_type") switch
                {
                    "required" => DependencyType.Required,
                    "optional" => DependencyType.Optional,
                    "incompatible" => DependencyType.Incompatible,
                    "embedded" => DependencyType.Embedded,
                    _ => throw new NotImplementedException()
                },
                VersionId = j.GetString("version_id"),
                ProjectId = j.GetString("project_id"),
                FileName = j.GetString("file_name")
            }),
            GameVersions = node.GetEnumerable<string>("game_versions"),
            VersionType = node.GetString("version_type") switch
            {
                "release" => FileReleaseType.Release,
                "beta" => FileReleaseType.Beta,
                "alpha" => FileReleaseType.Alpha,
                _ => throw new NotImplementedException()
            },
            Loaders = node.GetEnumerable<string>("loaders"),
            Featured = node.GetBool("featured"),
            Status = node.GetString("status") switch
            {
                "listed" => ModrinthFileStatus.Listed,
                "archived" => ModrinthFileStatus.Archived,
                "draft" => ModrinthFileStatus.Draft,
                "unlisted" => ModrinthFileStatus.Unlisted,
                "scheduled" => ModrinthFileStatus.Scheduled,
                "unknown" => ModrinthFileStatus.Unknown,
                _ => throw new NotImplementedException()
            },
            RequestedStatus = node.GetString("requested_status") switch
            {
                "listed" => RequestedStatus.Listed,
                "archived" => RequestedStatus.Archived,
                "draft" => RequestedStatus.Draft,
                "unlisted" => RequestedStatus.Unlisted,
                _ => null
            }
        };
    }

    #endregion
}

internal record ModrinthFilesUpdateCheckRequestPayload(string[] hashes, string[] game_versions, string[] loaders, string algorithm = "sha1");

[JsonSerializable(typeof(List<List<string>>))]
[JsonSerializable(typeof(IEnumerable<string>))]
[JsonSerializable(typeof(ModrinthFilesUpdateCheckRequestPayload))]
internal sealed partial class ModrinthProviderContext : JsonSerializerContext;