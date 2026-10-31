using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Vox.Core;

/// <summary>发布通道。网站走 git；公众号/博客园走 API，CSDN 使用编辑器内部接口；知乎无公开 API 走复制助手。</summary>
public enum PublishChannel
{
    Site,
    WeChat,
    Cnblogs,
    Csdn,
    Zhihu,
}

/// <summary>平台发布状态（状态机：内容快照 hash 对比平台已发布快照）。</summary>
public enum PublishStatus
{
    /// <summary>已同步：平台已发布快照 = 本地内容快照。</summary>
    Ok,

    /// <summary>需更新：发布后原文又改过（hash 不一致）。</summary>
    Stale,

    /// <summary>未发布。</summary>
    Off,

    /// <summary>失败：上次发布出错（凭据/网络），附原因。</summary>
    Error,
}

/// <summary>单平台已发布快照。</summary>
public sealed class ChannelSnapshot
{
    public string Hash { get; set; } = string.Empty;
    public DateTime PublishedAt { get; set; }
    public string? Error { get; set; }
    public string? Note { get; set; }
}

/// <summary>发布状态库：&lt;%AppData%/Vox/publish-state.json&gt;，key1=文章仓库相对路径，key2=通道名。</summary>
public static class PublishStateStore
{

    public static string DefaultPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Vox", "publish-state.json");

    public static Dictionary<string, Dictionary<string, ChannelSnapshot>> Load(string? path = null)
    {
        var file = path ?? DefaultPath();
        if (!File.Exists(file)) return [];
        try
        {
            return JsonSerializer.Deserialize(File.ReadAllText(file),
                VoxJsonContext.Default.DictionaryStringDictionaryStringChannelSnapshot) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public static void Save(Dictionary<string, Dictionary<string, ChannelSnapshot>> states, string? path = null)
    {
        var file = path ?? DefaultPath();
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, JsonSerializer.Serialize(states, VoxJsonContext.Default.DictionaryStringDictionaryStringChannelSnapshot));
    }

    /// <summary>记录一次成功发布（快照 = 当前内容 hash）。</summary>
    public static void MarkPublished(
        Dictionary<string, Dictionary<string, ChannelSnapshot>> states,
        string articlePath, PublishChannel channel, string contentHash, string? note = null)
    {
        if (!states.TryGetValue(articlePath, out var article)) 
        {
            article = [];
            states[articlePath] = article;
        }
        article[channel.ToString()] = new ChannelSnapshot
        {
            Hash = contentHash,
            PublishedAt = DateTime.Now,
            Note = note,
        };
    }

    /// <summary>记录一次失败（保留旧快照供对比，Error 驱动 ✕ 状态）。</summary>
    public static void MarkError(
        Dictionary<string, Dictionary<string, ChannelSnapshot>> states,
        string articlePath, PublishChannel channel, string error)
    {
        if (!states.TryGetValue(articlePath, out var article))
        {
            article = [];
            states[articlePath] = article;
        }
        if (article.TryGetValue(channel.ToString(), out var old))
        {
            old.Error = error;
        }
        else
        {
            article[channel.ToString()] = new ChannelSnapshot { Error = error };
        }
    }

    /// <summary>内容快照：md 文件 SHA-256（front matter + 正文整体）。</summary>
    public static string ContentHash(string localFile)
    {
        using var sha = SHA256.Create();
        using var stream = File.OpenRead(localFile);
        return Convert.ToHexString(sha.ComputeHash(stream));
    }

    /// <summary>计算某文章在某平台的状态。站点通道特殊：git 工作区该文件干净 = Ok，有改动 = Stale。</summary>
    public static (PublishStatus Status, string Note) Evaluate(
        Dictionary<string, Dictionary<string, ChannelSnapshot>> states,
        string articlePath, PublishChannel channel, string currentHash,
        IReadOnlyCollection<string>? gitDirtyPaths = null)
    {
        if (channel == PublishChannel.Site)
        {
            if (gitDirtyPaths is { Count: > 0 } && gitDirtyPaths.Contains(articlePath))
            {
                return (PublishStatus.Stale, "本地修改未提交");
            }
            return (PublishStatus.Ok, string.Empty);
        }

        if (!states.TryGetValue(articlePath, out var article) ||
            !article.TryGetValue(channel.ToString(), out var snapshot))
        {
            return (PublishStatus.Off, "未发布");
        }
        if (!string.IsNullOrEmpty(snapshot.Error))
        {
            return (PublishStatus.Error, snapshot.Error);
        }
        if (snapshot.Hash != currentHash)
        {
            return (PublishStatus.Stale,
                $"{snapshot.PublishedAt:yyyy-MM-dd} 发布 · 原文已修改");
        }
        return (PublishStatus.Ok, $"{snapshot.PublishedAt:yyyy-MM-dd} 发布 · 已同步");
    }
}

/// <summary>发布前检查规则（发布对话框与发布中心「发布前检查」同源）。</summary>
public sealed record PublishCheckRule(string Key, int Count, string Detail);

public static class PublishChecks
{
    /// <summary>全库巡检：待推送提交 / 失效图片引用 / 缺 author / 草稿 / 无封面简述。</summary>
    public static IReadOnlyList<PublishCheckRule> Evaluate(RepoScan? scan, int? aheadBy = null)
    {
        var rules = new List<PublishCheckRule>();
        rules.Add(new PublishCheckRule("Ahead", aheadBy is > 0 ? aheadBy.Value : 0, "git 提交未推送（推完网站即更新）"));

        if (scan != null)
        {
            var dead = scan.Articles
                .SelectMany(a => a.ImageReferences)
                .Select(p => scan.FindImage(p))
                .Count(img => img is null || img.IsMissing);
            rules.Add(new PublishCheckRule("DeadLink", dead, "引用的图片文件不存在"));

            var noAuthor = scan.Articles.Count(a => string.IsNullOrWhiteSpace(a.Meta.Author));
            rules.Add(new PublishCheckRule("Author", noAuthor, "站点渲染默认站长（信息项，可批量补全）"));

            var drafts = scan.Articles.Count(a => a.IsDraft);
            rules.Add(new PublishCheckRule("Draft", drafts, "draft: true 的文章（写作页可切换）"));

            var noCover = scan.Articles.Count(a => string.IsNullOrWhiteSpace(a.Meta.Cover) || string.IsNullOrWhiteSpace(a.Meta.Description));
            rules.Add(new PublishCheckRule("CoverDesc", noCover, "公众号等平台的必备字段"));
        }
        return rules;
    }

    /// <summary>单篇检查：返回 (是否通过, 说明) 列表，用于发布对话框。</summary>
    public static IReadOnlyList<(bool Ok, string Label)> EvaluateArticle(Article article, RepoScan scan)
    {
        var list = new List<(bool, string)>
        {
            (!string.IsNullOrWhiteSpace(article.Meta.Cover), "封面"),
            (!string.IsNullOrWhiteSpace(article.Meta.Description), "description"),
        };
        var total = article.ImageReferences.Count;
        var valid = article.ImageReferences.Count(p =>
        {
            var img = scan.FindImage(p);
            return img is { IsMissing: false };
        });
        list.Add((valid == total, total == 0 ? "图片引用（无图）" : $"图片引用 {valid}/{total} 有效"));
        return list;
    }
}
