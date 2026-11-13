using YamlDotNet.Serialization;

namespace Vox.Core;

/// <summary>
/// 文档模板（模板中心）：内置模板随应用版本分发（src/Vox.Core/Templates/*.md 内嵌资源，内容依
/// GB/T 8567-2006 与 GB/T 28827.1-2022 章节结构），用户模板存用户模板目录（默认 %AppData%/Vox/templates，
/// 设置 TemplatesPath 可改）。展示分组由来源决定：内置按 front matter kind（dev/ops）归入软件研发/运维，
/// 用户目录模板一律归入「我的模板」——是否纳入版本管理由用户自行决定，程序不做任何 git 假设。
/// </summary>
public sealed record DocumentTemplate
{
    public required string Id { get; init; } // 文件名（不含扩展名），列表去重与排序键

    public required string Name { get; init; } // front matter title

    public required string Description { get; init; }

    public required string Standard { get; init; } // 依据标准/来源说明（如 GB/T 8567-2006 · SRS）

    /// <summary>dev / ops / my；未知值归一为 my（展示分组仍按来源优先）。</summary>
    public required string Kind { get; init; }

    public IReadOnlyList<string> Categories { get; init; } = [];

    public IReadOnlyList<string> Tags { get; init; } = [];

    public bool Draft { get; init; } = true;

    /// <summary>去掉 front matter 的正文（含 # 一级标题），预览与「使用此模板」的取材源。</summary>
    public required string Markdown { get; init; }

    public required bool IsBuiltin { get; init; }

    /// <summary>展示分组键：内置按 kind（dev/ops），用户模板一律 my。</summary>
    public string DisplayKind => IsBuiltin ? Kind switch { "ops" => "ops", _ => "dev" } : "my";

    private static readonly IDeserializer Yaml = new DeserializerBuilder()
        .IgnoreUnmatchedProperties()
        .WithNamingConvention(TemplateNamingConvention.Instance)
        .Build();

    /// <summary>
    /// 解析一份模板 md（front matter + 正文）。front matter 块或必填键（title/description/standard）
    /// 缺失抛 FormatException，由调用方按文件粒度兜底（用户目录坏文件跳过并提示，不拖垮整个目录）。
    /// </summary>
    public static DocumentTemplate Parse(string id, string content, bool isBuiltin)
    {
        var normalized = content.Replace("\r\n", "\n").Replace("\r", "\n");
        var match = System.Text.RegularExpressions.Regex.Match(
            normalized, @"^---\n([\s\S]*?)\n---\n", System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        if (!match.Success)
            throw new FormatException($"缺少 front matter（--- 块）：{id}");
        TemplateFrontMatter meta;
        try
        {
            meta = Yaml.Deserialize<TemplateFrontMatter>(match.Groups[1].Value);
        }
        catch (Exception ex)
        {
            throw new FormatException($"front matter 不是合法 YAML：{id}（{ex.Message}）");
        }
        if (string.IsNullOrWhiteSpace(meta.Title))
            throw new FormatException($"front matter 缺少 title：{id}");
        if (string.IsNullOrWhiteSpace(meta.Description))
            throw new FormatException($"front matter 缺少 description：{id}");
        if (string.IsNullOrWhiteSpace(meta.Standard))
            throw new FormatException($"front matter 缺少 standard：{id}");

        var body = normalized[(match.Index + match.Length)..].TrimStart();
        return new DocumentTemplate
        {
            Id = id,
            Name = meta.Title.Trim(),
            Description = meta.Description.Trim(),
            Standard = meta.Standard.Trim(),
            Kind = (meta.Kind ?? string.Empty).Trim() switch { "dev" => "dev", "ops" => "ops", _ => "my" },
            Categories = meta.Categories,
            Tags = meta.Tags,
            Draft = meta.Draft ?? true,
            Markdown = body,
            IsBuiltin = isBuiltin,
        };
    }

    /// <summary>
    /// 大纲骨架提取（「使用此模板」预填新建向导）：取二级标题行原文（保留「## 1. 引言」式手写序号），
    /// 三级及以下不算章节骨架。
    /// </summary>
    public static IReadOnlyList<string> ExtractOutline(string markdown)
    {
        var normalized = markdown.Replace("\r\n", "\n").Replace("\r", "\n");
        return normalized.Split('\n')
            .Where(line => line.StartsWith("## ", StringComparison.Ordinal))
            .Select(line => line.Trim())
            .ToList();
    }
}

/// <summary>模板 front matter DTO：internal 顶层类，键全小写（title/description/kind/standard/categories/tags/draft）。</summary>
internal sealed class TemplateFrontMatter
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string Standard { get; set; } = string.Empty;
    public List<string> Categories { get; set; } = [];
    public List<string> Tags { get; set; } = [];
    public bool? Draft { get; set; }
}

/// <summary>全小写命名约定：模板 front matter 键与属性名的映射（YamlDotNet 默认 PascalCase 精确匹配，实测小写键不回填）。</summary>
internal sealed class TemplateNamingConvention : INamingConvention
{
    public static readonly TemplateNamingConvention Instance = new();

    private TemplateNamingConvention()
    {
    }

    public string Apply(string name) => name.ToLowerInvariant();

    public string Reverse(string name) => name.ToLowerInvariant();

    public StringComparison Comparison => StringComparison.OrdinalIgnoreCase;
}
