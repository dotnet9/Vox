using Vox.Core;
using Xunit;

public class RepoProfileTests
{
    [Fact]
    public void Scan_finds_articles_with_sidecar_and_flags_missing()
    {
        var root = Directory.CreateTempSubdirectory("vox-core-test").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "2026", "09"));
            File.WriteAllText(Path.Combine(root, "2026", "09", "foo.md"), "# t");
            File.WriteAllText(Path.Combine(root, "2026", "09", "foo.yml"), "title: t");
            File.WriteAllText(Path.Combine(root, "2026", "09", "bar.md"), "# b");
            Directory.CreateDirectory(Path.Combine(root, "site"));
            File.WriteAllText(Path.Combine(root, "site", "categories.json"), "{}");

            var (articles, missing) = CodeWfRepoProfile.Scan(root);

            var foo = Assert.Single(articles);
            Assert.Equal("foo", foo.Slug);
            Assert.Equal("2026", foo.Year);
            Assert.Equal("2026/09/foo.yml", foo.MetaPath);
            var miss = Assert.Single(missing);
            Assert.Contains("bar.md", miss.MarkdownPath);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void LinkStrategy_roundtrips_cdn_and_relative()
    {
        var cdn = LinkStrategy.ToCdnUrl(@"2026\09\a.png");
        Assert.Equal("https://img1.dotnet9.com/2026/09/a.png", cdn);
        Assert.Equal("2026/09/a.png", LinkStrategy.ToRelativePath(cdn));
        Assert.Null(LinkStrategy.ToRelativePath("https://other.example/a.png"));
    }
}
