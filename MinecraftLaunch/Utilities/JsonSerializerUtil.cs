using MinecraftLaunch.Base.Models.JsonConverter;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace MinecraftLaunch.Utilities;

public static class JsonSerializerUtil
{
    public static JsonSerializerOptions GetDefaultOptions()
    {
        var options = new JsonSerializerOptions
        {
            MaxDepth = 100,
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            Converters = {
                new DateTimeJsonConverter()
            },
        };

        return options;
    }
}