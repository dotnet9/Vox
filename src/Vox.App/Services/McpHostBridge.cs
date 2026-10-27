using System.Text.Json.Nodes;
using Avalonia.Threading;
using Vox.App.Dialogs;
using Vox.App.Infrastructure;
using Vox.App.Views;
using Vox.Core;
using Vox.Core.Mcp;

namespace Vox.App.Services;

/// <summary>
/// MCP 宿主桥：把 Core 的 git / 发布 / 写确认请求接到应用服务与弹层；
/// 所有对外返回的载荷是 JSON 字符串（Core 不引用 App/Git 类型）。
/// </summary>
public sealed class McpHostBridge : IMcpHost
{
    private static MainWindow? Shell => MainWindow.Current;

    public async Task<bool> ConfirmWriteAsync(McpWriteRequest request, CancellationToken ct)
    {
        var shell = Shell;
        if (shell == null) return false;

        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(() =>
        {
            void OnClosed() => completion.TrySetResult(false);
            shell.ModalClosed += OnClosed;
            completion.Task.ContinueWith(_ => Dispatcher.UIThread.Post(() => shell.ModalClosed -= OnClosed),
                TaskScheduler.Default);
            shell.ShowModal(new McpConfirmModal(shell, request, allowed => completion.TrySetResult(allowed)));
        });

        await using var registration = ct.Register(() =>
        {
            completion.TrySetResult(false);
            Dispatcher.UIThread.Post(() => Shell?.CloseModal());
        });
        return await completion.Task.ConfigureAwait(false);
    }

    /// <summary>外部写入后整库重扫（页面/索引/发布缓存随之刷新），与界面内保存同口径。</summary>
    public void NotifyContentChanged(string? relativePath)
    {
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                AppState.Instance.NotifyRepoChanged();
                PublishHub.Invalidate();
            }
            catch
            {
                // 重扫失败保留旧索引，调用方已收到写入成功
            }
        });
    }

    public Task<string?> GitStatusAsync(CancellationToken ct) => Task.Run(() =>
    {
        try
        {
            var git = AppState.Instance.Git();
            if (git == null) return null;
            var (branch, ahead, behind) = git.BranchState();
            var changes = git.SemanticChanges();
            var items = new JsonArray();
            foreach (var change in changes)
            {
                items.Add(new JsonObject
                {
                    ["path"] = change.Path,
                    ["kind"] = change.Kind.ToString(),
                    ["kindLabel"] = change.KindLabel,
                    ["category"] = change.Category,
                });
            }
            return (string?)new JsonObject
            {
                ["branch"] = branch,
                ["ahead"] = ahead ?? 0,
                ["behind"] = behind ?? 0,
                ["clean"] = changes.Count == 0,
                ["changes"] = items,
            }.ToJsonString();
        }
        catch
        {
            return null;
        }
    }, ct);

    public Task<string?> PublishStatusAsync(string? articlePath, CancellationToken ct) => Task.Run(() =>
    {
        try
        {
            var rows = PublishHub.Rows();
            if (!string.IsNullOrWhiteSpace(articlePath))
            {
                var row = rows.FirstOrDefault(r =>
                    string.Equals(r.MarkdownPath, articlePath, StringComparison.OrdinalIgnoreCase));
                return row == null
                    ? (string?)new JsonObject
                    {
                        ["article"] = articlePath,
                        ["known"] = false,
                        ["channels"] = new JsonObject(),
                    }.ToJsonString()
                    : RowJson(row).ToJsonString();
            }

            var array = new JsonArray();
            foreach (var row in rows) array.Add(RowJson(row));
            return (string?)new JsonObject
            {
                ["total"] = rows.Count,
                ["rows"] = array,
            }.ToJsonString();
        }
        catch
        {
            return null;
        }
    }, ct);

    public Task<string?> PublishCheckAsync(string articlePath, CancellationToken ct) => Task.Run(() =>
    {
        try
        {
            var scan = AppState.Instance.Scan;
            var article = scan?.FindArticle(articlePath);
            if (scan == null || article == null) return null;
            var checks = PublishChecks.EvaluateArticle(article, scan);
            var items = new JsonArray();
            foreach (var (ok, label) in checks)
            {
                items.Add(new JsonObject { ["ok"] = ok, ["label"] = label });
            }
            return (string?)new JsonObject
            {
                ["article"] = article.MarkdownPath,
                ["passed"] = checks.Count(c => c.Ok),
                ["total"] = checks.Count,
                ["checks"] = items,
            }.ToJsonString();
        }
        catch
        {
            return null;
        }
    }, ct);

    /// <summary>
    /// 平台发布会使用平台凭据并产生外部副作用，MCP 层不代发：
    /// 返回明确指引，让调用方回到发布中心逐篇确认。
    /// </summary>
    public Task<string?> PublishArticleAsync(string articlePath, string platform, CancellationToken ct) =>
        Task.FromResult<string?>(new JsonObject
        {
            ["ok"] = false,
            ["reason"] = "manual-confirm-required",
            ["article"] = articlePath,
            ["platform"] = platform,
            ["message"] = "平台发布涉及平台凭据与外部可见副作用，请在 Vox 发布中心逐篇确认发布。",
        }.ToJsonString());

    public Task<string?> GitCommitAsync(string message, IReadOnlyList<string>? paths, CancellationToken ct) => Task.Run(() =>
    {
        try
        {
            var git = AppState.Instance.Git();
            if (git == null) return null;
            var (name, email) = git.Identity();
            var commit = paths is { Count: > 0 }
                ? git.CommitPaths(paths, message, name, email)
                : git.CommitAll(message, name, email);
            NotifyContentChanged(null);
            return (string?)new JsonObject
            {
                ["sha"] = commit.Sha,
                ["shortSha"] = commit.Sha[..7],
                ["message"] = message,
                ["paths"] = paths?.Count ?? 0,
            }.ToJsonString();
        }
        catch
        {
            return null;
        }
    }, ct);

    public Task<string?> GitPushAsync(CancellationToken ct) => Task.Run(() =>
    {
        try
        {
            var git = AppState.Instance.Git();
            if (git == null) return null;
            var pat = Vox.Git.CredentialStoreFactory.Create().GetPat("github.com");
            git.Push(pat ?? string.Empty);
            NotifyContentChanged(null);
            var (branch, ahead, behind) = git.BranchState();
            return (string?)new JsonObject
            {
                ["ok"] = true,
                ["branch"] = branch,
                ["ahead"] = ahead ?? 0,
                ["behind"] = behind ?? 0,
            }.ToJsonString();
        }
        catch
        {
            return null;
        }
    }, ct);

    private static JsonObject RowJson(ArticlePublishRow row)
    {
        var channels = new JsonObject();
        for (var i = 0; i < row.Channels.Length; i++)
        {
            var (status, note) = row.Channels[i];
            channels[((PublishChannel)i).ToString().ToLowerInvariant()] = new JsonObject
            {
                ["status"] = status switch
                {
                    PublishStatus.Ok => "ok",
                    PublishStatus.Stale => "stale",
                    PublishStatus.Error => "error",
                    _ => "off",
                },
                ["note"] = note,
            };
        }
        return new JsonObject
        {
            ["path"] = row.MarkdownPath,
            ["title"] = row.Title,
            ["yearMonth"] = row.YearMonth,
            ["channels"] = channels,
        };
    }
}
