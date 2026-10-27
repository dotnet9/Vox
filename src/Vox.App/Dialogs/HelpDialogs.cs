using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using static Vox.App.Infrastructure.I18n;

using Vox.App.Infrastructure;
using Vox.App.Views;

namespace Vox.App.Dialogs;

/// <summary>帮助菜单弹层：检查更新（GitHub Releases 最新版比对）与关于。</summary>
public static class HelpDialogs
{
    private static TextBlock Muted(string text, double size = 12) => new()
    {
        Text = text,
        FontSize = size,
        Foreground = Res("VoxMuted"),
        TextWrapping = TextWrapping.Wrap,
    };

    private static IBrush? Res(string key) =>
        Application.Current!.TryFindResource(key, out var v) && v is IBrush b ? b : null;

    private static Grid Row(string labelKey, string value)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        var label = new TextBlock { Text = " " };
        label[!TextBlock.TextProperty] = I18n.Bind(labelKey);
        label.Foreground = Res("VoxMuted");
        var valueText = new TextBlock { Text = value, Margin = new Thickness(24, 0, 0, 0) };
        Grid.SetColumn(valueText, 1);
        grid.Children.Add(label);
        grid.Children.Add(valueText);
        return grid;
    }

    /// <summary>关于弹层。</summary>
    public sealed class AboutModal : Border
    {
        public AboutModal(MainWindow shell)
        {
            var (modal, body, footer, close) = Modal.Create(TF0("Vox.Help.About.Title"), 420, 340);
            Classes.AddRange(modal.Classes);
            Width = modal.Width;
            MaxHeight = modal.MaxHeight;
            Modal.Skin(this);
            var skeleton = modal.Child;
            modal.Child = null;
            Child = skeleton;
            close.Click += (_, _) => shell.CloseModal();

            var logo = new Controls.VoxLogo
            {
                Width = 56,
                Height = 56,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            var name = new TextBlock
            {
                Text = "Vox · 维声", FontSize = 16, FontWeight = FontWeight.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            var slogan = new TextBlock
            {
                Text = TF0("Vox.Help.About.Slogan"),
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = Res("VoxMuted"),
            };
            var version = new TextBlock
            {
                Text = new Services.UpdateChecker().CurrentVersionText,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = Res("VoxMuted"),
            };
            var license = new TextBlock
            {
                Text = TF0("Vox.Help.About.License"),
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = Res("VoxMuted"),
            };
            body.HorizontalAlignment = HorizontalAlignment.Center;
            body.Children.AddRange([logo, name, slogan, version, license]);

            var closeBtn = new Button { Classes = { "btn" } };
            closeBtn[!Button.ContentProperty] = I18n.Bind("Vox.Common.Action.Close");
            closeBtn.Click += (_, _) => shell.CloseModal();
            footer.Children.Add(closeBtn);
        }
    }

    /// <summary>检查更新弹层：打开即自动查询，支持手动重查与前往发布页。</summary>
    public sealed class UpdateModal : Border
    {
        private readonly MainWindow _shell;
        private readonly TextBlock _result = new() { TextWrapping = TextWrapping.Wrap, MinHeight = 44 };
        private readonly Button _goRelease = new() { Classes = { "btn" }, IsVisible = false };
        private readonly Button _check = new() { Classes = { "btn", "primary" } };
        private readonly Services.UpdateChecker _checker = new();
        private string? _releaseUrl;

        public UpdateModal(MainWindow shell)
        {
            _shell = shell;
            var (modal, body, footer, close) = Modal.Create(TF0("Vox.Help.Update.Title"), 480, 340);
            Classes.AddRange(modal.Classes);
            Width = modal.Width;
            MaxHeight = modal.MaxHeight;
            Modal.Skin(this);
            var skeleton = modal.Child;
            modal.Child = null;
            Child = skeleton;
            close.Click += (_, _) => shell.CloseModal();

            body.Children.Add(Row("Vox.Help.Update.CurrentVersion", _checker.CurrentVersionText));
            body.Children.Add(Row("Vox.Help.Update.Channel", TF0("Vox.Help.Update.ChannelValue")));
            body.Children.Add(_result);

            _goRelease.Click += (_, _) => Services.UpdateChecker.OpenReleasePage(_releaseUrl);
            _check.Click += (_, _) => _ = RunCheckAsync();
            _check[!Button.ContentProperty] = I18n.Bind("Vox.Help.Update.ActionCheck");
            footer.Children.Add(_goRelease);
            footer.Children.Add(_check);

            _goRelease[!Button.ContentProperty] = I18n.Bind("Vox.Help.Update.ActionDownload");
            _result.Text = TF0("Vox.Help.Update.Checking");
            _ = RunCheckAsync();
        }

        private async Task RunCheckAsync()
        {
            _check.IsEnabled = false;
            _goRelease.IsVisible = false;
            _result.Foreground = Res("VoxMuted");
            _result.Text = TF0("Vox.Help.Update.Checking");
            try
            {
                var result = await _checker.CheckAsync();
                if (result.HasUpdate)
                {
                    var notes = string.IsNullOrWhiteSpace(result.Notes)
                        ? string.Empty
                        : result.Notes.Length > 200 ? result.Notes[..200] + "…" : result.Notes;
                    _result.Foreground = Res("VoxFg");
                    _result.Text = TF("Vox.Help.Update.NewVersion", result.NewVersion)
                                   + (notes.Length > 0 ? "\n" + notes : string.Empty);
                    _releaseUrl = result.ReleaseUrl;
                    _goRelease.IsVisible = true;
                }
                else
                {
                    _result.Text = TF0("Vox.Help.Update.UpToDate");
                }
            }
            catch (Exception exception)
            {
                _result.Foreground = Res("VoxDanger");
                _result.Text = TF("Vox.Help.Update.Failed", exception.Message);
            }
            finally
            {
                _check.IsEnabled = true;
                _check[!Button.ContentProperty] = I18n.Bind("Vox.Help.Update.ActionRecheck");
            }
        }
    }

    private static string TF0(string key) => TF(key);
}
