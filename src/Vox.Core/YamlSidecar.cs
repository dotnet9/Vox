using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Vox.Core;

/// <summary>
/// 文章元数据读写：支持两种载体——sidecar yml（旧）与 md 内联 front matter（规范形态，Hugo/Jekyll 事实标准）。
/// 读：sidecar 存在优先，否则解析 md 内联块；写：一律写内联 front matter（合并式单文件形态）。
/// 未知字段（自定义键）进 ArticleMeta.Extra 原样往返；banner/copyright 为已知可选字段；
/// albums 已废弃但保留序列化（非空才写出），存量数据不丢。
/// </summary>
public static partial class YamlSidecar
{
    public static readonly string Date_format = "yyyy-MM-dd HH:mm:ss";

    // 闭合 --- 允许位于文件尾（后无换行）与尾随空白；前置空行仍按严格口径拒绝（Hugo/Jekyll 同款边界）。
    private static readonly Regex FrontMatterRegex = new("(?s)^---[ \\t]*\\r?\\n(.*?)\\r?\\n---[ \\t]*(?:\\r?\\n|$)", RegexOptions.Compiled);

    /// <summary>匹配 Emitter 输出尾部的文档结束标记「...」（独占一行）。</summary>
    [GeneratedRegex("\r?\\n\\.\\.\\.\\s*$")]
    private static partial Regex DocumentEndRegex();

    /// <summary>从 md 文本中切出内联 front matter：无则 yaml 为空、body 为原文。
    /// 命中后仍校验块内容为「非空键值映射」——无 front matter 但正文以 --- 水平线开头的文件
    /// （首行标题会被当 YAML 注释）不得误切，否则保存即吃掉首行内容。</summary>
    public static bool TrySplitFrontMatter(string markdown, out string yaml, out string body)
    {
        var match = FrontMatterRegex.Match(markdown);
        if (match.Success && LooksLikeFrontMatter(match.Groups[1].Value))
        {
            yaml = match.Groups[1].Value;
            body = markdown[match.Length..];
            return true;
        }
        yaml = string.Empty;
        body = markdown;
        return false;
    }

    private static bool LooksLikeFrontMatter(string candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate)) return false; // 空/纯注释块：视为无 front matter，正文原样保留
        try
        {
            var stream = new YamlStream();
            stream.Load(new StringReader(candidate));
            return stream.Documents.Count > 0 && stream.Documents[0].RootNode is YamlMappingNode { Children.Count: > 0 };
        }
        catch
        {
            return false;
        }
    }

    /// <summary>读文章元数据：sidecar yml 优先，其次 md 内联 front matter；都没有返回默认空 meta。</summary>
    public static ArticleMeta LoadFromMarkdown(string markdownPath)
    {
        var sidecarPath = SidecarPathFor(markdownPath);
        if (File.Exists(sidecarPath))
        {
            return Load(sidecarPath);
        }
        var text = File.ReadAllText(markdownPath);
        var (yaml, _) = SplitStored(markdownPath, text);
        return ParseOrThrow(yaml);
    }

    public static bool TryLoadFromMarkdown(string markdownPath, out ArticleMeta? meta, out string? error)
    {
        try
        {
            meta = LoadFromMarkdown(markdownPath);
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            meta = null;
            error = ex.Message;
            return false;
        }
    }

    /// <summary>把元数据写为 md 内联 front matter（已有块则替换，无则前置），正文保持不变。
    /// 内联即规范形态：遗留同名 .yml 会因读取侧「sidecar 优先」遮蔽内联块（编辑看似丢失），写入后一并移除。</summary>
    public static void SaveToMarkdown(string markdownPath, ArticleMeta meta)
    {
        meta.Normalize();
        var text = File.Exists(markdownPath) ? File.ReadAllText(markdownPath) : string.Empty;
        TrySplitFrontMatter(text, out _, out var body);
        File.WriteAllText(markdownPath, ComposeMarkdown(meta, body), new UTF8Encoding(false));
        var legacySidecar = SidecarPathFor(markdownPath);
        if (File.Exists(legacySidecar)) File.Delete(legacySidecar);
    }

    /// <summary>读改写：载入元数据 → mutate → 写回内联 front matter。</summary>
    public static string RewriteMarkdown(string markdownPath, Func<ArticleMeta, ArticleMeta> mutate)
    {
        var meta = LoadFromMarkdown(markdownPath);
        var next = mutate(meta);
        SaveToMarkdown(markdownPath, next);
        return Serialize(next);
    }

    /// <summary>元数据 + 正文合并为完整 md 文本：---\n{yaml}\n---\n\n{body}（与历史写盘格式一致）。
    /// 正文只剥前导换行不剥空白：首行 4 空格缩进代码块的前导缩进是 markdown 语义，TrimStart() 会毁掉它。</summary>
    public static string ComposeMarkdown(ArticleMeta meta, string body) =>
        $"---\n{Serialize(meta).TrimEnd()}\n---\n\n{body.TrimStart('\r', '\n')}";

    /// <summary>
    /// 序列化元数据为 YAML 文本（无 front matter 包裹）。已知键显式构造节点，避免 NativeAOT 反射激活，
    /// 未知键按原节点（含原引号风格）追加在尾部，往返保真。
    /// </summary>
    public static string Serialize(ArticleMeta meta)
    {
        meta.Normalize();
        var root = new YamlMappingNode();
        AddString("title", meta.Title);
        AddString("slug", meta.Slug);
        AddString("description", meta.Description);
        root.Add("date", (meta.Date == default ? DateTime.Now : meta.Date).ToString(Date_format, CultureInfo.InvariantCulture));
        if (meta.LastMod is { } lastMod) root.Add("lastmod", lastMod.ToString(Date_format, CultureInfo.InvariantCulture));
        AddString("cover", meta.Cover ?? string.Empty);
        AddList("categories", meta.Categories);
        AddList("albums", meta.Albums);
        AddList("tags", meta.Tags);
        AddString("author", meta.Author);
        root.Add("draft", meta.Draft ? "true" : "false");
        if (meta.Banner is { } banner) root.Add("banner", banner ? "true" : "false");
        AddString("copyright", meta.Copyright);
        foreach (var extra in meta.Extra)
        {
            if (extra.Node is { } node && !string.IsNullOrWhiteSpace(extra.Key))
            {
                root.Add(new YamlScalarNode(extra.Key), node);
            }
        }
        var merged = new YamlStream(new YamlDocument(root));
        using var writer = new StringWriter();
        // assignAnchors:false —— RepresentationModel 默认在保存阶段给「值相等」的节点（如 date/lastmod 同值、
        // banner/draft 同为 false）分配进程随机锚 id，每次保存都变 → 全文件 diff 噪音，违背「往返保真」。
        merged.Save(writer, false);
        // 去掉 Emitter 输出的文档结束标记「...」（front matter 内不需要，且会污染与手工维护文件的 diff）
        var text = writer.ToString();
        text = DocumentEndRegex().Replace(text, string.Empty);
        return text.TrimEnd() + Environment.NewLine;

        void AddString(string key, string? value)
        {
            if (value is not null) root.Add(new YamlScalarNode(key), StringNode(value));
        }

        void AddList(string key, List<string> values)
        {
            if (values.Count > 0) root.Add(new YamlScalarNode(key), new YamlSequenceNode(values.Select(StringNode)));
        }
    }

    private static YamlScalarNode StringNode(string value) => new(value)
    {
        // 这些文本不能被其他 YAML 消费者误读为 null、布尔值、数字或日期。
        Style = value.Length == 0 || value is "~" || value.Equals("null", StringComparison.OrdinalIgnoreCase)
            || IsBooleanText(value) || double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _)
            || DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
            ? ScalarStyle.DoubleQuoted : ScalarStyle.Any
    };

    /// <summary>
    /// 解析 front matter YAML：已知键映射类型化字段，未知键收进 Extra；解析失败返回 false（error 含原因），
    /// 调用方（元数据页签）据黄条警示且不回填表单。
    /// </summary>
    public static bool TryParseFrontMatter(string yaml, out ArticleMeta meta, out string? error)
    {
        meta = new ArticleMeta { Title = string.Empty, Slug = string.Empty };
        error = null;
        if (string.IsNullOrWhiteSpace(yaml))
        {
            meta.Normalize();
            return true;
        }
        try
        {
            var stream = new YamlStream();
            stream.Load(new StringReader(yaml));
            if (stream.Documents.Count != 1 || stream.Documents[0].RootNode is not YamlMappingNode root)
                throw new FormatException("Front matter must contain one YAML mapping.");
            foreach (var (keyNode, node) in root.Children)
            {
                var key = keyNode is YamlScalarNode scalar ? scalar.Value ?? string.Empty : keyNode.ToString();
                switch (key)
                {
                    case "title": meta.Title = ReadScalar(node, key) ?? string.Empty; break;
                    case "slug": meta.Slug = ReadScalar(node, key) ?? string.Empty; break;
                    case "description": meta.Description = ReadScalar(node, key); break;
                    case "date": meta.Date = ReadDate(node, key) ?? default; break;
                    case "lastmod": meta.LastMod = ReadDate(node, key); break;
                    case "cover": meta.Cover = ReadScalar(node, key); break;
                    case "categories": meta.Categories = ReadList(node, key); break;
                    case "albums": meta.Albums = ReadList(node, key); break;
                    case "tags": meta.Tags = ReadList(node, key); break;
                    case "author": meta.Author = ReadScalar(node, key) ?? string.Empty; break;
                    case "draft": meta.Draft = ReadBoolean(node, key) ?? false; break;
                    case "banner": meta.Banner = ReadBoolean(node, key); break;
                    case "copyright": meta.Copyright = ReadScalar(node, key); break;
                    default: meta.Extra.Add(new MetaExtraField(key, node)); break;
                }
            }
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
        meta.Normalize();
        return true;
    }

    /// <summary>比较两份元数据是否一致（忽略 lastmod——保存时按差异自动刷新）：用于「编辑过才重排 front matter」判定。</summary>
    public static bool MetadataEquals(ArticleMeta a, ArticleMeta b)
    {
        a.Normalize();
        b.Normalize();
        return string.Equals(a.Title, b.Title, StringComparison.Ordinal)
            && string.Equals(a.Slug, b.Slug, StringComparison.Ordinal)
            && string.Equals(a.Description, b.Description, StringComparison.Ordinal)
            && a.Date.Equals(b.Date)
            && string.Equals(a.Cover, b.Cover, StringComparison.Ordinal)
            && a.Categories.SequenceEqual(b.Categories, StringComparer.Ordinal)
            && a.Albums.SequenceEqual(b.Albums, StringComparer.Ordinal)
            && a.Tags.SequenceEqual(b.Tags, StringComparer.Ordinal)
            && string.Equals(a.Author, b.Author, StringComparison.Ordinal)
            && a.Draft == b.Draft
            && a.Banner == b.Banner
            && string.Equals(a.Copyright, b.Copyright, StringComparison.Ordinal)
            && ExtraEquals(a.Extra, b.Extra);
    }

    private static bool ExtraEquals(List<MetaExtraField> a, List<MetaExtraField> b)
    {
        if (a.Count != b.Count) return false;
        for (var i = 0; i < a.Count; i++)
        {
            if (!string.Equals(a[i].Key, b[i].Key, StringComparison.Ordinal)) return false;
            if (!Equals(a[i].Node, b[i].Node)) return false; // YamlNode.Equals 为深度值比较
        }
        return true;
    }

    private static string? ReadScalar(YamlNode node, string key)
    {
        if (node is not YamlScalarNode scalar) throw new FormatException($"{key} must be a scalar.");
        var value = scalar.Value;
        return scalar.Style is ScalarStyle.Plain or ScalarStyle.Any
            && (string.IsNullOrEmpty(value) || value == "~" || value.Equals("null", StringComparison.OrdinalIgnoreCase))
            ? null : value;
    }

    private static DateTime? ReadDate(YamlNode node, string key) => ReadScalar(node, key) is { } value
        ? DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind) : null;

    private static bool IsBooleanText(string value) => value.ToLowerInvariant() is "true" or "false" or "yes" or "no" or "on" or "off";

    private static bool? ReadBoolean(YamlNode node, string key) => ReadScalar(node, key)?.ToLowerInvariant() switch
    {
        null => null,
        "true" or "yes" or "on" => true,
        "false" or "no" or "off" => false,
        _ => throw new FormatException($"{key} must be a boolean.")
    };

    private static List<string> ReadList(YamlNode node, string key)
    {
        IEnumerable<YamlNode> nodes = node is YamlSequenceNode sequence ? sequence.Children : [node];
        return nodes.Select(item => ReadScalar(item, key)?.Trim()).Where(value => !string.IsNullOrEmpty(value))
            .Select(value => value!).ToList();
    }

    /// <summary>解析失败时抛出（供扫描报告错误）；重复键与非法字段不得被回退解析静默吞掉。</summary>
    private static ArticleMeta ParseOrThrow(string yaml)
    {
        if (yaml.Length == 0)
        {
            return new ArticleMeta { Title = string.Empty, Slug = string.Empty };
        }
        if (TryParseFrontMatter(yaml, out var meta, out var error))
        {
            return meta;
        }
        throw new InvalidOperationException(error ?? "front matter parse failed");
    }

    // ---------- 旧 sidecar 形态（读取兼容保留） ----------

    public static ArticleMeta Load(string path)
    {
        var text = File.ReadAllText(path);
        return ParseOrThrow(text);
    }

    public static bool TryLoad(string path, out ArticleMeta? meta, out string? error)
    {
        try
        {
            meta = Load(path);
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            meta = null;
            error = ex.Message;
            return false;
        }
    }

    private static string SidecarPathFor(string markdownPath) =>
        Path.ChangeExtension(markdownPath, ".yml");

    /// <summary>按存储形态切分：sidecar 存在时 yaml 取自 sidecar、body 为原 md（可能含内联块，调用方按需剥离）；否则内联切分。</summary>
    private static (string Yaml, string Body) SplitStored(string markdownPath, string markdownText)
    {
        if (File.Exists(SidecarPathFor(markdownPath)))
        {
            return (File.ReadAllText(SidecarPathFor(markdownPath)), markdownText);
        }
        TrySplitFrontMatter(markdownText, out var yaml, out var body);
        return (yaml, body);
    }

}

public static class ArticleMetaExtensions
{
    /// <summary>读入后兜底：空集合非 null、Trim 各标量，保证规则引擎面对规范形态。author 不默认填充（缺 author 是体检项）。</summary>
    public static void Normalize(this ArticleMeta meta)
    {
        meta.Title = meta.Title?.Trim() ?? string.Empty;
        meta.Slug = meta.Slug?.Trim() ?? string.Empty;
        meta.Description = meta.Description?.Trim();
        meta.Cover = string.IsNullOrWhiteSpace(meta.Cover) ? null : meta.Cover!.Trim();
        meta.Author = meta.Author?.Trim() ?? string.Empty;
        meta.Copyright = string.IsNullOrWhiteSpace(meta.Copyright) ? null : meta.Copyright!.Trim();
        meta.Categories ??= [];
        meta.Albums ??= [];
        meta.Tags ??= [];
        meta.Extra ??= [];
    }
}
