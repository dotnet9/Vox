using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using ModelContextProtocol.Protocol;
using Vox.Core;
using Vox.Core.Mcp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using McpSdk = ModelContextProtocol.Server;

namespace Vox.Core.Mcp;

/// <summary>
/// 进程内 MCP 传输宿主：Kestrel 监听 127.0.0.1，/mcp 端点承载 ModelContextProtocol.AspNetCore
/// 的 Streamable HTTP 传输（会话握手、协议版本协商由 SDK 处理），Bearer 鉴权在路由前拦截。
/// 客户端识别在中间件完成（initialize 报文里的 clientInfo.name 与 Mcp-Session-Id 响应头绑定）。
/// 工具 / 资源 / 指令的业务实现仍在 <see cref="McpServer"/>（Vox.Core），本类只做协议桥接与客户端快照。
/// </summary>
public sealed class McpWebHost : IDisposable
{
    private sealed record ClientTrack(string SessionId, string Client, DateTime ConnectedAt, DateTime LastSeen, int Calls);

    /// <summary>当前请求绑定的客户端名：中间件从会话映射写入，SDK handler 读取用于审计。</summary>
    private static readonly AsyncLocal<string?> CurrentClient = new();

    private WebApplication? _app;
    private Task? _runTask;
    private CancellationTokenSource? _cts;
    private readonly ConcurrentDictionary<string, ClientTrack> _clients = new(StringComparer.Ordinal);
    private string _auditPath = string.Empty;
    private bool _disposed;

    public event Action? StateChanged;

    /// <summary>宿主自身的审计（鉴权失败等）；工具调用的审计由引擎 <see cref="McpServer.CallAudited"/> 负责。</summary>
    public event Action<ToolAuditEntry>? CallAudited;

    public bool IsRunning => _app != null;

    public string Endpoint { get; private set; } = string.Empty;

    /// <summary>最近 30 分钟有活动的客户端快照（导航角标与客户端页）。</summary>
    public IReadOnlyList<McpClientSnapshot> Clients
    {
        get
        {
            var cutoff = DateTime.Now.AddMinutes(-30);
            return _clients.Values
                .Where(c => c.LastSeen >= cutoff)
                .OrderBy(c => c.ConnectedAt)
                .Select(c => new McpClientSnapshot(c.Client, c.ConnectedAt, c.LastSeen, c.Calls))
                .ToList();
        }
    }

    public void Start(McpOptions options, McpServer engine)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_app != null) return;
        McpServer.Validate(options);
        _auditPath = options.AuditPath ?? string.Empty;
        var token = options.Token.Trim();

        var builder = WebApplication.CreateBuilder();
        builder.Services
            .AddMcpServer(o =>
            {
                o.ServerInfo = new Implementation
                {
                    Name = "vox",
                    Title = "Vox·维声",
                    Version = options.Version,
                };
                o.ServerInstructions =
                    "Vox 内容仓库的本地 MCP 服务：只读工具直接放行，写入工具需在 Vox 应用内确认；" +
                    "授权范围与工具开关由 Vox 的 MCP 服务页面控制。";
            })
            .WithHttpTransport(o => o.SessionMode = ModelContextProtocol.AspNetCore.HttpServerSessionMode.Stateful)
            .WithListToolsHandler((_, _) => ValueTask.FromResult<ListToolsResult>(engine.ListTools()))
            .WithCallToolHandler((ctx, ct) => HandleCallAsync(engine, ctx, ct))
            .WithListResourcesHandler((_, _) => ValueTask.FromResult<ListResourcesResult>(engine.ListResources()))
            .WithReadResourceHandler((ctx, _) => ValueTask.FromResult<ReadResourceResult>(
                engine.ReadResource(new JsonObject { ["uri"] = ctx.Params?.Uri })))
            .WithListPromptsHandler((_, _) => ValueTask.FromResult<ListPromptsResult>(engine.ListPrompts()))
            .WithGetPromptHandler((ctx, _) => ValueTask.FromResult<GetPromptResult>(
                engine.GetPrompt(ToPromptParams(ctx.Params))))
            .WithListResourceTemplatesHandler((_, _) =>
                ValueTask.FromResult<ListResourceTemplatesResult>(new ListResourceTemplatesResult()));

        _app = builder.Build();
        // 端口 0 = 测试场景动态端口；启动后从服务器地址特性回读实际绑定值
        _app.Urls.Add($"http://127.0.0.1:{options.Port}");
        // 路由前中间件：Bearer 鉴权 → 401；放行后缓冲报文体，识别客户端并维护会话快照。
        _app.Use(async (context, next) =>
        {
            if (!IsMcpPath(context.Request.Path))
            {
                await next(context);
                return;
            }
            if (!Authorized(context.Request.Headers.Authorization.ToString(), token))
            {
                Audit("未知客户端", "—", "Bearer 令牌无效", "error", "HTTP 401", 0);
                context.Response.StatusCode = 401;
                context.Response.Headers.WWWAuthenticate = "Bearer";
                await context.Response.CompleteAsync();
                return;
            }

            var sessionId = context.Request.Headers["Mcp-Session-Id"].ToString();
            if (!HttpMethods.IsPost(context.Request.Method))
            {
                // GET（SSE 监听）/ DELETE（会话终止）：仅刷新活跃时间。
                if (!string.IsNullOrEmpty(sessionId)) Touch(sessionId, null, called: false);
                CurrentClient.Value = LookupName(sessionId);
                await next(context);
                return;
            }

            context.Request.EnableBuffering();
            string body;
            using (var reader = new StreamReader(context.Request.Body, leaveOpen: true))
            {
                body = await reader.ReadToEndAsync();
            }
            context.Request.Body.Position = 0;

            JsonNode? message = null;
            try
            {
                message = string.IsNullOrEmpty(body) ? null : JsonNode.Parse(body);
            }
            catch
            {
                // 非法报文交由 SDK 返回协议错误，这里只影响客户端识别
            }
            var method = message?["method"]?.GetValue<string>();
            var clientName = message?["params"]?["clientInfo"]?["name"]?.GetValue<string>();
            CurrentClient.Value = clientName ?? LookupName(sessionId);
            await next(context);

            // 会话 ID 在 initialize 响应头里分配；此后客户端回传 Mcp-Session-Id 头。
            var assigned = context.Response.Headers["Mcp-Session-Id"].ToString();
            Touch(string.IsNullOrEmpty(assigned) ? sessionId : assigned, clientName, called: method == "tools/call");
        });
        _app.MapMcp("/mcp");

        _cts = new CancellationTokenSource();
        _app.StartAsync(_cts.Token).GetAwaiter().GetResult();
        var bound = _app.Urls.FirstOrDefault(u => u.StartsWith("http://127.0.0.1", StringComparison.OrdinalIgnoreCase));
        Endpoint = $"{(string.IsNullOrEmpty(bound) ? $"http://127.0.0.1:{options.Port}" : bound.TrimEnd('/'))}/mcp/";
        _runTask = _app.WaitForShutdownAsync(_cts.Token);
        StateChanged?.Invoke();
    }

    public void Stop() => StopAsync().GetAwaiter().GetResult();

    public async Task StopAsync()
    {
        WebApplication? app;
        CancellationTokenSource? cts;
        lock (this)
        {
            app = _app;
            _app = null;
            cts = _cts;
            _cts = null;
        }
        Endpoint = string.Empty;
        if (app == null) return;
        try
        {
            await app.StopAsync().ConfigureAwait(false);
        }
        catch
        {
            // 已停止
        }
        try
        {
            await app.DisposeAsync().ConfigureAwait(false);
        }
        catch
        {
            // 已释放
        }
        _clients.Clear();
        StateChanged?.Invoke();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            Stop();
        }
        catch
        {
            // 析构路径吞掉停止异常
        }
        _cts?.Dispose();
    }

    private static bool IsMcpPath(PathString path) =>
        path.StartsWithSegments("/mcp");

    private static bool Authorized(string? header, string token)
    {
        if (string.IsNullOrWhiteSpace(header)) return false;
        const string prefix = "Bearer ";
        if (!header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
        var supplied = Encoding.UTF8.GetBytes(header[prefix.Length..].Trim());
        var expected = Encoding.UTF8.GetBytes(token);
        return supplied.Length == expected.Length && CryptographicOperations.FixedTimeEquals(supplied, expected);
    }

    private async ValueTask<CallToolResult> HandleCallAsync(
        McpServer engine, McpSdk.RequestContext<CallToolRequestParams> context, CancellationToken ct)
    {
        var client = CurrentClient.Value ?? "MCP 客户端";
        var parameters = new JsonObject { ["name"] = context.Params?.Name };
        if (context.Params?.Arguments is { Count: > 0 } args)
        {
            var json = new JsonObject();
            foreach (var (key, value) in args)
            {
                json[key] = JsonNode.Parse(value.GetRawText());
            }
            parameters["arguments"] = json;
        }
        return await engine.CallTool(parameters, client, ct).ConfigureAwait(false);
    }

    private static JsonObject? ToPromptParams(GetPromptRequestParams? parameters)
    {
        if (parameters == null) return null;
        var json = new JsonObject { ["name"] = parameters.Name };
        if (parameters.Arguments is { Count: > 0 } args)
        {
            var inner = new JsonObject();
            foreach (var (key, value) in args)
            {
                inner[key] = JsonSerializer.SerializeToNode(value);
            }
            json["arguments"] = inner;
        }
        return json;
    }

    private string LookupName(string sessionId) =>
        _clients.TryGetValue(sessionId, out var track) ? track.Client : "MCP 客户端";

    private void Touch(string sessionId, string? clientName, bool called)
    {
        if (string.IsNullOrEmpty(sessionId)) return;
        _clients.AddOrUpdate(
            sessionId,
            _ => new ClientTrack(sessionId, clientName ?? "MCP 客户端", DateTime.Now, DateTime.Now, called ? 1 : 0),
            (_, existing) => existing with
            {
                Client = clientName ?? existing.Client,
                LastSeen = DateTime.Now,
                Calls = existing.Calls + (called ? 1 : 0),
            });
    }

    private void Audit(string client, string tool, string target, string result, string? detail, long durationMs)
    {
        var entry = new ToolAuditEntry
        {
            Time = DateTime.Now,
            Client = client,
            Tool = tool,
            Target = target,
            Result = result,
            Detail = detail,
            DurationMs = (int)durationMs,
        };
        ToolAuditStore.Append(entry, _auditPath);
        CallAudited?.Invoke(entry);
    }
}
