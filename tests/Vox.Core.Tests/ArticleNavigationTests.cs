using Vox.Core;
using Xunit;

public class ArticleNavigationTests
{
    [Fact]
    public void List_has_only_articles_in_descending_date_order()
    {
        var rows = ArticleNavigation.BuildRows([Article(2025, 1), Article(2026, 11)], false, new HashSet<string>());
        Assert.All(rows, row => { Assert.False(row.IsGroup); Assert.Equal(0, row.Depth); });
        Assert.Equal("2026/11/article.md", rows[0].Key);
    }

    [Fact]
    public void Tree_indents_year_month_and_article_and_keeps_stable_keys()
    {
        var rows = ArticleNavigation.BuildRows([Article(2026, 11)], true, new HashSet<string>());
        Assert.Equal([0, 1, 2], rows.Select(row => row.Depth));
        Assert.Equal("year:2026", rows[0].Key);
        Assert.Equal("month:2026/11", rows[1].Key);
        Assert.True(rows[0].IsExpanded);
        Assert.False(rows[2].IsGroup);
    }

    [Fact]
    public void Collapsing_a_year_omits_all_descendants_and_retains_other_years()
    {
        var rows = ArticleNavigation.BuildRows([Article(2026, 11), Article(2025, 1)], true, new HashSet<string> { "year:2026" });
        Assert.False(rows[0].IsExpanded);
        Assert.DoesNotContain(rows, row => row.Article?.Year == "2026");
        Assert.Contains(rows, row => row.Article?.Year == "2025");
    }

    [Fact]
    public void Collapsing_a_month_does_not_collapse_its_siblings()
    {
        var rows = ArticleNavigation.BuildRows([Article(2026, 11), Article(2026, 10)], true, new HashSet<string> { "month:2026/11" });
        Assert.DoesNotContain(rows, row => row.Article?.Month == "11");
        Assert.Contains(rows, row => row.Article?.Month == "10");
        Assert.Contains(rows, row => row.Key == "month:2026/11" && !row.IsExpanded);
    }

    [Fact]
    public void Large_collapsed_tree_does_not_materialize_article_rows()
    {
        var articles = Enumerable.Range(0, 5000).Select(index => Article(2026, 11, index.ToString()));
        var rows = ArticleNavigation.BuildRows(articles, true, new HashSet<string> { "year:2026" });
        Assert.Single(rows);
        Assert.Null(rows[0].Article);
    }

    private static Article Article(int year, int month, string slug = "article") => new()
    {
        Year = year.ToString(), Month = month.ToString("00"), Slug = slug,
        MarkdownPath = $"{year}/{month:00}/{slug}.md", MetaPath = "",
        Meta = new() { Title = slug, Slug = slug, Date = new DateTime(year, month, 1) }
    };
}
