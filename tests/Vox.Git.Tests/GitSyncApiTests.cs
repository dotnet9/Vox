using LibGit2Sharp;
using Vox.Git;
using VoxChangeKind = Vox.Git.ChangeKind;
using Xunit;

public class GitSyncApiTests
{
    /// <summary>建一个带初始提交的临时仓库。</summary>
    private static (string Root, GitService Svc) CreateRepo()
    {
        var root = Directory.CreateTempSubdirectory("vox-git-api").FullName;
        Repository.Init(root);
        var svc = new GitService(root);
        Directory.CreateDirectory(Path.Combine(root, "2026", "01"));
        File.WriteAllText(Path.Combine(root, "2026", "01", "a.md"), "line1\nline2\n");
        Directory.CreateDirectory(Path.Combine(root, "site"));
        File.WriteAllText(Path.Combine(root, "site", "x.json"), "{}");
        svc.CommitAll("init", "Vox", "vox@test");
        svc.SetIdentity("Dotnet9", "dotnet9@test");
        return (root, svc);
    }

    private static void Cleanup(string root)
    {
        try { Directory.Delete(root, recursive: true); } catch { /* libgit2 句柄延迟释放 */ }
    }

    [Fact]
    public void SemanticChanges_and_BranchState_after_edits()
    {
        var (root, svc) = CreateRepo();
        try
        {
            var (branch, ahead, behind) = svc.BranchState();
            Assert.Equal("master", branch); // Repository.Init 默认分支名随 libgit2 配置，常见为 master
            Assert.True(ahead is null or 0, "无 upstream 时允许为 null"); // 有远端跟踪后为真实领先数
            Assert.True(behind is null or 0);

            File.AppendAllText(Path.Combine(root, "2026", "01", "a.md"), "line3\n");
            File.WriteAllText(Path.Combine(root, "site", "x.json"), "{\"a\":1}");
            File.WriteAllText(Path.Combine(root, "2026", "01", "new.png"), "png");

            var changes = svc.SemanticChanges();
            Assert.Contains(changes, c => c.Path.EndsWith("a.md") && c.Kind == VoxChangeKind.Modified && c.Category == "article");
            Assert.Contains(changes, c => c.Category == "site" && c.Kind == VoxChangeKind.Modified);
            Assert.Contains(changes, c => c.Path.EndsWith("new.png") && c.Kind == VoxChangeKind.Added && c.Category == "image");
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public void TextDiff_parses_hunks_with_kinds()
    {
        var (root, svc) = CreateRepo();
        try
        {
            File.AppendAllText(Path.Combine(root, "2026", "01", "a.md"), "line3\n");
            var diff = svc.TextDiff("2026/01/a.md");

            Assert.NotNull(diff);
            var hunk = Assert.Single(diff!.Hunks);
            Assert.Contains("@@", hunk.Header);
            Assert.Contains(hunk.Lines, l => l.Kind == '+' && l.Text == "line3");
            Assert.All(hunk.Lines, l => Assert.True(l.Kind is ' ' or '+' or '-'));
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public void CommitPaths_commits_only_selected_files()
    {
        var (root, svc) = CreateRepo();
        try
        {
            File.AppendAllText(Path.Combine(root, "2026", "01", "a.md"), "edited\n");
            File.WriteAllText(Path.Combine(root, "site", "x.json"), "{\"b\":2}");

            svc.CommitPaths(["site/x.json"], "site: only json", "Vox", "vox@test");

            using var repo = new Repository(root);
            // a.md 仍是未提交修改，x.json 已干净
            var status = repo.RetrieveStatus();
            Assert.NotEqual(FileStatus.NewInWorkdir, status["2026/01/a.md"].State);
            Assert.DoesNotContain(status, s => s.FilePath.Replace('\\', '/') == "site/x.json");
            Assert.Contains("only json", repo.Head.Tip.Message);
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public void ChangedFiles_returns_files_of_commit()
    {
        var (root, svc) = CreateRepo();
        try
        {
            File.WriteAllText(Path.Combine(root, "2026", "01", "b.md"), "b\n");
            var commit = svc.CommitPaths(["2026/01/b.md"], "add b", "Vox", "vox@test");

            var files = svc.ChangedFiles(commit.Sha[..7]);
            Assert.Contains("2026/01/b.md", files);
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public void Discard_restores_modified_and_deleted_files()
    {
        var (root, svc) = CreateRepo();
        try
        {
            var mdPath = Path.Combine(root, "2026", "01", "a.md");
            File.AppendAllText(mdPath, "edited\n");
            svc.Discard("2026/01/a.md");
            Assert.Equal("line1\nline2\n", File.ReadAllText(mdPath).Replace("\r\n", "\n"));

            File.Delete(mdPath);
            svc.Discard("2026/01/a.md");
            Assert.True(File.Exists(mdPath));
            Assert.Equal("line1\nline2\n", File.ReadAllText(mdPath).Replace("\r\n", "\n"));
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public void Discard_removes_added_file()
    {
        var (root, svc) = CreateRepo();
        try
        {
            var png = Path.Combine(root, "2026", "01", "new.png");
            File.WriteAllText(png, "png");
            svc.Discard("2026/01/new.png");
            Assert.False(File.Exists(png));
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public void AddToGitignore_appends_once_and_skips_duplicates()
    {
        var (root, svc) = CreateRepo();
        try
        {
            svc.AddToGitignore("*.tmp");
            svc.AddToGitignore("*.tmp");
            svc.AddToGitignore("*.log");
            var text = File.ReadAllText(Path.Combine(root, ".gitignore"));
            Assert.Contains("*.tmp", text);
            Assert.Contains("*.log", text);
            Assert.Equal(1, text.Split(new[] { "\n", "\r" }, StringSplitOptions.None).Count(l => l.Trim() == "*.tmp"));
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public void Identity_roundtrips()
    {
        var (root, svc) = CreateRepo();
        try
        {
            svc.SetIdentity("作者", "author@test");
            var (name, email) = svc.Identity();
            Assert.Equal("作者", name);
            Assert.Equal("author@test", email);
        }
        finally
        {
            Cleanup(root);
        }
    }
}
