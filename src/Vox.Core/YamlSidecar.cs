using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Vox.Core;

/// <summary>
/// slug.yml sidecar 读写：yml 细节收拢在此模块，外部只见中立的 <see cref="ArticleMeta"/>（决策 D4）。
/// 字段布局与 Assets.Dotnet9 存量一致（title/slug/description/date/lastmod/cover/categories/albums/tags/author/draft）。
/// </summary>
public static class YamlSidecar
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

    public static void Save(string path, ArticleMeta meta)
    {
        meta.Normalize();
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, Serialize(meta));
    }

    public static string Serialize(ArticleMeta meta)
    {
        meta.Normalize();
        var front = Serializer.Serialize(new SidecarFields(meta));
        return front.TrimEnd() + Environment.NewLine;
    }

    /// <summary>yml 文本 → 新文本（保留 YamlDotNet 规范化布局），供批量修复直接读改写。</summary>
    public static string Rewrite(string path, Func<ArticleMeta, ArticleMeta> mutate)
    {
        var meta = Load(path);
        var next = mutate(meta);
        Save(path, next);
        return Serialize(next);
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
    /// <summary>读入后兜底：空集合非 null、Trim 各标量，保证后续规则引擎面对的是规范形态。</summary>
    public static void Normalize(this ArticleMeta meta)
    {
        meta.Title = meta.Title?.Trim() ?? string.Empty;
        meta.Slug = meta.Slug?.Trim() ?? string.Empty;
        meta.Description = meta.Description?.Trim();
        meta.Cover = string.IsNullOrWhiteSpace(meta.Cover) ? null : meta.Cover!.Trim();
        meta.Author = string.IsNullOrWhiteSpace(meta.Author) ? "Dotnet9" : meta.Author!.Trim();
        meta.Categories ??= [];
        meta.Albums ??= [];
        meta.Tags ??= [];
    }
}
