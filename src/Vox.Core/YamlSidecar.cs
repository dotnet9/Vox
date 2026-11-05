using System.Text;
using System.Text.RegularExpressions;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
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

    /// <summary>已知键集合：映射进 ArticleMeta 类型化属性；albums 虽已废弃仍按已知键处理（走类型化列表）。</summary>
    private static readonly HashSet<string> KnownKeys = new(StringComparer.Ordinal)
    {
        "title", "slug", "description", "date", "lastmod", "cover",
        "categories", "tags", "albums", "author", "draft", "banner", "copyright",
    };

    // lastmod 键名显式钉死：CamelCase 约定会把 LastMod 变成 lastMod，与仓库实际键「lastmod」不符（历史缺陷）。
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .WithAttributeOverride<ArticleMeta>(m => m.LastMod, new YamlDotNet.Serialization.YamlMemberAttribute { Alias = "lastmod" })
        .IgnoreUnmatchedProperties()
        .Build();

    private static readonly ISerializer Serializer = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .WithAttributeOverride<SidecarFields>(f => (object)f.LastMod!, new YamlDotNet.Serialization.YamlMemberAttribute { Alias = "lastmod" })
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull | DefaultValuesHandling.OmitEmptyCollections)
        .Build();

    private static readonly Regex FrontMatterRegex = new("(?s)^---\r?\n(.*?)\r?\n---\r?\n", RegexOptions.Compiled);

    /// <summary>匹配 Emitter 输出尾部的文档结束标记「...」（独占一行）。</summary>
    [GeneratedRegex("\r?\\n\\.\\.\\.\\s*$")]
    private static partial Regex DocumentEndRegex();

    /// <summary>从 md 文本中切出内联 front matter：无则 yaml 为空、body 为原文。</summary>
    public static bool TrySplitFrontMatter(string markdown, out string yaml, out string body)
    {
        var match = FrontMatterRegex.Match(markdown);
        if (match.Success)
        {
            yaml = match.Groups[1].Value;
            body = markdown[match.Length..];
            return true;
        }
        yaml = string.Empty;
        body = markdown;
        return false;
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

    /// <summary>元数据 + 正文合并为完整 md 文本：---\n{yaml}\n---\n\n{body}（与历史写盘格式一致）。</summary>
    public static string ComposeMarkdown(ArticleMeta meta, string body) =>
        $"---\n{Serialize(meta).TrimEnd()}\n---\n\n{body.TrimStart()}";

    /// <summary>
    /// 序列化元数据为 YAML 文本（无 front matter 包裹）。已知键经 ISerializer 产出（引号/转义口径与历史一致），
    /// 未知键按原节点（含原引号风格）追加在尾部，往返保真。
    /// </summary>
    public static string Serialize(ArticleMeta meta)
    {
        meta.Normalize();
        var known = Serializer.Serialize(new SidecarFields(meta)).TrimEnd();
        var stream = new YamlStream();
        stream.Load(new StringReader(known));
        if (stream.Documents.Count == 0 || stream.Documents[0].RootNode is not YamlMappingNode root
            || meta.Extra.Count == 0)
        {
            return known + Environment.NewLine;
        }
        var children = new List<KeyValuePair<YamlNode, YamlNode>>();
        foreach (var pair in root.Children)
        {
            children.Add(pair);
        }
        foreach (var extra in meta.Extra)
        {
            if (extra.Node is { } node && !string.IsNullOrWhiteSpace(extra.Key))
            {
                children.Add(new KeyValuePair<YamlNode, YamlNode>(new YamlScalarNode(extra.Key), node));
            }
        }
        var merged = new YamlStream(new YamlDocument(new YamlMappingNode(children)));
        using var writer = new StringWriter();
        merged.Save(writer);
        // 去掉 Emitter 输出的文档结束标记「...」（front matter 内不需要，且会污染与手工维护文件的 diff）
        var text = writer.ToString();
        text = DocumentEndRegex().Replace(text, string.Empty);
        return text.TrimEnd() + Environment.NewLine;
    }

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
            meta = Deserializer.Deserialize<ArticleMeta>(yaml) ?? new ArticleMeta { Title = string.Empty, Slug = string.Empty };
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
        try
        {
            meta.Extra = CollectExtra(yaml);
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

    /// <summary>抽取未知键（albums 是已废弃的已知键，走类型化属性，不进 Extra）：YamlStream 深度解析，节点原样保留。</summary>
    private static List<MetaExtraField> CollectExtra(string yaml)
    {
        var extras = new List<MetaExtraField>();
        var stream = new YamlStream();
        stream.Load(new StringReader(yaml));
        if (stream.Documents.Count == 0 || stream.Documents[0].RootNode is not YamlMappingNode root)
        {
            return extras;
        }
        foreach (var pair in root.Children)
        {
            var key = pair.Key is YamlScalarNode scalar ? scalar.Value ?? string.Empty : pair.Key.ToString();
            if (!KnownKeys.Contains(key))
            {
                extras.Add(new MetaExtraField(key, pair.Value));
            }
        }
        return extras;
    }

    /// <summary>解析失败时抛出（供 TryLoadFromMarkdown 捕获入扫描 Errors），成功则含 Extra。</summary>
    private static ArticleMeta ParseOrThrow(string yaml)
    {
        if (yaml.Length == 0)
        {
            return new ArticleMeta { Title = string.Empty, Slug = string.Empty };
        }
        if (TryParseFrontMatter(yaml, out var meta, out _))
        {
            return meta;
        }
        return Deserializer.Deserialize<ArticleMeta>(yaml)
            ?? new ArticleMeta { Title = string.Empty, Slug = string.Empty };
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

    /// <summary>与 yml 字段一一对应的投影：日期按固定格式输出，空 cover 输出空串而非省略；
    /// banner/copyright 仅在有意值时输出（copyright 留空 = 原创，键可缺席）。</summary>
    private sealed class SidecarFields
    {
        public string Title { get; }
        public string Slug { get; }
        public string? Description { get; }
        public string Date { get; }
        public string? LastMod { get; }
        public string Cover { get; }
        public List<string> Categories { get; }
        public List<string> Albums { get; }
        public List<string> Tags { get; }
        public string Author { get; }
        public bool Draft { get; }
        public bool? Banner { get; }
        public string? Copyright { get; }

        public SidecarFields(ArticleMeta meta)
        {
            Title = meta.Title;
            Slug = meta.Slug;
            Description = meta.Description;
            Date = meta.Date == default ? DateTime.Now.ToString(Date_format) : meta.Date.ToString(Date_format);
            LastMod = meta.LastMod?.ToString(Date_format);
            Cover = meta.Cover ?? string.Empty;
            Categories = meta.Categories;
            Albums = meta.Albums;
            Tags = meta.Tags;
            Author = meta.Author;
            Draft = meta.Draft;
            Banner = meta.Banner;
            Copyright = string.IsNullOrWhiteSpace(meta.Copyright) ? null : meta.Copyright!.Trim();
        }
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
