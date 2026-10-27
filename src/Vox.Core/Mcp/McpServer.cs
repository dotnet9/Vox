using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Vox.Core.Mcp;

/// <summary>
/// 本机 MCP 服务：127.0.0.1 上的极简 HTTP/1.1 端点（JSON-RPC 2.0），
/// 只接受 Bearer Token 鉴权的 POST，方法覆盖 initialize / tools / resources / prompts。
/// 自建 TcpListener 而不走 HttpListener：免 URL ACL、免管理员权限，AOT 下零额外依赖。
/// </summary>
public sealed partial class McpServer : IDisposable
{
    private const int MaxBodyBytes = 2 * 1024 * 1024;
    private const int MaxHeaderBytes = 32 * 1024;

    private readonly object _gate = new();
    private readonly object _sessionGate = new();
    private readonly Dictionary<string, Session> _sessions = new(StringComparer.Ordinal);
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private McpOptions _options;
    private int _port;

    public McpServer(McpOptions options, IMcpHost? host = null, Func<RepoScan?>? scanProvider = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        Host = host;
        ScanProvider = scanProvider;
    }

    /// <summary>每次写入审计（含被拒绝与鉴权失败），界面「实时调用」与「调用审计」共用。</summary>
    public event Action<ToolAuditEntry>? CallAudited;

    public IMcpHost? Host { get; }

    /// <summary>扫描源：界面传入 AppState.Scan 复用内存索引；测试可传临时仓库扫描。</summary>
    public Func<RepoScan?>? ScanProvider { get; set; }

    public McpOptions Options => _options;

    public bool IsRunning
    {
        get
        {
            lock (_gate) return _listener != null;
        }
    }

    public int Port
    {
        get
        {
            lock (_gate) return _listener == null ? _options.Port : _port;
        }
    }

    public string Endpoint => $"http://127.0.0.1:{Port}/mcp/";

    public int VisibleToolCount => McpCatalog.VisibleTools(_options).Count;

    public void Start()
    {
        lock (_gate)
        {
            if (_listener != null) return;
            Validate(_options);
            var listener = new TcpListener(IPAddress.Loopback, _options.Port);
            listener.Start();
            _listener = listener;
            _port = ((IPEndPoint)listener.LocalEndpoint).Port;
            _cts = new CancellationTokenSource();
            _ = Task.Run(() => AcceptLoopAsync(listener, _cts.Token));
        }
    }

    public void Stop()
    {
        TcpListener? listener;
        CancellationTokenSource? cts;
        lock (_gate)
        {
            listener = _listener;
            cts = _cts;
            _listener = null;
            _cts = null;
        }
        try
        {
            cts?.Cancel();
        }
        catch
        {
            // 已释放
        }
        try
        {
            listener?.Stop();
        }
        catch
        {
            // 已停止
        }
        cts?.Dispose();
        lock (_sessionGate) _sessions.Clear();
    }

    public void Restart()
    {
        Stop();
        Start();
    }

    /// <summary>热更新授权范围 / 开关 / 工具上下线；端口与令牌在下次 Restart 生效。</summary>
    public void UpdateOptions(McpOptions options) => _options = options;

    /// <summary>最近 30 分钟内有请求的已初始化客户端（导航角标与客户端页）。</summary>
    public IReadOnlyList<McpClientSnapshot> Clients()
    {
        if (!IsRunning) return [];
        var cutoff = DateTime.Now.AddMinutes(-30);
        lock (_sessionGate)
        {
            return _sessions.Values
                .Where(s => s.LastSeen >= cutoff)
                .OrderBy(s => s.ConnectedAt)
                .Select(s => new McpClientSnapshot(s.Name, s.ConnectedAt, s.LastSeen, s.Calls))
                .ToList();
        }
    }

    public void Dispose() => Stop();

    private static void Validate(McpOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.RepoRoot) || !Directory.Exists(options.RepoRoot))
        {
            throw new InvalidOperationException("内容仓库未配置或不存在，MCP 服务无法启动。");
        }
        if (options.Port is < 0 or > 65535)
        {
            throw new InvalidOperationException("端口须为 1024–65535 的整数。");
        }
        if (options.Token.Trim().Length < 16)
        {
            throw new InvalidOperationException("访问令牌至少 16 个字符，请先生成令牌。");
        }
    }

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (SocketException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            _ = Task.Run(() => HandleConnectionAsync(client, ct), CancellationToken.None);
        }
    }

    private async Task HandleConnectionAsync(TcpClient client, CancellationToken ct)
    {
        using (client)
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(45));
            try
            {
                client.NoDelay = true;
                await using var stream = client.GetStream();
                var request = await ReadRequestAsync(stream, timeout.Token).ConfigureAwait(false);
                if (request == null) return;
                var response = await ProcessAsync(request, timeout.Token).ConfigureAwait(false);
                await WriteResponseAsync(stream, response, timeout.Token).ConfigureAwait(false);
            }
            catch (HttpError error)
            {
                try
                {
                    await WriteResponseAsync(client.GetStream(),
                        new McpHttpResponse(error.Status, null, "text/plain"), CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch
                {
                    // 连接已断
                }
            }
            catch (OperationCanceledException)
            {
                // 超时
            }
            catch (IOException)
            {
                // 客户端提前断开
            }
            catch (Exception)
            {
                // 单个连接失败不影响服务
            }
        }
    }

    private async Task<McpHttpResponse> ProcessAsync(HttpRequest request, CancellationToken ct)
    {
        if (request.Path is not ("/mcp" or "/mcp/"))
        {
            return new McpHttpResponse(404, null, "text/plain");
        }
        if (!string.Equals(request.Method, "POST", StringComparison.OrdinalIgnoreCase))
        {
            return new McpHttpResponse(405, null, "text/plain",
                new Dictionary<string, string> { ["Allow"] = "POST" });
        }
        if (!IsAuthorized(request.Headers.GetValueOrDefault("authorization")))
        {
            Audit("未知客户端", "—", "Bearer 令牌无效", "error", "HTTP 401", 0);
            return new McpHttpResponse(401, null, "text/plain",
                new Dictionary<string, string> { ["WWW-Authenticate"] = "Bearer" });
        }

        JsonNode? node;
        try
        {
            node = JsonNode.Parse(request.Body);
        }
        catch
        {
            return JsonRpcError(null, -32700, "Parse error");
        }
        if (node is not JsonObject message)
        {
            return JsonRpcError(null, -32600, "Invalid Request");
        }

        var method = McpJson.Str(message, "method");
        if (string.IsNullOrWhiteSpace(method))
        {
            return JsonRpcError(message["id"], -32600, "Invalid Request");
        }
        var isNotification = !message.ContainsKey("id") || message["id"] is null;
        var session = ResolveSession(request, message, method, out var newSessionId);
        var started = Stopwatch.GetTimestamp();

        try
        {
            var result = await DispatchAsync(method, message["params"] as JsonObject, session, started, ct)
                .ConfigureAwait(false);
            if (isNotification)
            {
                return new McpHttpResponse(202, null, null);
            }
            var response = new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = message["id"]?.DeepClone(),
                ["result"] = result,
            };
            return JsonResponse(response, newSessionId);
        }
        catch (RpcException error)
        {
            return isNotification
                ? new McpHttpResponse(202, null, null)
                : JsonRpcError(message["id"], error.Code, error.Message, newSessionId);
        }
        catch (Exception error)
        {
            return isNotification
                ? new McpHttpResponse(202, null, null)
                : JsonRpcError(message["id"], -32603, error.Message, newSessionId);
        }
    }

    private async Task<JsonObject> DispatchAsync(
        string method, JsonObject? parameters, Session session, long started, CancellationToken ct)
    {
        switch (method)
        {
            case "initialize":
                return Initialize(parameters, session, started);
            case "notifications/initialized":
            case "notifications/cancelled":
                return [];
            case "ping":
                return [];
            case "tools/list":
                return ListTools();
            case "tools/call":
                return await CallToolAsync(parameters, session, ct).ConfigureAwait(false);
            case "resources/list":
                return ListResources();
            case "resources/read":
                return ReadResource(parameters);
            case "prompts/list":
                return ListPrompts();
            case "prompts/get":
                return GetPrompt(parameters);
            default:
                throw new RpcException(-32601, $"Method not found: {method}");
        }
    }

    private JsonObject Initialize(JsonObject? parameters, Session session, long started)
    {
        var protocol = McpJson.Str(parameters, "protocolVersion") ?? "2024-11-05";
        var clientName = McpJson.Str(parameters?["clientInfo"] as JsonObject, "name");
        if (!string.IsNullOrWhiteSpace(clientName)) session.Name = clientName.Trim();
        var version = McpJson.Str(parameters?["clientInfo"] as JsonObject, "version");

        Audit(session.Name, "connect", "HTTP · 初始化握手", "ok",
            $"协议 {protocol} · 工具 {VisibleToolCount} 个" + (string.IsNullOrWhiteSpace(version) ? "" : $" · 客户端 {version}"),
            ElapsedMs(started));

        return new JsonObject
        {
            ["protocolVersion"] = protocol,
            ["capabilities"] = new JsonObject
            {
                ["tools"] = new JsonObject { ["listChanged"] = false },
                ["resources"] = new JsonObject { ["subscribe"] = false, ["listChanged"] = false },
                ["prompts"] = new JsonObject { ["listChanged"] = false },
            },
            ["serverInfo"] = new JsonObject
            {
                ["name"] = "vox",
                ["title"] = "Vox·维声",
                ["version"] = _options.Version,
            },
            ["instructions"] =
                "Vox 内容仓库的本地 MCP 服务：只读工具直接放行，写入工具需在 Vox 应用内确认；" +
                "授权范围与工具开关由 Vox 的 MCP 服务页面控制。",
        };
    }

    private Session ResolveSession(HttpRequest request, JsonObject message, string method, out string? newSessionId)
    {
        newSessionId = null;
        var now = DateTime.Now;

        if (string.Equals(method, "initialize", StringComparison.Ordinal))
        {
            var id = Guid.NewGuid().ToString("N");
            var session = new Session(id, ClientName(message["params"] as JsonObject), now);
            lock (_sessionGate) _sessions[id] = session;
            newSessionId = id;
            return session;
        }

        var sessionId = request.Headers.GetValueOrDefault("mcp-session-id");
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            lock (_sessionGate)
            {
                if (_sessions.TryGetValue(sessionId, out var existing))
                {
                    existing.LastSeen = now;
                    return existing;
                }
            }
        }

        var fallbackName = request.Headers.GetValueOrDefault("x-client-name");
        return new Session(sessionId ?? string.Empty,
            string.IsNullOrWhiteSpace(fallbackName) ? "MCP 客户端" : fallbackName.Trim(), now);
    }

    private static string ClientName(JsonObject? parameters)
    {
        var name = McpJson.Str(parameters?["clientInfo"] as JsonObject, "name");
        return string.IsNullOrWhiteSpace(name) ? "MCP 客户端" : name.Trim();
    }

    private JsonObject ListTools()
    {
        var tools = new JsonArray();
        foreach (var tool in McpCatalog.VisibleTools(_options))
        {
            tools.Add(new JsonObject
            {
                ["name"] = tool.Name,
                ["description"] = tool.Description,
                ["inputSchema"] = JsonNode.Parse(tool.SchemaJson),
                ["annotations"] = new JsonObject { ["readOnlyHint"] = tool.ReadOnly },
            });
        }
        return new JsonObject { ["tools"] = tools };
    }

    private JsonObject ListPrompts()
    {
        var prompts = new JsonArray();
        if (_options.Scope == McpAccessScope.Status)
        {
            return new JsonObject { ["prompts"] = prompts };
        }
        foreach (var prompt in McpCatalog.Prompts)
        {
            var args = new JsonArray();
            foreach (var argument in prompt.Arguments)
            {
                args.Add(new JsonObject
                {
                    ["name"] = argument,
                    ["description"] = argument == "article" ? "文章仓库相对路径" : "附加指令（可选）",
                    ["required"] = argument == "article",
                });
            }
            prompts.Add(new JsonObject
            {
                ["name"] = prompt.Name,
                ["title"] = prompt.Title,
                ["description"] = prompt.Description,
                ["arguments"] = args,
            });
        }
        return new JsonObject { ["prompts"] = prompts };
    }

    private JsonObject GetPrompt(JsonObject? parameters)
    {
        var name = McpJson.Str(parameters, "name");
        var prompt = McpCatalog.Prompts.FirstOrDefault(p => p.Name == name);
        if (prompt == null) throw new RpcException(-32602, "未知的预设指令。");
        if (_options.Scope == McpAccessScope.Status)
        {
            throw new RpcException(-32602, "当前授权范围仅状态查询，指令模板不可用。");
        }

        var args = parameters?["arguments"] as JsonObject ?? [];
        var article = McpJson.Str(args, "article");
        var instruction = McpJson.Str(args, "instruction");
        var system = prompt.Name switch
        {
            "wechat-rewrite" => "你是一名公众号编辑：把文章改写为适合公众号阅读的版本，保留事实、链接与代码块，输出完整 Markdown。",
            "summary-description" => "你是一名编辑：为文章写一段 80 字以内、可直接写入 front matter description 的中文摘要，只输出摘要。",
            "translate-en" => "You are a translator: translate the article into natural English, keeping Markdown structure, code blocks and links unchanged.",
            _ => "你是一名校对：逐条指出错别字、标点与病句，并给出修改建议；不要重写全文。",
        };
        var text = new StringBuilder();
        text.AppendLine(system);
        if (!string.IsNullOrWhiteSpace(article))
        {
            text.AppendLine();
            text.AppendLine($"文章路径：{article}");
            var fullPath = ResolveRepoPath(article);
            if (fullPath != null && File.Exists(fullPath))
            {
                var body = YamlSidecar.TrySplitFrontMatter(File.ReadAllText(fullPath), out _, out var b) ? b : string.Empty;
                if (body.Length > 6000) body = body[..6000] + "\n…（截断）";
                text.AppendLine();
                text.AppendLine(body.Trim());
            }
        }
        if (!string.IsNullOrWhiteSpace(instruction))
        {
            text.AppendLine();
            text.AppendLine($"附加指令：{instruction.Trim()}");
        }

        return new JsonObject
        {
            ["description"] = prompt.Description,
            ["messages"] = new JsonArray(new JsonObject
            {
                ["role"] = "user",
                ["content"] = new JsonObject { ["type"] = "text", ["text"] = text.ToString().TrimEnd() },
            }),
        };
    }

    private bool IsAuthorized(string? header)
    {
        if (string.IsNullOrWhiteSpace(header)) return false;
        const string prefix = "Bearer ";
        if (!header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
        var supplied = Encoding.UTF8.GetBytes(header[prefix.Length..].Trim());
        var expected = Encoding.UTF8.GetBytes(_options.Token.Trim());
        return supplied.Length == expected.Length && CryptographicOperations.FixedTimeEquals(supplied, expected);
    }

    private string? ResolveRepoPath(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath)) return null;
        var root = Path.GetFullPath(_options.RepoRoot);
        var full = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var prefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        return full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? full : null;
    }

    private static string? RelativePath(string root, string fullPath)
    {
        var relative = PathText.ToForwardSlashes(Path.GetRelativePath(root, fullPath));
        return relative.StartsWith("..", StringComparison.Ordinal) ? null : relative;
    }

    private static long ElapsedMs(long started) =>
        (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;

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
        ToolAuditStore.Append(entry, _options.AuditPath);
        CallAudited?.Invoke(entry);
    }

    // ---------- HTTP 解析 / 响应 ----------

    private sealed record HttpRequest(
        string Method,
        string Path,
        Dictionary<string, string> Headers,
        string Body);

    private sealed record McpHttpResponse(
        int Status,
        string? Body,
        string? ContentType,
        IReadOnlyDictionary<string, string>? Headers = null);

    private sealed class HttpError(int status) : Exception
    {
        public int Status { get; } = status;
    }

    private sealed class RpcException(int code, string message) : Exception(message)
    {
        public int Code { get; } = code;
    }

    private static async Task<HttpRequest?> ReadRequestAsync(NetworkStream stream, CancellationToken ct)
    {
        var buffer = new List<byte>(8192);
        var chunk = new byte[4096];
        var headerEnd = -1;
        while (headerEnd < 0)
        {
            var read = await stream.ReadAsync(chunk.AsMemory(), ct).ConfigureAwait(false);
            if (read == 0) return null;
            for (var i = 0; i < read; i++) buffer.Add(chunk[i]);
            if (buffer.Count > MaxHeaderBytes) throw new HttpError(431);
            headerEnd = FindHeaderEnd(buffer);
        }

        var headerText = Encoding.ASCII.GetString(buffer.GetRange(0, headerEnd).ToArray());
        var lines = headerText.Split("\r\n", StringSplitOptions.None);
        var parts = lines[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3) throw new HttpError(400);

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 1; i < lines.Length; i++)
        {
            var colon = lines[i].IndexOf(':');
            if (colon <= 0) continue;
            headers[lines[i][..colon].Trim()] = lines[i][(colon + 1)..].Trim();
        }

        if (headers.TryGetValue("transfer-encoding", out var encoding) &&
            encoding.Contains("chunked", StringComparison.OrdinalIgnoreCase))
        {
            throw new HttpError(411);
        }
        var length = 0L;
        if (headers.TryGetValue("content-length", out var rawLength) &&
            !long.TryParse(rawLength, out length))
        {
            throw new HttpError(400);
        }
        if (length > MaxBodyBytes) throw new HttpError(413);

        var body = new byte[length];
        var written = 0;
        for (var i = headerEnd + 4; i < buffer.Count && written < length; i++) body[written++] = buffer[i];
        while (written < length)
        {
            var read = await stream.ReadAsync(body.AsMemory(written, (int)length - written), ct).ConfigureAwait(false);
            if (read == 0) break;
            written += read;
        }

        var path = parts[1];
        var query = path.IndexOf('?');
        if (query >= 0) path = path[..query];
        return new HttpRequest(parts[0].ToUpperInvariant(), path, headers,
            Encoding.UTF8.GetString(body, 0, written));
    }

    private static int FindHeaderEnd(List<byte> buffer)
    {
        for (var i = 3; i < buffer.Count; i++)
        {
            if (buffer[i] == 10 && buffer[i - 1] == 13 && buffer[i - 2] == 10 && buffer[i - 3] == 13)
            {
                return i - 3;
            }
        }
        return -1;
    }

    private static async Task WriteResponseAsync(NetworkStream stream, McpHttpResponse response, CancellationToken ct)
    {
        var body = response.Body == null ? [] : Encoding.UTF8.GetBytes(response.Body);
        var head = new StringBuilder()
            .Append("HTTP/1.1 ").Append(response.Status).Append(' ').Append(Reason(response.Status)).Append("\r\n")
            .Append("Content-Length: ").Append(body.Length).Append("\r\n")
            .Append("Connection: close\r\n")
            .Append("Cache-Control: no-store\r\n");
        if (!string.IsNullOrEmpty(response.ContentType))
        {
            head.Append("Content-Type: ").Append(response.ContentType).Append("\r\n");
        }
        if (response.Headers != null)
        {
            foreach (var (key, value) in response.Headers) head.Append(key).Append(": ").Append(value).Append("\r\n");
        }
        head.Append("\r\n");

        await stream.WriteAsync(Encoding.ASCII.GetBytes(head.ToString()), ct).ConfigureAwait(false);
        if (body.Length > 0) await stream.WriteAsync(body, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    private static string Reason(int status) => status switch
    {
        200 => "OK",
        202 => "Accepted",
        400 => "Bad Request",
        401 => "Unauthorized",
        404 => "Not Found",
        405 => "Method Not Allowed",
        411 => "Length Required",
        413 => "Content Too Large",
        431 => "Request Header Fields Too Large",
        _ => "Internal Server Error",
    };

    private static McpHttpResponse JsonResponse(JsonObject node, string? sessionId)
    {
        var headers = sessionId == null
            ? null
            : new Dictionary<string, string> { ["Mcp-Session-Id"] = sessionId };
        return new McpHttpResponse(200, node.ToJsonString(), "application/json; charset=utf-8", headers);
    }

    private static McpHttpResponse JsonRpcError(JsonNode? id, int code, string message, string? sessionId = null)
    {
        var node = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id?.DeepClone(),
            ["error"] = new JsonObject { ["code"] = code, ["message"] = message },
        };
        return JsonResponse(node, sessionId);
    }

    private sealed class Session(string id, string name, DateTime connectedAt)
    {
        public string Id { get; } = id;
        public string Name { get; set; } = name;
        public DateTime ConnectedAt { get; } = connectedAt;
        public DateTime LastSeen { get; set; } = connectedAt;
        public int Calls { get; set; }
    }
}
