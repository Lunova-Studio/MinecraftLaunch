using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using System.Text.Json.Serialization;
using System.Text.Json;
using MinecraftLaunch.Base.Models.JsonConverter;

namespace MinecraftLaunch.Extensions;

public static class JsonNodeExtension {
    public static string Serialize<T>(this T value, JsonTypeInfo<T> jsonType) {
        return JsonSerializer.Serialize(value, jsonType);
    }

    public static T Deserialize<T>(this string json, JsonTypeInfo<T> jsonType) {
        return JsonSerializer.Deserialize(json, jsonType);
    }

    public static JsonNode AsNode(this string json) {
        return JsonNode.Parse(json);
    }

    public static JsonNode Select(this JsonNode node, string name) {
        return node[name];
    }

    public static bool TryGetValue<T>(this JsonNode node, string name, out T value) {
        var cNode = node[name];
        var flag = cNode is not null;

        value = flag ? cNode.GetValue<T>() : default;
        return flag;
    }

    public static int GetInt32(this JsonNode node) {
        return node.GetValue<int>();
    }

    public static int GetInt32(this JsonNode node, string name) {
        return node.Select(name).GetValue<int>();
    }

    public static long GetInt64(this JsonNode node) {
        return node.GetValue<long>();
    }

    public static long GetInt64(this JsonNode node, string name) {
        return node.Select(name).GetValue<long>();
    }

    public static bool GetBool(this JsonNode node) {
        return node.GetValue<bool>();
    }

    public static bool GetBool(this JsonNode node, string name) {
        return node.Select(name).GetValue<bool>();
    }

    public static string GetString(this JsonNode node) {
        return node?.GetValue<string>();
    }

    public static string GetString(this JsonNode node, string name) {
        return node.Select(name)?.GetValue<string>();
    }

    public static DateTime GetDateTime(this JsonNode node) {
        return node.GetValue<DateTime>();
    }

    public static DateTime GetDateTime(this JsonNode node, string name) {
        return node.Select(name).GetValue<DateTime>();
    }

    public static JsonArray GetEnumerable(this JsonNode node) {
        return node.AsArray();
    }

    public static JsonArray GetEnumerable(this JsonNode node, string name) {
        return node?.Select(name)?.AsArray();
    }

    public static IEnumerable<T> GetEnumerable<T>(this JsonNode node) {
        return node.AsArray()
            .Select(x => x.GetValue<T>());
    }

    public static IEnumerable<T> GetEnumerable<T>(this JsonNode node, string name) {
        return node.Select(name)
            .AsArray()
            .Select(x => x.GetValue<T>());
    }

    public static IEnumerable<T> GetEnumerable<T>(this JsonNode node, string name, string elementName) {
        return node.Select(name)
            .AsArray()
            .Select(x => x.Select(elementName).GetValue<T>());
    }
}