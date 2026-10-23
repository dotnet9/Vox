using CodeWF.Toolkit.Lang;

namespace Vox.App.Infrastructure;

/// <summary>I18n 快捷入口：T(ResourceKeys.X) 取当前语言文本，带 {0} 占位的用 TF。</summary>
public static class I18n
{
    /// <summary>键即强类型 ResourceKeys 属性值（string），返回当前语言文本；缺失时回退键名。</summary>
    public static string T(string key) => I18nManager.Instance.GetResource(key);

    public static string TF(string key, params object[] args) =>
        string.Format(I18nManager.Instance.GetResource(key), args);
}
