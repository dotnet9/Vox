using Vox.Core;
using Xunit;

public class SiteCategoriesTests
{
    [Fact]
    public void Load_reads_display_names_sorted_by_sort()
    {
        var root = Directory.CreateTempSubdirectory("vox-cats-test").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "site"));
            File.WriteAllText(Path.Combine(root, "site", "categories.json"),
                """
                [
                  { "Sort": 2, "Name": "WPF", "Memo": "WPF 桌面开发", "Slug": "wpf" },
                  { "Sort": 1, "Name": ".NET", "Memo": ".NET 生态", "Slug": "dotnet" },
                  { "Sort": 10, "Name": "Avalonia UI", "Memo": "Avalonia 跨平台", "Slug": "avalonia" }
                ]
                """);

            var names = SiteCategories.LoadDisplayNames(root);

            Assert.Equal([".NET", "WPF", "Avalonia UI"], names);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Load_returns_empty_for_missing_or_broken_file()
    {
        var root = Directory.CreateTempSubdirectory("vox-cats-empty").FullName;
        try
        {
            Assert.Empty(SiteCategories.LoadDisplayNames(root));

            Directory.CreateDirectory(Path.Combine(root, "site"));
            File.WriteAllText(Path.Combine(root, "site", "categories.json"), "{ not an array ");
            Assert.Empty(SiteCategories.LoadDisplayNames(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
