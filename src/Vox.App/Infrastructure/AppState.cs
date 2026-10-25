using System.Globalization;
using CodeWF.Toolkit.Lang;
using CodeWF.Toolkit.Lang.Json;
using Vox.Core;

namespace Vox.App.Infrastructure;

/// <summary>启动装配：设置 → I18n → 仓库状态。语言切换经 <see cref="I18nManager.Culture"/> 全局生效。</summary>
public static class AppBootstrap
{
    /// <summary>命令行传入的会话级仓库目录（目录/文件两种入口共用），不回写设置。</summary>
    public static string? SessionRepoPath { get; private set; }

    /// <summary>命令行传入的待定位文件（打开文件入口），首个索引完成后自动打开。</summary>
    public static string? SessionOpenFile { get; private set; }

    /// <summary>会话级仓库覆盖生效中（--empty / 目录 / 文件），保存设置时还原原值，避免污染全局配置。</summary>
    public static bool RepoOverridden { get; private set; }

    /// <summary>覆盖前的全局设置仓库路径。</summary>
    public static string? OriginalRepoPath { get; private set; }

    public static void Initialize(string[]? args = null)
    {
        var settings = SettingsStore.Load();
        OriginalRepoPath = settings.RepoPath;

        // 启动参数口径：--empty=空白进程（不加载仓库）；目录=本进程管理该目录；
        // 文件=管理所在目录并定位到该文件。均为会话级覆盖，不写入全局设置。
        foreach (var raw in args ?? Array.Empty<string>())
        {
            if (raw == "--empty")
            {
                settings.RepoPath = null;
                RepoOverridden = true;
                break;
            }
            if (Directory.Exists(raw))
            {
                SessionRepoPath = Path.GetFullPath(raw);
                settings.RepoPath = SessionRepoPath;
                RepoOverridden = true;
                break;
            }
            if (File.Exists(raw))
            {
                SessionOpenFile = Path.GetFullPath(raw);
                SessionRepoPath = Path.GetDirectoryName(SessionOpenFile);
                settings.RepoPath = SessionRepoPath;
                RepoOverridden = true;
                break;
            }
        }

        var plugin = new JsonLangPlugin
        {
            ResourceFolder = Path.Combine(AppContext.BaseDirectory, "I18n"),
        };
        I18nManager.Instance.Register(plugin, CultureInfo.GetCultureInfo(settings.Language));
        I18nManager.Instance.Culture = CultureInfo.GetCultureInfo(settings.Language);
    }
}

/// <summary>应用级共享状态：设置、当前仓库索引、git 服务。页面直接订阅 <see cref="AppState.ScanChanged"/>。</summary>
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

    /// <summary>持久化设置；会话级仓库覆盖生效时，回写文件用原仓库路径（覆盖目录不入全局配置）。</summary>
    public void SaveSettings()
    {
        if (!AppBootstrap.RepoOverridden)
        {
            SettingsStore.Save(Settings);
            return;
        }
        var sessionRepo = Settings.RepoPath;
        Settings.RepoPath = AppBootstrap.OriginalRepoPath;
        SettingsStore.Save(Settings);
        Settings.RepoPath = sessionRepo; // 会话内继续使用覆盖目录
    }

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
