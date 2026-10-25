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
/// 缓存构建（全量文章 hash + git diff）在后台线程预热：Invalidate 立即触发重建，
/// 重建期间读取方继续用旧缓存（stale-while-revalidate），UI 线程不再出现秒级冻结。
/// </summary>
public static class PublishHub
{
    private static readonly object Gate = new();
    private static Dictionary<string, ArticlePublishRow>? _cache;
    private static Task? _rebuilding;

    public static void Invalidate()
    {
        lock (Gate)
        {
            if (_rebuilding != null) return; // 已有构建在途：完成后缓存即新
            _rebuilding = Task.Run(BuildOnce);
        }
    }

    /// <summary>后台构建：完成后原子换入缓存并清掉在途标记（构建期间的新失效由下一次 Invalidate 接力）。</summary>
    private static void BuildOnce()
    {
        Dictionary<string, ArticlePublishRow> built;
        try
        {
            built = BuildCache();
        }
        catch
        {
            built = new Dictionary<string, ArticlePublishRow>();
        }
        lock (Gate)
        {
            _cache = built;
            _rebuilding = null;
        }
    }

    /// <summary>取缓存：命中直接返回；冷启动（无旧值）时等待在途后台构建，必要时就地补发一次。</summary>
    private static Dictionary<string, ArticlePublishRow> EnsureCache()
    {
        Task building;
        lock (Gate)
        {
            if (_cache != null) return _cache;
            _rebuilding ??= Task.Run(BuildOnce);
            building = _rebuilding;
        }
        building.GetAwaiter().GetResult();
        lock (Gate) return _cache ?? new Dictionary<string, ArticlePublishRow>();
    }

    public static (PublishStatus Status, string Note) EvaluateArticle(
        string markdownPath, PublishChannel channel)
    {
        var rows = EnsureCache();
        if (!rows.TryGetValue(markdownPath, out var row))
        {
            return (PublishStatus.Off, Infrastructure.I18n.T("Vox.Publish.Status.Unpublished"));
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

    private static Dictionary<string, ArticlePublishRow> BuildCache()
    {
        var scan = AppState.Instance.Scan;
        var rows = new Dictionary<string, ArticlePublishRow>();
        if (scan == null)
        {
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
        return rows;
    }
}
