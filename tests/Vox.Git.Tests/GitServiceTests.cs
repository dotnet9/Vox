using LibGit2Sharp;
using Vox.Git;
using Xunit;

public class GitServiceTests
{
    [Fact]
    public void CommitAll_creates_commit_on_fresh_repo()
    {
        var root = Directory.CreateTempSubdirectory("vox-git-test").FullName;
        try
        {
            Repository.Init(root);
            File.WriteAllText(Path.Combine(root, "a.md"), "hello");
            var svc = new GitService(root);

            svc.CommitAll("test: init", "Vox", "vox@codewf.com");

            using var repo = new Repository(root);
            Assert.NotEmpty(repo.Commits);
            Assert.Contains("test: init", repo.Head.Tip.Message);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* libgit2 句柄延迟释放 */ }
        }
    }
}
