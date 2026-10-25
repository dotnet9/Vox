using Avalonia.Data;
using CodeWF.Avalonia.Lang.MarkupExtensions;
using CodeWF.Toolkit.Lang;

namespace Vox.App.Infrastructure;

/// <summary>I18n 快捷入口：T(ResourceKeys.X) 取当前语言文本，带 {0} 占位的用 TF。</summary>
public static class I18n
{
    /// <summary>键即强类型 ResourceKeys 属性值（string），返回当前语言文本；缺失时回退键名。</summary>
    public static string T(string key) => I18nManager.Instance.GetResource(key);

    public static string TF(string key, params object[] args) =>
        string.Format(I18nManager.Instance.GetResource(key), args);

    /// <summary>
    /// C# 构建视图的动态文案绑定（Vex {i18n:I18n} 标记扩展的代码等价）：
    /// 赋给目标属性（如 tb[!TextBlock.TextProperty] = I18n.Bind(key)）后随语言切换自动刷新，
    /// 无需重建页面。新键可直接传字符串 "Vox.Xxx.Yyy"，并入语言 JSON 后即生效。
    /// </summary>
    public static MultiBinding Bind(object key) => new I18nExtension(key).CreateBinding();
}
