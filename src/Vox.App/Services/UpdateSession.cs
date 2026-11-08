using Vox.App.Infrastructure;
using static Vox.App.Infrastructure.I18n;
using Vox.Core;

namespace Vox.App.Services;

/// <summary>更新会话状态（原型 updState）：checking → available → downloading → downloaded → installing；
/// 旁路 current（已是最新）/ canceled / failed（可重试）。installed 为安装程序拉起后的终态。</summary>
public enum UpdateState
{
    Idle,
    Checking,
    Current,
    Available,
    Downloading,
    Downloaded,
    Installing,
    Installed,
    Failed,
    Canceled,
}

/// <summary>
/// 更新会话的单一数据源（Vex ShellUpdateViewModel 的 Vox 移植，静态单例口径同 McpService/PublishHub）：
/// 标题栏药丸、日志气泡、更新窗与设置页/帮助菜单的「检查更新」四处共用这一个实例。
/// 启动静默检查发现新版只亮药丸；下载可后台（关窗/最小化不终止）；失败可重试；手动入口重新检查、恢复现场不重查。
/// </summary>
public sealed class UpdateSession
{
    public static UpdateSession Instance { get; } = new();

    private readonly UpdateChecker _checker = new();
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _operation;
    private UpdateCheckResult? _update;
    private string? _publishedDate;
    private string? _downloadPath;
    private bool _installLaunched;
    private UpdateState _state = UpdateState.Idle;
    private string? _error;
    private double _percent;
    private double _doneMB;
    private double _totalMB;
    private bool _startupWatched;
    private bool _windowPresented;

    /// <summary>任何状态变化后广播（UI 线程）：药丸/气泡/更新窗各自重渲染。语言切换由控件自行订阅 CultureChanged。</summary>
    public event Action? StateChanged;

    /// <summary>药丸「恢复更新窗」或气泡「立即更新」时请求宿主打开/置前更新窗（后台态恢复现场，不重新检查）。</summary>
    public event Action? WindowRequested;

    /// <summary>宿主注入的 toast 出口（MainWindow.ShowToast）。</summary>
    public Action<string>? Toast { get; set; }

    /// <summary>宿主注入的应用退出（安装程序拉起后由会话调用；IClassicDesktopStyleApplicationLifetime.Shutdown）。</summary>
    public Action? ShutdownApp { get; set; }

    private UpdateSession() { }

    // ---------- 状态与派生数据 ----------

    public UpdateState State => _state;
    public bool IsBusy => _state is UpdateState.Checking or UpdateState.Downloading or UpdateState.Installing;
    public bool IsDownloading => _state == UpdateState.Downloading;
    public string CurrentVersion => _checker.CurrentVersionText;
    public string? ReleaseUrl => _update?.ReleaseUrl;
    public string? DownloadPath => _downloadPath;
    public string? ErrorText => _error;
    public bool CanDownload => !string.IsNullOrWhiteSpace(_update?.Asset?.Url);

    /// <summary>新版本号去掉 tag 的 v 前缀（发布命名约定不是读者认知的版本号）。</summary>
    public string NewVersionText => (_update?.NewVersion ?? string.Empty).TrimStart('v', 'V');
    public bool HasUpdate => _update is { HasUpdate: true };

    /// <summary>下载进度（0-100，MB 口径给更新窗的数字读数）。总量未知时百分比为 0（由窗体按 0 处理）。</summary>
    public double Percent => _percent;
    public double DoneMB => _doneMB;
    public double TotalMB => _totalMB;
    public string ProgressMBText => string.Format(System.Globalization.CultureInfo.CurrentUICulture,
        T("Vox.Update.Win.ProgressMB"), _doneMB.ToString("F1"), _totalMB.ToString("F1"));

    /// <summary>最新 release 的发布日期（含已是最新时，供「当前 x（日期 发布）」展示）。</summary>
    public string? PublishedDate => _publishedDate;

    /// <summary>按 Vex 类型化口径解析的发布日志（版本分节），气泡/更新窗共用。</summary>
    public IReadOnlyList<UpdateNoteVersion> NoteSections => UpdateReleaseNotes.Parse(_update?.Notes);

    /// <summary>气泡/更新窗的扁平渲染模型（引用块 + 标签分组）；分组标题由控件按 TagKind 取界面语言文案。</summary>
    public UpdateBubbleNotes BuildBubble() => UpdateReleaseNotes.BuildBubble(NoteSections);

    /// <summary>更新窗当前是否「在场」：隐藏/最小化都算不在场——下载完成的 toast 只发一次且不在现场时发。</summary>
    public bool WindowPresented
    {
        get => _windowPresented;
        set => _windowPresented = value;
    }

    // ---------- 标题栏药丸（原型 pillSync：随状态变形） ----------

    public bool PillVisible => _state is UpdateState.Available or UpdateState.Downloading or UpdateState.Downloaded
        or UpdateState.Installing or UpdateState.Failed or UpdateState.Canceled;

    /// <summary>下载完成/安装中亮脉冲环，提示回到更新窗确认。</summary>
    public bool PillReady => _state is UpdateState.Downloaded or UpdateState.Installing;

    public string PillText => _state switch
    {
        UpdateState.Downloading => TF("Vox.Update.Pill.Downloading", Math.Round(_percent).ToString("0")),
        UpdateState.Downloaded or UpdateState.Installing => T("Vox.Update.Pill.Ready"),
        _ => T("Vox.Update.Pill.Found"),
    };

    public string PillTip => _state switch
    {
        UpdateState.Downloading => TF("Vox.Update.Pill.DownloadingTip", Math.Round(_percent).ToString("0")),
        UpdateState.Downloaded or UpdateState.Installing => T("Vox.Update.Pill.ReadyTip"),
        _ => HasUpdate ? TF("Vox.Update.Pill.FoundTip", NewVersionText) : T("Vox.Settings.About.CheckUpdate"),
    };

    /// <summary>药丸进度填充（0-100）：下载中在 accent-deep/accent 混色左段上露出推进。</summary>
    public double PillPercent => IsDownloading ? Math.Clamp(_percent, 0, 100) : 0;

    // ---------- 公共动作 ----------

    /// <summary>三个手动入口（帮助菜单 / 设置页 / 关于）统一走这里：恢复下载中的现场，否则开窗重新检查。</summary>
    public void OpenFromMenu()
    {
        WindowRequested?.Invoke();
        if (_state is UpdateState.Downloading or UpdateState.Downloaded or UpdateState.Installing) return;
        _ = CheckAsync();
    }

    /// <summary>更新窗每次打开时的补查：会话已有时不重查（恢复现场不重查）。</summary>
    public Task EnsureCheckedAsync() => _state == UpdateState.Idle ? CheckAsync() : Task.CompletedTask;

    /// <summary>启动后台静默检查（原型 1.4 秒后）：发现新版只亮药丸 + toast，不开窗；离线静默失败回待机。
    /// 代际守卫：用户在延迟窗口内已手动检查/开始下载时不打扰。</summary>
    public async Task RunStartupCheckAsync()
    {
        if (_startupWatched || _state != UpdateState.Idle) return;
        _startupWatched = true;
        await Task.Delay(1400);
        if (_state != UpdateState.Idle || _lifetime.IsCancellationRequested) return;
        await CheckCoreAsync(silent: true);
        if (_state == UpdateState.Available && !_windowPresented)
        {
            Toast?.Invoke(TF("Vox.Update.Toast.Found", NewVersionText));
        }
    }

    /// <summary>药丸在后台态点击：恢复更新窗现场（不重新检查）。</summary>
    public void RequestWindow() => WindowRequested?.Invoke();

    /// <summary>气泡「完整日志」：打开 GitHub Releases 页（Vox 无内置《更新日志》文档）。</summary>
    public void OpenFullChangelog() => UpdateChecker.OpenReleasePage(_update?.ReleaseUrl);

    /// <summary>气泡「立即更新」：日志已在气泡看过，直接进入下载并开更新窗显示进度（原型 upGo 口径）；
    /// 无安装资产时退回打开发布页。</summary>
    public Task StartDownloadFromBubbleAsync()
    {
        RequestWindow();
        if (_state != UpdateState.Available) return Task.CompletedTask;
        if (CanDownload) return DownloadAsync();
        UpdateChecker.OpenReleasePage(_update?.ReleaseUrl);
        return Task.CompletedTask;
    }

    public Task CheckAsync() => CheckCoreAsync(silent: false);

    public Task DownloadAsync()
    {
        // 无安装资产（未配自动化下载的 release）时退回打开发布页，不进下载状态机。
        if (_update?.Asset is null)
        {
            UpdateChecker.OpenReleasePage(_update?.ReleaseUrl);
            return Task.CompletedTask;
        }
        return RunAsync(UpdateState.Downloading, async token =>
        {
            _percent = 0;
            _doneMB = 0;
            _totalMB = _update.Asset.SizeBytes / 1048576.0;
            var progress = new Progress<UpdateDownloadProgress>(value =>
            {
                if (token.IsCancellationRequested || !IsDownloading) return;
                _percent = value.Percent;
                _doneMB = value.DoneMB;
                _totalMB = value.TotalMB;
                StateChanged?.Invoke();
            });
            _downloadPath = await _checker.DownloadAssetAsync(_update.Asset, progress, token);
            token.ThrowIfCancellationRequested();
            SetState(UpdateState.Downloaded);
            // 下载中关窗/最小化不终止下载：后台完成只 toast，不打断写作（原型 layer-update.hidden 判定）。
            if (!_windowPresented)
            {
                Toast?.Invoke(T("Vox.Update.Toast.Ready"));
            }
        });
    }

    /// <summary>取消下载：半成品文件保留，可随时重新开始。</summary>
    public void Cancel() => _operation?.Cancel();

    /// <summary>失败后的主按钮分派：已有安装包先安装；有可下载资产重新下载；否则重新检查
    /// （DownloadAsync 内部对无资产的情形退回打开发布页）。</summary>
    public Task RetryAsync() => _downloadPath is not null
        ? RestartToInstallAsync()
        : HasUpdate ? DownloadAsync() : CheckAsync();

    /// <summary>「重启以更新」：拉起下载的安装程序，成功后 Vox 退出；压缩包无握手时打开所在文件夹转手动安装。</summary>
    public Task RestartToInstallAsync() => RunAsync(UpdateState.Installing, async token =>
    {
        token.ThrowIfCancellationRequested();
        if (string.IsNullOrEmpty(_downloadPath) || !File.Exists(_downloadPath))
        {
            throw new InvalidOperationException(TF("Vox.Update.Toast.InstallFailed", _downloadPath ?? string.Empty));
        }
        var ext = Path.GetExtension(_downloadPath).ToLowerInvariant();
        if (ext is not (".exe" or ".msi" or ".dmg" or ".pkg" or ".deb" or ".appimage"))
        {
            // 非安装器资产（zip 等）：没有安装器可交接，打开所在文件夹请用户手动完成（安装器握手是后续里程碑）。
            _installLaunched = false;
            OpenDownloadFolder();
            SetState(UpdateState.Installed);
            return;
        }
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_downloadPath)
        {
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(_downloadPath))!,
        });
        if (process is null) throw new InvalidOperationException(TF("Vox.Update.Toast.InstallFailed", _downloadPath));
        _installLaunched = true;
        SetState(UpdateState.Installed);
        // 给安装程序一点拉起时间再退出宿主（原型 installing 文案：Vox 将退出，安装在独立安装程序中继续）。
        await Task.Delay(1200, token);
        ShutdownApp?.Invoke();
    });

    /// <summary>安装程序是否已确认拉起（Installed 态的副标题分岔：拉起→提示退出；手动→提示文件夹）。</summary>
    public bool InstallLaunched => _installLaunched;

    /// <summary>打开下载文件所在文件夹（资源管理器选中该文件；非 Windows 退回打开文件夹）。</summary>
    public void OpenDownloadFolder()
    {
        if (string.IsNullOrEmpty(_downloadPath)) return;
        try
        {
            if (OperatingSystem.IsWindows())
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe",
                    $"/select,\"{_downloadPath}\"") { UseShellExecute = true });
            }
            else
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                    Path.GetDirectoryName(_downloadPath)!) { UseShellExecute = true });
            }
        }
        catch
        {
            // 文件夹打不开不打扰主流程（下载文件仍在临时目录）。
        }
    }

    // ---------- 状态机内部 ----------

    private async Task CheckCoreAsync(bool silent)
    {
        if (IsBusy) return;
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _operation = operation;
        _error = null;
        SetState(UpdateState.Checking);
        try
        {
            _update = null;
            _downloadPath = null;
            _installLaunched = false;
            var result = await _checker.CheckAsync(operation.Token);
            operation.Token.ThrowIfCancellationRequested();
            _update = result.HasUpdate ? result : null;
            _publishedDate = result.PublishedDate;
            SetState(result.HasUpdate ? UpdateState.Available : UpdateState.Current);
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested)
        {
            SetState(silent ? UpdateState.Idle : UpdateState.Canceled);
        }
        catch (Exception error)
        {
            // 静默检查失败不进失败态：保持待机（离线静默失败），手动检查才向用户暴露错误。
            if (silent) SetState(UpdateState.Idle);
            else
            {
                _error = error.Message;
                SetState(UpdateState.Failed);
            }
        }
        finally
        {
            _operation = null;
        }
    }

    private async Task RunAsync(UpdateState state, Func<CancellationToken, Task> action)
    {
        if (IsBusy) return;
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _operation = operation;
        _error = null;
        SetState(state);
        try
        {
            await action(operation.Token);
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested)
        {
            SetState(UpdateState.Canceled);
        }
        catch (Exception error)
        {
            _error = error.Message;
            SetState(UpdateState.Failed);
        }
        finally
        {
            _operation = null;
        }
    }

    private void SetState(UpdateState state)
    {
        _state = state;
        StateChanged?.Invoke();
    }
}
