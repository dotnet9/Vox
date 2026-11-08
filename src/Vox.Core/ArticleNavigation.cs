namespace Vox.Core;

/// <summary>列表和树共用可见行；折叠分组不产生子行，界面只实现视口内的容器。</summary>
public sealed record ArticleNavigationRow(string Key, string Label, int Depth, Article? Article, bool IsExpanded = false, string? ParentKey = null)
{
    public bool IsGroup => Article is null;
}

public static class ArticleNavigation
{
    public static IReadOnlyList<ArticleNavigationRow> BuildRows(
        IEnumerable<Article> articles, bool tree, IReadOnlySet<string> collapsed)
    {
        var ordered = articles.OrderByDescending(article => article.Meta.Date).ToList();
        if (!tree) return ordered.Select(article => Leaf(article, 0)).ToList();
        var rows = new List<ArticleNavigationRow>();
        foreach (var year in ordered.GroupBy(article => article.Year).OrderByDescending(group => group.Key))
        {
            var yearKey = YearKey(year.Key);
            rows.Add(new(yearKey, year.Key, 0, null, !collapsed.Contains(yearKey)));
            if (collapsed.Contains(yearKey)) continue;
            foreach (var month in year.GroupBy(article => article.Month).OrderByDescending(group => group.Key))
            {
                var monthKey = MonthKey(year.Key, month.Key);
                rows.Add(new(monthKey, month.Key, 1, null, !collapsed.Contains(monthKey), yearKey));
                if (!collapsed.Contains(monthKey)) rows.AddRange(month.Select(article => Leaf(article, 2)));
            }
        }
        return rows;
    }

    public static string YearKey(string year) => "year:" + year;
    public static string MonthKey(string year, string month) => $"month:{year}/{month}";
    private static ArticleNavigationRow Leaf(Article article, int depth) =>
        new(article.MarkdownPath, string.IsNullOrEmpty(article.Title) ? article.Slug : article.Title, depth, article,
            ParentKey: depth > 0 ? MonthKey(article.Year, article.Month) : null);
}
