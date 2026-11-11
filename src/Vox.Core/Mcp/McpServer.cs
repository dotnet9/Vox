using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using Vox.Core.Mcp;

namespace Vox.Core.Mcp;

/// <summary>
/// Vox 的 MCP 工具引擎：工具 / 资源 / 指令的全部业务逻辑与权限过滤（授权范围、工具上下线、
/// git 开关、写前确认）。协议传输（Streamable HTTP + Bearer 鉴权）由 Vox.App 的
/// ModelContextProtocol.AspNetCore 承载，引擎只产出协议结果对象，不关心传输形态。
/// </summary>
public sealed partial class McpServer
{
    private const string ArticlePrefix = "vox://article/";
    private const string ImagePrefix = "vox://image/";
    private const string SitePrefix = "vox://site/";

    private McpOptions _options;

    /// <summary>每次写入审计（含被拒绝与鉴权失败），界面「实时调用」与「调用审计」共用。</summary>
    public event Action<ToolAuditEntry>? CallAudited;

    public IMcpHost? Host { get; }

    /// <summary>扫描源：界面传入 AppState.Scan 复用内存索引；测试可传临时仓库扫描。</summary>
    public Func<RepoScan?>? ScanProvider { get; set; }

    public McpOptions Options => _options;

    public int VisibleToolCount => McpCatalog.VisibleTools(_options).Count;

    public McpServer(McpOptions options, IMcpHost? host = null, Func<RepoScan?>? scanProvider = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        Host = host;
        ScanProvider = scanProvider;
    }

    /// <summary>热更新授权范围 / 开关 / 工具上下线。</summary>
    public void UpdateOptions(McpOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>启动校验：仓库就绪、端口合法、令牌强度。失败抛 InvalidOperationException，由调用方转为界面错误。</summary>
    public static void Validate(McpOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.RepoRoot) || !Directory.Exists(options.RepoRoot))
        {
            throw new InvalidOperationException("内容仓库未配置或不存在，MCP 服务无法启动。");
        }
        if (options.Port is < 0 or > 65535)
        {
            throw new InvalidOperationException("端口须为 1024–65535 的整数。");
        }
        if (options.Token.Trim().Length < 24)
        {
            throw new InvalidOperationException("访问令牌至少 24 个字符，请先生成令牌。");
        }
    }

    /// <summary>tools/list：可见工具清单（授权范围、git 开关与上下线三重过滤）。</summary>
    public ListToolsResult ListTools()
    {
        var tools = new List<Tool>();
        foreach (var tool in McpCatalog.VisibleTools(_options))
        {
            tools.Add(new Tool
            {
                Name = tool.Name,
                Description = tool.Description,
                InputSchema = JsonSerializer.SerializeToElement(JsonNode.Parse(tool.SchemaJson)),
                Annotations = new ToolAnnotations { ReadOnlyHint = tool.ReadOnly },
            });
        }
        return new ListToolsResult { Tools = tools };
    }

    /// <summary>tools/call：按名称分发到工具实现；写入工具先经 Vox 应用内确认，全程审计。</summary>
    public async Task<CallToolResult> CallTool(JsonObject? parameters, string client, CancellationToken ct)
    {
        var started = Stopwatch.GetTimestamp();
        var call = new CallState();
        CurrentCall.Value = call;
        var name = McpJson.Str(parameters, "name") ?? string.Empty;
        var args = parameters?["arguments"] as JsonObject ?? [];
        var tool = McpCatalog.Find(name);

        McpToolOutcome outcome;
        if (tool == null)
        {
            outcome = Fail($"未知工具：{name}", name);
        }
        else if (!McpCatalog.IsVisible(tool, _options))
        {
            outcome = Deny("工具在当前授权范围或开关下不可见。", tool.Name);
        }
        else
        {
            try
            {
                outcome = await ExecuteToolAsync(tool, args, client, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception error)
            {
                outcome = Fail(error.Message, tool.Name);
            }
        }

        var result = outcome.Denied ? "deny"
            : !outcome.Ok ? "error"
            : call.Confirmed ? "okc"
            : "ok";
        Audit(client, tool?.Name ?? name, string.IsNullOrWhiteSpace(outcome.Target) ? name : outcome.Target,
            result, outcome.Detail, ElapsedMs(started));

        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = outcome.Text }],
            IsError = !outcome.Ok,
        };
    }

    /// <summary>resources/list：文章以 md 原文暴露，图片暴露相对路径与 CDN 形态，站点数据 JSON 直出。</summary>
    public ListResourcesResult ListResources()
    {
        var resources = new List<Resource>();
        if (_options.Scope == McpAccessScope.Status)
        {
            return new ListResourcesResult { Resources = resources };
        }

        var scan = Scan();
        foreach (var article in scan.Articles.OrderByDescending(a => a.Meta.Date))
        {
            resources.Add(new Resource
            {
                Uri = ArticlePrefix + EncodePath(article.MarkdownPath),
                Name = $"article:{article.MarkdownPath}",
                Title = article.Title,
                MimeType = "text/markdown",
                Size = article.BodyBytes,
            });
        }

        if (_options.Scope == McpAccessScope.Repo)
        {
            foreach (var image in scan.Images.Where(i => i.Bytes >= 0).OrderBy(i => i.RelativePath, StringComparer.Ordinal))
            {
                resources.Add(new Resource
                {
                    Uri = ImagePrefix + EncodePath(image.RelativePath),
                    Name = $"image:{image.RelativePath}",
                    Title = image.FileName,
                    MimeType = ImageMime(image.RelativePath),
                    Size = image.Bytes,
                });
            }
            foreach (var site in scan.SiteFiles.OrderBy(s => s.RelativePath, StringComparer.Ordinal))
            {
                var sitePath = site.RelativePath.StartsWith("site/", StringComparison.OrdinalIgnoreCase)
                    ? site.RelativePath[5..]
                    : site.RelativePath;
                resources.Add(new Resource
                {
                    Uri = SitePrefix + EncodePath(sitePath),
                    Name = $"site:{sitePath}",
                    Title = Path.GetFileName(site.RelativePath),
                    MimeType = "application/json",
                    Size = site.RawJson.Length,
                });
            }
        }

        return new ListResourcesResult { Resources = resources };
    }

    /// <summary>resources/read：按 URI 前缀分发文章 / 图片 / 站点数据。</summary>
    public ReadResourceResult ReadResource(JsonObject? parameters)
    {
        var uri = McpJson.Str(parameters, "uri");
        if (string.IsNullOrWhiteSpace(uri)) throw new McpRequestException("缺少 uri 参数。");
        uri = uri.Trim();

        if (uri.StartsWith(ArticlePrefix, StringComparison.Ordinal))
        {
            if (_options.Scope == McpAccessScope.Status)
            {
                throw new McpRequestException("当前授权范围仅状态查询，内容资源不可读。");
            }
            var path = DecodePath(uri[ArticlePrefix.Length..]);
            var full = ResolveExisting(path, ".md");
            if (full == null) throw new McpRequestException($"资源不存在：{path}");
            return TextResource(uri, "text/markdown", File.ReadAllText(full));
        }

        if (uri.StartsWith(ImagePrefix, StringComparison.Ordinal))
        {
            if (_options.Scope != McpAccessScope.Repo)
            {
                throw new McpRequestException("当前授权范围不开放图片资源。");
            }
            var path = DecodePath(uri[ImagePrefix.Length..]);
            var image = Scan().FindImage(path);
            if (image == null) throw new McpRequestException($"资源不存在：{path}");
            var payload = ImageJson(image, includeReferences: true);
            payload["cdn"] = LinkStrategy.ToCdnUrl(image.RelativePath, _options.CdnBase, _options.RepoRoot);
            return TextResource(uri, "application/json", payload.ToJsonString());
        }

        if (uri.StartsWith(SitePrefix, StringComparison.Ordinal))
        {
            if (_options.Scope != McpAccessScope.Repo)
            {
                throw new McpRequestException("当前授权范围不开放站点数据资源。");
            }
            var path = DecodePath(uri[SitePrefix.Length..]);
            var relativePath = path.StartsWith("site/", StringComparison.OrdinalIgnoreCase) ? path : "site/" + path;
            var full = ResolveExisting(relativePath, ".json");
            if (full == null) throw new McpRequestException($"资源不存在：{path}");
            var relative = RelativePath(_options.RepoRoot, full) ?? relativePath;
            if (!relative.StartsWith("site/", StringComparison.OrdinalIgnoreCase))
            {
                throw new McpRequestException("站点数据资源须位于 site/ 目录。");
            }
            return TextResource(uri, "application/json", File.ReadAllText(full));
        }

        throw new McpRequestException($"不支持的资源 URI：{uri}");
    }

    /// <summary>prompts/list：四条预设指令模板。</summary>
    public ListPromptsResult ListPrompts()
    {
        var prompts = new List<Prompt>();
        if (_options.Scope == McpAccessScope.Status)
        {
            return new ListPromptsResult { Prompts = prompts };
        }
        foreach (var prompt in McpCatalog.Prompts)
        {
            var args = new List<PromptArgument>();
            foreach (var argument in prompt.Arguments)
            {
                args.Add(new PromptArgument
                {
                    Name = argument,
                    Description = argument == "article" ? "文章仓库相对路径" : "附加指令（可选）",
                    Required = argument == "article",
                });
            }
            prompts.Add(new Prompt
            {
                Name = prompt.Name,
                Title = prompt.Title,
                Description = prompt.Description,
                Arguments = args,
            });
        }
        return new ListPromptsResult { Prompts = prompts };
    }

    /// <summary>prompts/get：渲染预设指令（附带文章正文片段）。</summary>
    public GetPromptResult GetPrompt(JsonObject? parameters)
    {
        var name = McpJson.Str(parameters, "name");
        var prompt = McpCatalog.Prompts.FirstOrDefault(p => p.Name == name);
        if (prompt == null) throw new McpRequestException("未知的预设指令。");
        if (_options.Scope == McpAccessScope.Status)
        {
            throw new McpRequestException("当前授权范围仅状态查询，指令模板不可用。");
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

        return new GetPromptResult
        {
            Description = prompt.Description,
            Messages =
            [
                new PromptMessage
                {
                    Role = Role.User,
                    Content = new TextContentBlock { Text = text.ToString().TrimEnd() },
                },
            ],
        };
    }

    private static ReadResourceResult TextResource(string uri, string mimeType, string text) => new()
    {
        Contents =
        [
            new TextResourceContents
            {
                Uri = uri,
                MimeType = mimeType,
                Text = text,
            },
        ],
    };

    private static string ImageMime(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".svg" => "image/svg+xml",
        _ => "application/octet-stream",
    };

    private static string EncodePath(string path) =>
        string.Join("/", path.Split('/').Select(Uri.EscapeDataString));

    private static string DecodePath(string path) =>
        string.Join("/", path.Split('/').Select(Uri.UnescapeDataString));

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

    /// <summary>每次写入审计（含被拒绝与鉴权失败），界面「实时调用」与「调用审计」共用。</summary>
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

    /// <summary>协议级请求错误（客户端可读 message；SDK 负责映射为 JSON-RPC error）。</summary>
    public sealed class McpRequestException(string message) : Exception(message);
}
