using System.Text;
using System.Text.RegularExpressions;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Vox.Core;

/// <summary>
/// 文章元数据读写：支持两种载体——sidecar yml（旧）与 md 内联 front matter（规范形态，Hugo/Jekyll 事实标准）。
/// 读：sidecar 存在优先，否则解析 md 内联块；写：一律写内联 front matter（合并式单文件形态）。
/// </summary>
public static partial class YamlSidecar
{
    public static readonly string Date_format = "yyyy-MM-dd HH:mm:ss";

    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    private static readonly ISerializer Serializer = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull | DefaultValuesHandling.OmitEmptyCollections)
        .Build();

    private static readonly Regex FrontMatterRegex = new("(?s)^---\r?\n(.*?)\r?\n---\r?\n", RegexOptions.Compiled);

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
        var meta = yaml.Length == 0
            ? new ArticleMeta { Title = string.Empty, Slug = string.Empty }
            : Deserializer.Deserialize<ArticleMeta>(yaml) ?? new ArticleMeta { Title = string.Empty, Slug = string.Empty };
        meta.Normalize();
        return meta;
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

    /// <summary>把元数据写为 md 内联 front matter（已有块则替换，无则前置），正文保持不变。</summary>
    public static void SaveToMarkdown(string markdownPath, ArticleMeta meta)
    {
        meta.Normalize();
        var text = File.Exists(markdownPath) ? File.ReadAllText(markdownPath) : string.Empty;
        TrySplitFrontMatter(text, out _, out var body);
        File.WriteAllText(markdownPath, SerializeInline(meta, body), new UTF8Encoding(false));
    }

    /// <summary>读改写：载入元数据 → mutate → 写回内联 front matter。</summary>
    public static string RewriteMarkdown(string markdownPath, Func<ArticleMeta, ArticleMeta> mutate)
    {
        var meta = LoadFromMarkdown(markdownPath);
        var next = mutate(meta);
        SaveToMarkdown(markdownPath, next);
        return Serialize(next);
    }

    /// <summary>序列化元数据为 YAML 文本（无 front matter 包裹）。</summary>
    public static string Serialize(ArticleMeta meta)
    {
        meta.Normalize();
        return Serializer.Serialize(new SidecarFields(meta)).TrimEnd() + Environment.NewLine;
    }

    // ---------- 旧 sidecar 形态（读取兼容保留） ----------

    public static ArticleMeta Load(string path)
    {
        var text = File.ReadAllText(path);
        var meta = Deserializer.Deserialize<ArticleMeta>(text) ?? new ArticleMeta { Title = string.Empty, Slug = string.Empty };
        meta.Normalize();
        return meta;
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

    private static string SerializeInline(ArticleMeta meta, string body)
    {
        var front = Serializer.Serialize(new SidecarFields(meta)).TrimEnd();
        return $"---\n{front}\n---\n\n{body.TrimStart()}";
    }

    /// <summary>与 yml 字段一一对应的投影：日期按固定格式输出，空 cover 输出空串而非省略。</summary>
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
        meta.Categories ??= [];
        meta.Albums ??= [];
        meta.Tags ??= [];
    }
}
