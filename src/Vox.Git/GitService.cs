using LibGit2Sharp;

namespace Vox.Git;

/// <summary>内置 git 基座：LibGit2Sharp 进程内完成本地操作与 HTTPS+PAT 推送；SSH 降级外部 git（决策 D5）。</summary>
public sealed class GitService
{
    private readonly string _repoRoot;

    public GitService(string repoRoot) => _repoRoot = repoRoot;

    public RepositoryStatus Status()
    {
        using var repo = new Repository(_repoRoot);
        return repo.RetrieveStatus(new StatusOptions { IncludeUntracked = true });
    }

    public Commit CommitAll(string message, string authorName, string authorEmail)
    {
        using var repo = new Repository(_repoRoot);
        Commands.Stage(repo, "*");
        var signature = new Signature(authorName, authorEmail, DateTimeOffset.Now);
        return repo.Commit(message, signature, signature);
    }

    public void Push(string pat, string remoteName = "origin", string branch = "main")
    {
        using var repo = new Repository(_repoRoot);
        var remote = repo.Network.Remotes[remoteName];
        repo.Network.Push(remote, $"refs/heads/{branch}", new PushOptions
        {
            CredentialsProvider = (_, _, _) => new UsernamePasswordCredentials
            {
                Username = pat,
                Password = string.Empty,
            },
        });
    }
}
