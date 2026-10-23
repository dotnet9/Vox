using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using LibGit2Sharp;
using TKCode.Arc;
using Vox.Git;
using static Vox.App.Infrastructure.I18n;
using ArcButton = TKCode.Arc.Button;
using Avalonia;
using Vox.App.Infrastructure;

namespace Vox.App.Views.Pages;

/// <summary>同步：分支状态、语义化改动分组、提交信息、一键提交并推送、历史（决策 D5/D7）。</summary>
public sealed class SyncPage : UserControl
{
    private readonly TextBlock _branchState = new() { TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel _changes = new() { Spacing = 10 };
    private readonly TextBox _message = new() { AcceptsReturn = true, MinHeight = 64 };
    private readonly StackPanel _history = new() { Spacing = 4 };

    public SyncPage()
    {
        var commitButton = new ArcButton { Variant = ButtonVariant.Primary, Content = T(Sync.Label.CommitAndPush) };
        commitButton.Click += async (_, _) => await CommitAndPushAsync();

        Content = new ScrollViewer
        {
            Content = new DockPanel
            {
                Margin = new Thickness(16, 12, 16, 12),
                Children =
                {
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 10,
                        Children =
                        {
                            new TextBlock { Text = T(Sync.Label.Title), FontSize = 17, FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center },
                            new Border { Width = 8 },
                            commitButton,
                        },
                    }.Dock(Dock.Top),
                    new StackPanel
                    {
                        Spacing = 10,
                        Children =
                        {
                            _branchState,
                            _changes,
                            new StackPanel
                            {
                                Spacing = 4,
                                Children =
                                {
                                    new TextBlock { Text = T(Sync.Label.CommitMessage), FontWeight = FontWeight.SemiBold, FontSize = 12 },
                                    _message,
                                    new TextBlock { Text = T(Sync.Label.MessageHint), FontSize = 10.5, Opacity = 0.55 },
                                },
                            },
                            new TextBlock { Text = T(Sync.Label.History), FontWeight = FontWeight.SemiBold, FontSize = 12 },
                            _history,
                        },
                    }.Dock(Dock.Top),
                },
            },
        };

        AttachedToVisualTree += (_, _) => Reload();
        AppState.Instance.ScanChanged += () => Avalonia.Threading.Dispatcher.UIThread.Post(Reload);
    }

    private void Reload()
    {
        _changes.Children.Clear();
        _history.Children.Clear();
        var git = AppState.Instance.Git();
        if (git == null)
        {
            _branchState.Text = T(Sync.Label.NeedRepo);
            return;
        }

        try
        {
            var (branch, ahead) = git.BranchState();
            var changes = git.SemanticChanges();
            _branchState.Text = ahead is > 0
                ? TF(Sync.Label.BranchAhead, ahead, changes.Count)
                : TF(Sync.Label.BranchSynced, changes.Count);

            foreach (var group in changes.GroupBy(c => c.Category).OrderBy(g => g.Key))
            {
                var panel = new StackPanel { Spacing = 2 };
                panel.Children.Add(new TextBlock
                {
                    Text = $"{T("Vox.Sync.Group." + group.Key)} · {group.Count()}",
                    FontWeight = FontWeight.SemiBold,
                    FontSize = 12.5,
                    Margin = new Thickness(0, 4, 0, 2),
                });
                foreach (var entry in group.Take(30))
                {
                    panel.Children.Add(new TextBlock
                    {
                        Text = $"[{entry.KindLabel}] {entry.Path}",
                        FontSize = 11.5,
                        Opacity = 0.75,
                        FontFamily = new FontFamily("Geist Mono, Cascadia Code, Consolas"),
                    });
                }
                if (group.Count() > 30)
                {
                    panel.Children.Add(new TextBlock { Text = $"… +{group.Count() - 30}", FontSize = 11, Opacity = 0.5 });
                }
                _changes.Children.Add(panel);
            }

            if (changes.Count == 0)
            {
                _changes.Children.Add(new TextBlock { Text = T(Sync.Label.NoChanges), FontSize = 12, Opacity = 0.6 });
            }

            _message.Text = SuggestMessage(changes);
            foreach (var commit in git.History(10))
            {
                _history.Children.Add(new TextBlock
                {
                    Text = $"{commit.Sha}  {commit.Message}   · {commit.Author} · {commit.When.LocalDateTime:MM-dd HH:mm}",
                    FontSize = 11.5,
                    Opacity = 0.7,
                });
            }
        }
        catch (LibGit2SharpException ex)
        {
            _branchState.Text = ex.Message;
        }
    }

    private static string SuggestMessage(IReadOnlyList<ChangeEntry> changes)
    {
        if (changes.Count == 0) return string.Empty;
        var articles = changes.Count(c => c.Category == "article");
        var images = changes.Count(c => c.Category == "image");
        var site = changes.Count(c => c.Category == "site");
        var parts = new List<string>();
        if (articles > 0) parts.Add($"文章 ×{articles}");
        if (images > 0) parts.Add($"图片 ×{images}");
        if (site > 0) parts.Add($"站点数据 ×{site}");
        return $"docs: 更新 {string.Join("、", parts)}";
    }

    private async Task CommitAndPushAsync()
    {
        var git = AppState.Instance.Git();
        if (git == null || TopLevel.GetTopLevel(this) is not Window window) return;

        var pat = CredentialStoreFactory.Create().GetPat("github.com");
        if (string.IsNullOrEmpty(pat))
        {
            await Dialog.ConfirmAsync(window, T(Sync.Label.Title), T(Sync.Label.NeedPat), T(Common.Action.Confirm));
            return;
        }

        var changes = git.SemanticChanges();
        var message = string.IsNullOrWhiteSpace(_message.Text) ? SuggestMessage(changes) : _message.Text!;
        try
        {
            git.CommitAll(message, "Dotnet9", "dotnet9@codewf.com");
            git.Push(pat);
            AppState.Instance.NotifyRepoChanged();
            await Dialog.ConfirmAsync(window, T(Sync.Label.Title), T(Sync.Label.Pushed), T(Common.Action.Confirm));
        }
        catch (Exception ex)
        {
            await Dialog.ConfirmAsync(window, T(Sync.Label.Title), ex.Message, T(Common.Action.Confirm));
        }
    }
}
