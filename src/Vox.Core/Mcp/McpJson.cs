using System.Text.Json.Nodes;

namespace Vox.Core.Mcp;

/// <summary>MCP 报文里 JsonNode 的取值小工具（AOT 友好：不依赖反射序列化）。</summary>
internal static class McpJson
{
    public static string? Str(JsonObject? obj, string key) =>
        obj?[key] is JsonValue value && value.TryGetValue<string>(out var s) ? s : null;

    public static bool Bool(JsonObject? obj, string key, bool fallback = false) =>
        obj?[key] is JsonValue value && value.TryGetValue<bool>(out var b) ? b : fallback;

    public static int Int(JsonObject? obj, string key, int fallback)
    {
        if (obj?[key] is not JsonValue value) return fallback;
        if (value.TryGetValue<int>(out var i)) return i;
        if (value.TryGetValue<long>(out var l) && l is >= int.MinValue and <= int.MaxValue) return (int)l;
        if (value.TryGetValue<double>(out var d) && d is >= int.MinValue and <= int.MaxValue) return (int)d;
        return fallback;
    }

    public static bool Has(JsonObject? obj, string key) => obj?.ContainsKey(key) == true;

    public static IReadOnlyList<string>? Strings(JsonObject? obj, string key)
    {
        if (obj?[key] is not JsonArray array) return null;
        var list = new List<string>();
        foreach (var node in array)
        {
            if (node is JsonValue value && value.TryGetValue<string>(out var s) && !string.IsNullOrWhiteSpace(s))
            {
                list.Add(s.Trim());
            }
        }
        return list;
    }

    public static JsonArray Array(IEnumerable<string> values)
    {
        var array = new JsonArray();
        foreach (var value in values) array.Add(value);
        return array;
    }

    public static JsonObject Meta(ArticleMeta meta) => new()
    {
        ["title"] = meta.Title,
        ["slug"] = meta.Slug,
        ["description"] = meta.Description,
        ["date"] = meta.Date == default ? null : meta.Date.ToString("yyyy-MM-dd HH:mm:ss"),
        ["lastMod"] = meta.LastMod?.ToString("yyyy-MM-dd HH:mm:ss"),
        ["cover"] = meta.Cover,
        ["categories"] = Array(meta.Categories),
        ["albums"] = Array(meta.Albums),
        ["tags"] = Array(meta.Tags),
        ["author"] = meta.Author,
        ["draft"] = meta.Draft,
    };
}
