using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using static Vox.App.Infrastructure.I18n;

using Vox.App.Infrastructure;
using Vox.App.Views;

namespace Vox.App.Dialogs;

/// <summary>帮助菜单弹层：关于。检查更新已升级为三段体验（标题栏药丸/日志气泡/更新窗），
/// 入口（帮助菜单/设置页）统一走 Services.UpdateSession.Instance.OpenFromMenu()。</summary>
public static class HelpDialogs
{
    private static IBrush? Res(string key) =>
        Application.Current!.TryFindResource(key, out var v) && v is IBrush b ? b : null;

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

    private static string TF0(string key) => TF(key);
}
