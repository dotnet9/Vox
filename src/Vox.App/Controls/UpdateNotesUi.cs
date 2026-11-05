using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Vox.App.Infrastructure;
using Vox.App.Services;
using static Vox.App.Infrastructure.I18n;
using Vox.Core;

namespace Vox.App.Controls;

/// <summary>更新日志的公共渲染器（原型 .chg-*，气泡与更新窗共用）：
/// [系统要求]/[注意] 引用块 + 按标签分组（新增/优化/修复/删除四档语义色，词表外条目归「其他」组）。</summary>
internal static class UpdateNotesUi
{
    /// <summary>类型键 → 分组标题资源键；null 走「其他」。</summary>
    private static string GroupTitleKey(string? tagKind) => tagKind switch
    {
        "new" => "Vox.Update.Group.New",
        "opt" => "Vox.Update.Group.Opt",
        "fix" => "Vox.Update.Group.Fix",
        "remove" => "Vox.Update.Group.Remove",
        _ => "Vox.Update.Group.Others",
    };

    private static string GroupClass(string? tagKind) => tagKind switch
    {
        "new" => "t-new",
        "opt" => "t-opt",
        "fix" => "t-fix",
        "remove" => "t-rm",
        _ => string.Empty,
    };

    /// <summary>把会话的发布日志渲染成可滚动内容（引用块 + 分组列表）。</summary>
    public static Control Build(UpdateSession session)
    {
        var notes = session.BuildBubble();
        var host = new StackPanel();
        foreach (var quote in notes.Quotes)
        {
            host.Children.Add(new Border
            {
                Classes = { "chg-quote" },
                Child = new TextBlock { Classes = { "chg-quote" }, Text = quote },
            });
        }
        foreach (var group in notes.Groups)
        {
            var section = new StackPanel { Classes = { "chg-sec" } };
            var kindClass = GroupClass(group.TagKind);
            if (kindClass.Length > 0) section.Classes.Add(kindClass);
            var title = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
            title[!TextBlock.TextProperty] = I18n.Bind(GroupTitleKey(group.TagKind));
            section.Children.Add(new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 6,
                Children = { new Border { Classes = { "chg-dot" } }, title },
            });
            var items = new StackPanel { Margin = new Thickness(14, 5, 0, 0) };
            foreach (var item in group.Items)
            {
                items.Children.Add(new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("Auto,*"),
                    Margin = new Thickness(0, 3),
                    Children =
                    {
                        new TextBlock { Classes = { "chg-mark" }, Text = "•" },
                        new TextBlock { Classes = { "chg-item" }, Text = item, [Grid.ColumnProperty] = 1 },
                    },
                });
            }
            section.Children.Add(items);
            host.Children.Add(section);
        }
        return host;
    }
}
