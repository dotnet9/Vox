using LibGit2Sharp;

namespace Vox.Git;

/// <summary>语义化改动条目：聚合到“文章 / 图片 / 站点数据”视图，而不是文件名列表（需求 M1 同步 P0）。</summary>
public sealed record ChangeEntry(string Path, ChangeKind Kind, string Category)
{
    public string KindLabel => Kind switch
    {
        ChangeKind.Added => "A",
        ChangeKind.Modified => "M",
        ChangeKind.Deleted => "D",
        ChangeKind.Renamed => "R",
        _ => "?",
    };
}

public enum ChangeKind
{
    Modified,
    Added,
    Deleted,
    Renamed,
    Other,
}

public sealed record CommitInfo(string Sha, string Message, string Author, DateTimeOffset When);

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

    /// <summary>分支名与领先远端的提交数（同步页顶部状态）。</summary>
    public (string Branch, int? AheadBy) BranchState()
    {
        using var repo = new Repository(_repoRoot);
        var head = repo.Head;
        return (head.FriendlyName, head.TrackingDetails.AheadBy);
    }

    /// <summary>工作区改动按仓库语义聚合：文章（md/yml）、图片、站点数据、其他。</summary>
    public IReadOnlyList<ChangeEntry> SemanticChanges()
    {
        var entries = new List<ChangeEntry>();
        foreach (var item in Status())
        {
            var path = item.FilePath.Replace('\\', '/');
            ChangeKind kind;
            if (item.State.HasFlag(FileStatus.NewInWorkdir) || item.State.HasFlag(FileStatus.NewInIndex)) kind = ChangeKind.Added;
            else if (item.State.HasFlag(FileStatus.DeletedFromWorkdir) || item.State.HasFlag(FileStatus.DeletedFromIndex)) kind = ChangeKind.Deleted;
            else if (item.State.HasFlag(FileStatus.RenamedInWorkdir) || item.State.HasFlag(FileStatus.RenamedInIndex)) kind = ChangeKind.Renamed;
            else if (item.State.HasFlag(FileStatus.ModifiedInWorkdir) || item.State.HasFlag(FileStatus.ModifiedInIndex)) kind = ChangeKind.Modified;
            else kind = ChangeKind.Other;
            if (kind == ChangeKind.Other) continue;
            entries.Add(new ChangeEntry(path, kind, Categorize(path)));
        }
        return entries;
    }

    private static string Categorize(string path)
    {
        if (path.StartsWith("site/", StringComparison.OrdinalIgnoreCase)) return "site";
        var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".md" => "article",
            ".yml" or ".yaml" => "article",
            ".svg" or ".jpg" or ".jpeg" or ".png" or ".gif" or ".webp" => "image",
            _ => "other",
        };
    }

    public IReadOnlyList<CommitInfo> History(int count = 20)
    {
        using var repo = new Repository(_repoRoot);
        return repo.Commits.Take(count)
            .Select(c => new CommitInfo(c.Sha[..7], c.MessageShort, c.Author.Name, c.Author.When))
            .ToList();
    }

    public Commit CommitAll(string message, string authorName, string authorEmail)
    {
        using var repo = new Repository(_repoRoot);
        Commands.Stage(repo, "*");
        var signature = new Signature(authorName, authorEmail, DateTimeOffset.Now);
        return repo.Commit(message, signature, signature);
    }

    public void Push(string pat, string remoteName = "origin", string? branch = null)
    {
        using var repo = new Repository(_repoRoot);
        var remote = repo.Network.Remotes[remoteName];
        var pushRef = $"refs/heads/{branch ?? repo.Head.FriendlyName}";
        repo.Network.Push(remote, pushRef, new PushOptions
        {
            CredentialsProvider = (_, _, _) => new UsernamePasswordCredentials
            {
                Username = pat,
                Password = string.Empty,
            },
        });
    }
}
