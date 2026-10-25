using Vox.App.Infrastructure;
using Vox.Core;
using Vox.Git;

namespace Vox.App.Services;

/// <summary>单篇文章 × 各平台的状态视图（矩阵行 / 徽标 / 文章库列共用）。</summary>
public sealed record ArticlePublishRow
{
    public required string Title { get; init; }
    public required string MarkdownPath { get; init; }
    public required string YearMonth { get; init; }
    public required string LocalFile { get; init; }
    public required string ContentHash { get; init; }
    public required (PublishStatus Status, string Note)[] Channels { get; init; }
}

/// <summary>
/// 发布状态中枢：汇总内容快照 hash、发布状态库与 git 工作区，给写作页徽标、文章库列、
/// 发布中心矩阵一个一致的口径。变更（编辑保存 / git 操作 / 发布完成）后调 Invalidate。
/// </summary>
public static class PublishHub
{
    private static Dictionary<string, ArticlePublishRow>? _cache;

    public static void Invalidate() => _cache = null;

    public static (PublishStatus Status, string Note) EvaluateArticle(
        string markdownPath, PublishChannel channel)
    {
        var rows = EnsureCache();
        if (!rows.TryGetValue(markdownPath, out var row))
        {
            return (PublishStatus.Off, "未发布");
        }
        return row.Channels[(int)channel];
    }

    public static IReadOnlyList<ArticlePublishRow> Rows()
    {
        var rows = EnsureCache();
        return rows.Values
            .OrderByDescending(r => r.YearMonth, StringComparer.Ordinal)
            .ToList();
    }

    public static int PendingCount()
    {
        // 导航角标口径：待推送提交 + 需更新/未发布/失败的平台格子。
        var ahead = AheadCount();
        var cells = Rows().Sum(r => r.Channels.Count(c => c.Status is PublishStatus.Stale or PublishStatus.Off or PublishStatus.Error));
        return ahead + cells;
    }

    public static int AheadCount()
    {
        try
        {
            var git = AppState.Instance.Git();
            return git?.BranchState().AheadBy ?? 0;
        }
        catch
        {
            return 0;
        }
    }

    private static Dictionary<string, ArticlePublishRow> EnsureCache()
    {
        if (_cache != null) return _cache;

        var scan = AppState.Instance.Scan;
        var rows = new Dictionary<string, ArticlePublishRow>();
        if (scan == null)
        {
            _cache = rows;
            return rows;
        }

        var states = PublishStateStore.Load();
        HashSet<string>? dirty = null;
        try
        {
            dirty = AppState.Instance.Git()?.SemanticChanges()
                .Select(c => c.Path)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            // 无 git 或仓库异常：站点通道一律按 Ok 处理。
        }

        foreach (var article in scan.Articles)
        {
            var localFile = LinkStrategy.ToLocalFile(scan.RepoRoot, article.MarkdownPath);
            string hash;
            try
            {
                hash = File.Exists(localFile) ? PublishStateStore.ContentHash(localFile) : string.Empty;
            }
            catch
            {
                hash = string.Empty;
            }

            var channels = new (PublishStatus, string)[5];
            foreach (PublishChannel channel in Enum.GetValues<PublishChannel>())
            {
                channels[(int)channel] = PublishStateStore.Evaluate(
                    states, article.MarkdownPath, channel, hash, dirty?.ToList());
            }
            rows[article.MarkdownPath] = new ArticlePublishRow
            {
                Title = string.IsNullOrEmpty(article.Title) ? article.Slug : article.Title,
                MarkdownPath = article.MarkdownPath,
                YearMonth = $"{article.Year}/{article.Month}",
                LocalFile = localFile,
                ContentHash = hash,
                Channels = channels,
            };
        }
        _cache = rows;
        return rows;
    }
}
