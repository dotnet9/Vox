using System.Globalization;
using CodeWF.Toolkit.Lang;
using CodeWF.Toolkit.Lang.Json;
using Vox.Core;

namespace Vox.App.Infrastructure;

/// <summary>启动装配：设置 → I18n → 仓库状态。语言切换经 <see cref="I18nManager.Culture"/> 全局生效。</summary>
public static class AppBootstrap
{
    public static void Initialize()
    {
        var settings = SettingsStore.Load();

        var plugin = new JsonLangPlugin
        {
            ResourceFolder = Path.Combine(AppContext.BaseDirectory, "I18n"),
        };
        I18nManager.Instance.Register(plugin, CultureInfo.GetCultureInfo(settings.Language));
        I18nManager.Instance.Culture = CultureInfo.GetCultureInfo(settings.Language);
    }
}

/// <summary>应用级共享状态：设置、当前仓库索引、git 服务。页面直接订阅 <see cref="ScanChanged"/>。</summary>
public sealed class AppState
{
    public static AppState Instance { get; } = new();

    public VoxSettings Settings { get; private set; } = new();

    public RepoScan? Scan { get; private set; }

    /// <summary>索引正在构建（同步页 / 设置页禁用按钮用）。</summary>
    public bool IsScanning { get; private set; }

    public event Action? ScanChanged;

    public bool HasRepo => !string.IsNullOrWhiteSpace(Settings.RepoPath) && Directory.Exists(Settings.RepoPath);

    public void LoadSettings() => Settings = SettingsStore.Load();

    public void SaveSettings() => SettingsStore.Save(Settings);

    /// <summary>设置仓库路径并重建索引；失败时保留旧索引。</summary>
    public void OpenRepo(string path)
    {
        Settings.RepoPath = path;
        SaveSettings();
        Rescan();
    }

    public void Rescan()
    {
        if (!HasRepo)
        {
            Scan = null;
            ScanChanged?.Invoke();
            return;
        }
        try
        {
            IsScanning = true;
            Scan = RepoScanner.Scan(Settings.RepoPath!, Settings.Link);
        }
        finally
        {
            IsScanning = false;
        }
        ScanChanged?.Invoke();
    }

    /// <summary>文件级变更后只重扫受影响部分太复杂——M1 直接全量重建（536 篇规模亚秒级）。</summary>
    public void NotifyRepoChanged() => Rescan();

    public Git.GitService? Git() => HasRepo ? new Git.GitService(Settings.RepoPath!) : null;

    public string? CurrentRepoRoot => HasRepo ? Settings.RepoPath : null;
}
