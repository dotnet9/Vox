using LibGit2Sharp;

namespace Vox.Git;

/// <summary>语义化改动条目：聚合到“文章 / 图片 / 站点数据”视图，而不是文件名列表（需求 M1 同步 P0）。</summary>
public sealed record ChangeEntry(string Path, ChangeKind Kind, string Category, int LinesAdded = -1, int LinesDeleted = -1)
{
    public string KindLabel => Kind switch
    {
        ChangeKind.Added => "A",
        ChangeKind.Modified => "M",
        ChangeKind.Deleted => "D",
        ChangeKind.Renamed => "R",
        _ => "?",
    };

    /// <summary>行级统计可用（文本文件且 diff 已取到）；-1 表示未知（二进制/统计失败），UI 不显示计数。</summary>
    public bool HasLineStats => LinesAdded >= 0 && LinesDeleted >= 0;
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

/// <summary>单行 diff：Kind 为 ' '（上下文）/ '+'（新增）/ '-'（删除）。</summary>
public sealed record DiffLine(char Kind, string Text);

public sealed record DiffHunk(string Header, IReadOnlyList<DiffLine> Lines);

/// <summary>一个文件的文本 diff（HEAD → 工作区）。</summary>
public sealed record TextDiff(string OldPath, IReadOnlyList<DiffHunk> Hunks);

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

    /// <summary>分支名与领先/落后远端的提交数（同步页顶部状态）。</summary>
    public (string Branch, int? AheadBy, int? BehindBy) BranchState()
    {
        using var repo = new Repository(_repoRoot);
        var head = repo.Head;
        return (head.FriendlyName, head.TrackingDetails.AheadBy, head.TrackingDetails.BehindBy);
    }

    /// <summary>工作区改动按仓库语义聚合：文章（md/yml）、图片、站点数据、其他；含 +/- 行数统计。</summary>
    public IReadOnlyList<ChangeEntry> SemanticChanges()
    {
        var pending = new List<(string Path, ChangeKind Kind)>();
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
            pending.Add((path, kind));
        }

        var lineStats = CountLineStats(pending.Select(p => p.Path));
        return pending
            .Select(p => (p.Path, p.Kind, Stats: lineStats.GetValueOrDefault(p.Path, (-1, -1))))
            .Select(e => new ChangeEntry(e.Path, e.Kind, Categorize(e.Path), e.Stats.Item1, e.Stats.Item2))
            .ToList();
    }

    /// <summary>一批文件的 +/- 行数：HEAD（或空树）→ 工作区一次性 diff（与 TextDiff 同口径）取 Patch 行统计；失败的文件保持 -1（不阻断列表）。</summary>
    private Dictionary<string, (int Added, int Deleted)> CountLineStats(IEnumerable<string> paths)
    {
        var stats = new Dictionary<string, (int, int)>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var repo = new Repository(_repoRoot);
            var patch = repo.Diff.Compare<Patch>(repo.Head.Tip?.Tree, DiffTargets.WorkingDirectory, paths);
            foreach (var entry in patch)
            {
                stats[entry.Path.Replace('\\', '/')] = (entry.LinesAdded, entry.LinesDeleted);
            }
        }
        catch
        {
            // 行统计失败（如无 HEAD 的空仓库 diff 异常）：保持空表，UI 不显示计数。
        }

        // 未跟踪文件在部分 diff 口径下不产生 patch：新增的文本文件按行数补齐（图片等二进制保持未知）。
        foreach (var path in paths)
        {
            if (stats.ContainsKey(path) || IsImagePath(path)) continue;
            try
            {
                var full = System.IO.Path.Combine(_repoRoot, path.Replace('/', System.IO.Path.DirectorySeparatorChar));
                if (File.Exists(full)) stats[path] = (File.ReadLines(full).Count(), 0);
            }
            catch
            {
                // 读取失败（占用/编码）：不补。
            }
        }
        return stats;
    }

    private static bool IsImagePath(string path)
    {
        var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        return ext is ".svg" or ".jpg" or ".jpeg" or ".png" or ".gif" or ".webp";
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

    /// <summary>某次提交相对其父提交的改动文件列表（历史展开用）。</summary>
    public IReadOnlyList<string> ChangedFiles(string sha)
    {
        using var repo = new Repository(_repoRoot);
        var commit = repo.Commits.FirstOrDefault(c => c.Sha.StartsWith(sha, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"提交不存在：{sha}");
        if (!commit.Parents.Any())
        {
            return commit.Tree.Select(e => e.Path).ToList();
        }
        var changes = repo.Diff.Compare<TreeChanges>(commit.Parents.First().Tree, commit.Tree);
        return changes.Select(c => c.Path).Distinct().ToList();
    }

    /// <summary>单文件文本 diff（HEAD → 工作区，含未跟踪文件）。</summary>
    public TextDiff? TextDiff(string path)
    {
        using var repo = new Repository(_repoRoot);
        var tip = repo.Head.Tip;
        if (tip == null) return null;
        var patch = repo.Diff.Compare<Patch>(tip.Tree, DiffTargets.WorkingDirectory, new[] { path });
        var content = patch?.Content;
        if (string.IsNullOrWhiteSpace(content) || content.Contains("Binary files", StringComparison.Ordinal))
        {
            return null;
        }
        return ParsePatch(path, content);
    }

    /// <summary>解析 unified diff 文本为分块结构（解析失败返回整段为上下文）。</summary>
    public static TextDiff ParsePatch(string path, string content)
    {
        var hunks = new List<DiffHunk>();
        List<DiffLine>? current = null;
        var header = string.Empty;
        foreach (var raw in content.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.StartsWith("diff --git", StringComparison.Ordinal) ||
                line.StartsWith("index ", StringComparison.Ordinal) ||
                line.StartsWith("--- ", StringComparison.Ordinal) ||
                line.StartsWith("+++ ", StringComparison.Ordinal) ||
                line.StartsWith("new file", StringComparison.Ordinal) ||
                line.StartsWith("deleted file", StringComparison.Ordinal) ||
                line.StartsWith("similarity ", StringComparison.Ordinal) ||
                line.StartsWith("rename ", StringComparison.Ordinal))
            {
                continue;
            }
            if (line.StartsWith("@@", StringComparison.Ordinal))
            {
                current = [];
                hunks.Add(new DiffHunk(line, current));
                header = line;
                continue;
            }
            if (current == null) continue;
            if (line.Length == 0)
            {
                current.Add(new DiffLine(' ', string.Empty));
            }
            else if (line[0] is ' ' or '+' or '-')
            {
                current.Add(new DiffLine(line[0], line[1..]));
            }
        }
        if (hunks.Count == 0 && !string.IsNullOrEmpty(header))
        {
            hunks.Add(new DiffHunk(header, []));
        }
        return new TextDiff(path, hunks);
    }

    /// <summary>HEAD 中某文件的流（图片“修改前”用）；HEAD 不含该文件（新增）返回 null。</summary>
    public Stream? OpenHeadStream(string path)
    {
        using var repo = new Repository(_repoRoot);
        var tip = repo.Head.Tip;
        var entry = tip?.BlobFor(path);
        return entry?.GetContentStream();
    }

    /// <summary>HEAD 中某文件的字节长度；文件不存在返回 null。</summary>
    public long? HeadBlobSize(string path)
    {
        using var repo = new Repository(_repoRoot);
        var blob = repo.Head.Tip?.BlobFor(path);
        return blob?.Size;
    }

    /// <summary>勾选暂存后提交（部分提交）；paths 为仓库相对路径。</summary>
    public Commit CommitPaths(IEnumerable<string> paths, string message, string authorName, string authorEmail)
    {
        using var repo = new Repository(_repoRoot);
        foreach (var path in paths)
        {
            Commands.Stage(repo, path);
        }
        var signature = new Signature(string.IsNullOrWhiteSpace(authorName) ? "Dotnet9" : authorName,
            string.IsNullOrWhiteSpace(authorEmail) ? "dotnet9@codewf.com" : authorEmail, DateTimeOffset.Now);
        return repo.Commit(message, signature, signature);
    }

    /// <summary>把模式追加到仓库根 .gitignore（已存在则跳过）。</summary>
    public void AddToGitignore(string pattern)
    {
        var file = System.IO.Path.Combine(_repoRoot, ".gitignore");
        var lines = File.Exists(file) ? File.ReadAllLines(file).ToList() : [];
        if (lines.Any(l => string.Equals(l.Trim(), pattern.Trim(), StringComparison.Ordinal))) return;
        if (lines.Count > 0 && lines[^1].Trim().Length > 0) lines.Add(string.Empty);
        lines.Add(pattern);
        File.WriteAllLines(file, lines);
    }

    /// <summary>放弃一个文件的未提交更改：HEAD 中存在则还原（含暂存态），新增文件从索引与磁盘移除。</summary>
    public void Discard(string path)
    {
        using var repo = new Repository(_repoRoot);
        var inHead = repo.Head.Tip?.Tree[path] != null;
        if (inHead)
        {
            repo.CheckoutPaths("HEAD", new[] { path }, new CheckoutOptions { CheckoutModifiers = CheckoutModifiers.Force });
            return;
        }
        try { repo.Index.Remove(path); }
        catch (ArgumentException) { /* 未被暂存，忽略 */ }
        var full = System.IO.Path.Combine(_repoRoot, path.Replace('/', System.IO.Path.DirectorySeparatorChar));
        if (File.Exists(full)) File.Delete(full);
    }

    /// <summary>拉取并仅允许快进合并；远端分叉时抛出可读异常。</summary>
    public int PullFastForward(string authorName, string authorEmail)
    {
        using var repo = new Repository(_repoRoot);
        var remote = repo.Network.Remotes["origin"] ?? throw new InvalidOperationException("未配置 origin 远端。");
        var before = repo.Head.TrackingDetails.BehindBy ?? 0;
        repo.Network.Fetch(remote.Name, null, new FetchOptions());
        var result = repo.MergeFetchedRefs(
            new Signature(string.IsNullOrWhiteSpace(authorName) ? "Vox" : authorName, authorEmail, DateTimeOffset.Now),
            new MergeOptions { FastForwardStrategy = FastForwardStrategy.FastForwardOnly });
        if (result.Status == MergeStatus.NonFastForward)
        {
            throw new InvalidOperationException("远端历史与本地分叉，无法快进合并——请用外部 git 处理（决策 D5 降级路径）。");
        }
        return (repo.Head.TrackingDetails.BehindBy ?? 0) == 0 ? before : 0;
    }

    /// <summary>origin 远端归一为 Web 首页（https://{host}/{owner}/{repo}）；SSH/HTTPS 形态统一，无 origin 或本地路径远端返回 null。</summary>
    public string? RemoteUrl()
    {
        try
        {
            using var repo = new Repository(_repoRoot);
            return NormalizeRemoteUrl(repo.Network.Remotes["origin"]?.Url);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>提交在远端（GitHub 等）的 Web 地址：https://{host}/{owner}/{repo}/commit/{sha}；无 origin 返回 null。</summary>
    public string? CommitUrl(string sha) => RemoteUrl() is { } home ? $"{home}/commit/{sha}" : null;

    /// <summary>任意远端地址 → https Web 首页：去 .git 尾、SSH（git@host:owner/repo / ssh://git@host/...）归一为 https。本地路径远端返回 null。</summary>
    public static string? NormalizeRemoteUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        url = url.Trim().TrimEnd('/');
        if (url.Contains('\\')) return null; // Windows 本地路径远端
        if (url.Length >= 2 && url[1] == ':' && char.IsAsciiLetter(url[0])) return null; // 盘符形态（D:/repos/vox）
        if (url.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) url = url[..^4];
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            var hostPath = url.StartsWith("ssh://", StringComparison.OrdinalIgnoreCase) ? url["ssh://".Length..] : url;
            var at = hostPath.IndexOf('@');
            if (at >= 0) hostPath = hostPath[(at + 1)..];
            url = "https://" + hostPath.Replace(':', '/');
        }
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
            || !uri.Host.Contains('.')) return null; // 无点主机多为本地路径误归一
        var segments = uri.AbsolutePath.Trim('/').Split('/');
        if (segments.Length < 2 || segments.Any(string.IsNullOrEmpty)) return null;
        return $"{uri.Scheme}://{uri.Host}{uri.AbsolutePath.TrimEnd('/')}";
    }

    public (string Name, string Email) Identity()
    {
        using var repo = new Repository(_repoRoot);
        var name = repo.Config.Get<string>("user.name")?.Value ?? "Dotnet9";
        var email = repo.Config.Get<string>("user.email")?.Value ?? "dotnet9@codewf.com";
        return (name, email);
    }

    /// <summary>身份未配置时返回缺失标记（不再静默兜底硬编码身份冒名提交，2026-11-13）：
    /// 调用方据 identityMissing 阻断提交并指引配置。</summary>
    public (bool Missing, string Name, string Email) IdentityOrDefault()
    {
        using var repo = new Repository(_repoRoot);
        var name = repo.Config.Get<string>("user.name")?.Value;
        var email = repo.Config.Get<string>("user.email")?.Value;
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(email))
        {
            return (true, string.Empty, string.Empty);
        }
        return (false, name, email);
    }

    public void SetIdentity(string name, string email)
    {
        using var repo = new Repository(_repoRoot);
        repo.Config.Set("user.name", name);
        repo.Config.Set("user.email", email);
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

internal static class TreeEntryExtensions
{
    public static LibGit2Sharp.Blob? BlobFor(this Commit commit, string path) =>
        commit.Tree[path]?.Target as LibGit2Sharp.Blob;
}
