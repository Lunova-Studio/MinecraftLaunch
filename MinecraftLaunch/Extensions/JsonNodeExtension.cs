using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;

namespace MinecraftLaunch.Extensions;

public static partial class JsonNodeExtension
{
    public static TValue? GetValueOrDefault<TValue>(this JsonNode node, string propertyName) where TValue : struct
    {
        _ = node.TryGetValue<TValue>(propertyName, out var value);
        return value;
    }

    public static string FixJson(this string errorJson) => errorJson
        .FixJsonStringNewlines()
        .FixDuplicateEmptyKeys();

    public static string FixJsonStringNewlines(this string json) => JsonFixRegex().Replace(json, match =>
    {
        var value = match.Groups[1].Value;
        if (JsonFieldRegex().IsMatch(match.Value) || !value.Contains('\n') && !value.Contains('\r'))
            return match.Value;

        var fixedValue = value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\r\n", "\\n")
            .Replace("\n", "\\n")
            .Replace("\r", "\\n");

        return $"\"{fixedValue}\"";
    });

    public static string FixDuplicateEmptyKeys(this string json)
    {
        bool firstFound = false;
        return JsonDuplicateEmptyKeysRegex().Replace(json, match =>
        {
            if (!firstFound)
            {
                firstFound = true;
                return match.Value;
            }

            return string.Empty;
        });
    }

    public static string Serialize<T>(this T value, JsonTypeInfo<T> jsonType)
    {
        return JsonSerializer.Serialize(value, jsonType);
    }

    public static T Deserialize<T>(this string json, JsonTypeInfo<T> jsonType)
    {
        return JsonSerializer.Deserialize(json, jsonType);
    }

    public static JsonNode AsNode(this string json)
    {
        return JsonNode.Parse(json);
    }

    public static JsonArray AsArray(this IEnumerable<JsonNode> jsonNodes)
    {
        return [.. jsonNodes];
    }

    public static JsonNode Select(this JsonNode node, string name)
    {
        return node[name];
    }

    public static bool TryGetValue<T>(this JsonNode node, string name, out T value)
    {
        var cNode = node[name];
        var flag = cNode is not null;

        value = flag ? cNode.GetValue<T>() : default;
        return flag;
    }

    public static int GetInt32(this JsonNode node)
    {
        return node.GetValue<int>();
    }

    public static int GetInt32(this JsonNode node, string name)
    {
        return node.Select(name).GetValue<int>();
    }

    public static uint GetUInt32(this JsonNode node)
    {
        return node.GetValue<uint>();
    }

    public static uint GetUInt32(this JsonNode node, string name)
    {
        return node.Select(name).GetValue<uint>();
    }

    public static long? GetInt64(this JsonNode node)
    {
        return node?.GetValue<long>();
    }

    public static long? GetInt64(this JsonNode node, string name)
    {
        return node.Select(name)?.GetValue<long>();
    }

    public static bool GetBool(this JsonNode node)
    {
        return node.GetValue<bool>();
    }

    public static bool GetBool(this JsonNode node, string name)
    {
        return node.Select(name).GetValue<bool>();
    }

    public static string GetString(this JsonNode node)
    {
        return node?.GetValue<string>();
    }

    public static string GetString(this JsonNode node, string name)
    {
        return node.Select(name)?.GetValue<string>();
    }

    public static DateTime GetDateTime(this JsonNode node)
    {
        return node.GetValue<DateTime>();
    }

    public static DateTime GetDateTime(this JsonNode node, string name)
    {
        return node.Select(name).GetValue<DateTime>();
    }

    public static JsonArray GetEnumerable(this JsonNode node)
    {
        return node.AsArray();
    }

    public static JsonArray GetEnumerable(this JsonNode node, string name)
    {
        return node?.Select(name)?.AsArray();
    }

    public static IEnumerable<T> GetEnumerable<T>(this JsonNode node)
    {
        return node.AsArray()
            .Select(x => x.GetValue<T>());
    }

    public static IEnumerable<T> GetEnumerable<T>(this JsonNode node, string name)
    {
        return node.Select(name)
            .AsArray()
            ?.Select(x => x.GetValue<T>());
    }

    public static IEnumerable<T> GetEnumerable<T>(this JsonNode node, string name, string elementName)
    {
        return node.Select(name)
            .AsArray()?
            .Select(x =>
            {
                var child = x.Select(elementName);
                return child is JsonValue value
                    ? value.GetValue<T>()
                    : default;
            }).Where(v => v is not null)!;
    }

    [GeneratedRegex(@"^""[^""]*""\s*:")]
    private static partial Regex JsonFieldRegex();

    [GeneratedRegex("\"((?:[^\"\\\\]|\\\\.)*)\"", RegexOptions.Compiled)]
    private static partial Regex JsonFixRegex();

    [GeneratedRegex(@"(""\""\s*:\s*\""""\s*,?\s*)+")]
    private static partial Regex JsonDuplicateEmptyKeysRegex();
}