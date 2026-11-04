using System.Text.Json;

namespace Vox.Core;

/// <summary>site/categories.json 条目（真实形态：[{"Sort":1,"Name":".NET","Memo":"…","Slug":"dotnet"}]）。</summary>
public sealed record SiteCategory
{
    public int Sort { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Memo { get; init; }
    public string? Slug { get; init; }
}

/// <summary>
/// 分类受控字典：从内容仓库 site/categories.json 读取展示名（Name），按 Sort 升序——
/// 分类候选不硬编码在应用内，仓库改配置即生效。文件缺失或损坏返回空列表（面板提示无候选）。
/// </summary>
public static class SiteCategories
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static string PathFor(string repoRoot) =>
        Path.Combine(repoRoot, "site", "categories.json");

    public static IReadOnlyList<SiteCategory> Load(string repoRoot)
    {
        var file = PathFor(repoRoot);
        if (!File.Exists(file))
        {
            return [];
        }
        try
        {
            var json = File.ReadAllText(file);
            var items = JsonSerializer.Deserialize<List<SiteCategory>>(json, JsonOptions) ?? [];
            return items
                .Where(c => !string.IsNullOrWhiteSpace(c.Name))
                .OrderBy(c => c.Sort)
                .ThenBy(c => c.Name, StringComparer.Ordinal)
                .ToList();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>展示名列表（分类 chips 的受控候选）。</summary>
    public static IReadOnlyList<string> LoadDisplayNames(string repoRoot) =>
        Load(repoRoot).Select(c => c.Name).ToList();
}
