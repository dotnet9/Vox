using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using Vox.App.Infrastructure;
using Vox.App.Views;
using Vox.Core;
using Vox.Core.Mcp;

namespace Vox.App.Services;

/// <summary>
/// MCP 服务进程内单例：按设置装配 <see cref="McpServer"/>（业务引擎）与 <see cref="McpWebHost"/>
/// （SDK 传输宿主），向页面广播启停状态与审计记录；仓库扫描复用 <see cref="AppState.Scan"/>，
/// 写入后统一重扫并失效发布缓存。
/// </summary>
public static class McpService
{
    private static readonly object Gate = new();
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(5) };
    private static McpServer? _engine;
    private static McpWebHost? _host;

    public static event Action? StateChanged;
    public static event Action<ToolAuditEntry>? CallAudited;

    public static bool IsRunning
    {
        get
        {
            lock (Gate) return _host?.IsRunning == true;
        }
    }

    public static string? LastError { get; private set; }

    public static IReadOnlyList<McpClientSnapshot> Clients
    {
        get
        {
            lock (Gate) return _host?.Clients ?? [];
        }
    }

    public static string Endpoint =>
        IsRunning ? _host!.Endpoint : $"http://127.0.0.1:{AppState.Instance.Settings.McpPort}/mcp/";

    public static int VisibleToolCount
    {
        get
        {
            lock (Gate) return _engine?.VisibleToolCount ?? McpCatalog.VisibleTools(BuildOptions()).Count;
        }
    }

    public static McpOptions BuildOptions()
    {
        var settings = AppState.Instance.Settings;
        return new McpOptions
        {
            RepoRoot = AppState.Instance.CurrentRepoRoot ?? string.Empty,
            Port = settings.McpPort,
            Token = settings.McpToken,
            Scope = McpScopes.Parse(settings.McpScope),
            ConfirmWrites = settings.McpConfirmWrites,
            AllowGit = settings.McpAllowGit,
            DisabledTools = settings.McpDisabledTools,
            CdnBase = settings.CdnBase,
            Version = typeof(McpService).Assembly.GetName().Version?.ToString(3) ?? "0.1.0",
        };
    }

    public static McpServer Ensure()
    {
        lock (Gate)
        {
            if (_engine != null) return _engine;
            _engine = new McpServer(BuildOptions(), new McpHostBridge(), () => AppState.Instance.Scan);
            _engine.CallAudited += entry => CallAudited?.Invoke(entry);
            _host = new McpWebHost();
            _host.CallAudited += entry => CallAudited?.Invoke(entry);
            _host.StateChanged += () => StateChanged?.Invoke();
            return _engine;
        }
    }

    /// <summary>启动时调用：设置里启用且仓库就绪才拉起监听，失败只记录原因不阻塞启动。</summary>
    public static void AutoStart()
    {
        if (!AppState.Instance.Settings.McpEnabled || !AppState.Instance.HasRepo) return;
        Start();
    }

    public static bool Start()
    {
        LastError = null;
        try
        {
            var settings = AppState.Instance.Settings;
            if (string.IsNullOrWhiteSpace(settings.McpToken))
            {
                settings.McpToken = McpTokens.New();
                AppState.Instance.SaveSettings();
            }
            var engine = Ensure();
            var options = BuildOptions();
            engine.UpdateOptions(options);
            lock (Gate)
            {
                if (_host is not { IsRunning: true }) _host!.Start(options, engine);
            }
            StateChanged?.Invoke();
            return true;
        }
        catch (Exception error)
        {
            LastError = error.Message;
            try
            {
                _host?.Stop();
            }
            catch
            {
                // 已在停止路径
            }
            StateChanged?.Invoke();
            return false;
        }
    }

    public static void Stop()
    {
        try
        {
            lock (Gate) _host?.Stop();
            LastError = null;
        }
        catch (Exception error)
        {
            LastError = error.Message;
        }
        StateChanged?.Invoke();
    }

    public static bool Restart()
    {
        LastError = null;
        try
        {
            var engine = Ensure();
            engine.UpdateOptions(BuildOptions());
            Stop();
            if (!Start()) throw new InvalidOperationException(LastError ?? "重启失败");
            StateChanged?.Invoke();
            return true;
        }
        catch (Exception error)
        {
            LastError = error.Message;
            try
            {
                Stop();
            }
            catch
            {
                // 停止失败不覆盖启动错误
            }
            StateChanged?.Invoke();
            return false;
        }
    }

    /// <summary>授权范围 / 写确认 / git 总开关 / 工具上下线：不需要重启。</summary>
    public static void UpdateHot()
    {
        lock (Gate)
        {
            _engine?.UpdateOptions(BuildOptions());
        }
        StateChanged?.Invoke();
    }

    /// <summary>服务启停等系统事件同样进入调用审计（原型「实时调用」可见）。</summary>
    public static void AuditSystem(string target, bool ok, string? detail = null)
    {
        var entry = new ToolAuditEntry
        {
            Time = DateTime.Now,
            Client = "系统",
            Tool = "—",
            Target = target,
            Result = ok ? "ok" : "deny",
            Detail = detail,
            DurationMs = 0,
        };
        ToolAuditStore.Append(entry);
        CallAudited?.Invoke(entry);
    }

    /// <summary>页面「测试连接 / 模拟调用」走真实 HTTP 端点（真实鉴权、确认与审计）。</summary>
    public static async Task<(bool Ok, string Body)> SendAsync(JsonObject payload, string? session = null)
    {
        if (!IsRunning) return (false, "服务未启动");
        var settings = AppState.Instance.Settings;
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.McpToken);
        // MCP 规范要求 POST 的 Accept 同时含 application/json 与 text/event-stream，缺失会被官方 SDK 拒绝（406）。
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        if (!string.IsNullOrWhiteSpace(session))
        {
            request.Headers.TryAddWithoutValidation("Mcp-Session-Id", session);
        }
        try
        {
            using var response = await Http.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            return ((int)response.StatusCode == 200, body);
        }
        catch (Exception error)
        {
            return (false, error.Message);
        }
    }
}
