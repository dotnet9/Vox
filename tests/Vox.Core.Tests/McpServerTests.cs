using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Vox.Core;
using Vox.Core.Mcp;
using Xunit;

public sealed class McpServerTests
{
    private const string Token = "vox_testtoken_0123456789abcdef";

    [Fact]
    public async Task Rejects_requests_without_valid_bearer_token()
    {
        await using var fixture = await McpFixture.CreateAsync();
        var response = await fixture.PostRawAsync(
            """{"jsonrpc":"2.0","id":1,"method":"ping"}""", token: "vox_wrong_token_000000000000");
        Assert.Equal(401, response.Status);
        Assert.NotEmpty(ToolAuditStore.List(fixture.AuditPath, result: "error"));
    }

    [Fact]
    public async Task Initialize_returns_session_and_tool_catalog_honors_scope_and_switches()
    {
        await using var repo = await McpFixture.CreateAsync();
        var session = await repo.InitializeAsync("Cursor");
        Assert.False(string.IsNullOrWhiteSpace(session));

        var defaults = await repo.ToolsAsync(session);
        Assert.Equal(16, defaults.Count);
        Assert.Contains("article_read", defaults);
        Assert.DoesNotContain("git_commit", defaults);

        repo.Server.UpdateOptions(repo.Options with { AllowGit = true });
        var all = await repo.ToolsAsync(session);
        Assert.Equal(18, all.Count);
        Assert.Contains("git_commit", all);

        repo.Server.UpdateOptions(repo.Options with { AllowGit = true, DisabledTools = ["image_replace"] });
        var withoutReplace = await repo.ToolsAsync(session);
        Assert.Equal(17, withoutReplace.Count);
        Assert.DoesNotContain("image_replace", withoutReplace);

        repo.Server.UpdateOptions(repo.Options with { Scope = McpAccessScope.Articles, AllowGit = true, DisabledTools = [] });
        var articles = await repo.ToolsAsync(session);
        Assert.DoesNotContain("image_list", articles);
        Assert.DoesNotContain("git_status", articles);
        Assert.Contains("article_write", articles);

        repo.Server.UpdateOptions(repo.Options with { Scope = McpAccessScope.Status });
        var status = await repo.ToolsAsync(session);
        Assert.Equal(10, status.Count);
        Assert.DoesNotContain("article_write", status);
        Assert.Contains("stats_summary", status);

        repo.Server.UpdateOptions(repo.Options with { Scope = McpAccessScope.Repo, AllowGit = false, DisabledTools = [] });
        var gitOff = await repo.ToolsAsync(session);
        Assert.DoesNotContain("git_commit", gitOff);
        Assert.DoesNotContain("git_push", gitOff);
        Assert.Contains("git_status", gitOff);
    }

    [Fact]
    public async Task Resources_list_and_read_expose_article_text()
    {
        await using var repo = await McpFixture.CreateAsync();
        var session = await repo.InitializeAsync("ZCode");

        var list = await repo.CallAsync("resources/list", null, session);
        var uris = list.GetProperty("result").GetProperty("resources").EnumerateArray()
            .Select(r => r.GetProperty("uri").GetString()).ToList();
        Assert.Contains("vox://article/2026/10/hello.md", uris);
        Assert.Contains("vox://image/2026/09/pic.jpg", uris);
        Assert.Contains("vox://site/friends.json", uris);

        var read = await repo.CallAsync("resources/read",
            new Dictionary<string, object?> { ["uri"] = "vox://article/2026/10/hello.md" }, session);
        var text = read.GetProperty("result").GetProperty("contents")[0].GetProperty("text").GetString();
        Assert.Contains("# Hello Vox", text);
    }

    [Fact]
    public async Task Tool_call_reads_article_and_writes_audit_entry()
    {
        await using var repo = await McpFixture.CreateAsync();
        var session = await repo.InitializeAsync("Cursor");

        var call = await repo.CallAsync("tools/call", new Dictionary<string, object?>
        {
            ["name"] = "article_read",
            ["arguments"] = new Dictionary<string, object?> { ["path"] = "2026/10/hello.md" },
        }, session);
        Assert.False(call.GetProperty("result").GetProperty("isError").GetBoolean());
        var payload = JsonDocument.Parse(call.GetProperty("result").GetProperty("content")[0]
            .GetProperty("text").GetString()!);
        Assert.Equal("Hello Vox", payload.RootElement.GetProperty("meta").GetProperty("title").GetString());
        Assert.Contains("正文内容 alpha", payload.RootElement.GetProperty("body").GetString());

        var audit = ToolAuditStore.List(repo.AuditPath, tool: "article_read");
        var entry = Assert.Single(audit);
        Assert.Equal("Cursor", entry.Client);
        Assert.Equal("ok", entry.Result);
        Assert.Equal("2026/10/hello.md", entry.Target);
    }

    [Fact]
    public async Task Write_tool_denied_without_confirmation_and_written_after_confirmation()
    {
        await using var repo = await McpFixture.CreateAsync();
        var session = await repo.InitializeAsync("Cursor");
        repo.Host.AllowWrites = false;

        var call = new Dictionary<string, object?>
        {
            ["name"] = "article_write",
            ["arguments"] = new Dictionary<string, object?>
            {
                ["path"] = "2026/10/hello.md",
                ["mode"] = "replace_all",
                ["content"] = "# 改写后的标题\n\n新的正文。",
            },
        };
        var denied = await repo.CallAsync("tools/call", call, session);
        Assert.True(denied.GetProperty("result").GetProperty("isError").GetBoolean());
        Assert.Contains("正文内容 alpha", await File.ReadAllTextAsync(repo.ArticleFile));

        var denyAudit = ToolAuditStore.List(repo.AuditPath, tool: "article_write");
        Assert.Equal("deny", Assert.Single(denyAudit).Result);

        repo.Host.AllowWrites = true;
        var allowed = await repo.CallAsync("tools/call", call, session);
        Assert.False(allowed.GetProperty("result").GetProperty("isError").GetBoolean());
        var text = await File.ReadAllTextAsync(repo.ArticleFile);
        Assert.Contains("改写后的标题", text);
        Assert.StartsWith("---", text);
        Assert.Contains("title: Hello Vox", text);
        Assert.Contains("2026/10/hello.md", repo.Host.Changed);
        Assert.Equal(2, repo.Host.Requests.Count);

        // 确认后的写入在审计里与普通成功区分（原型「已确认」筛选）
        var allowAudit = ToolAuditStore.List(repo.AuditPath, result: "okc");
        Assert.Equal("article_write", Assert.Single(allowAudit).Tool);
    }

    [Fact]
    public async Task Prompts_list_carries_four_presets()
    {
        await using var repo = await McpFixture.CreateAsync();
        var session = await repo.InitializeAsync("Claude Desktop");
        var list = await repo.CallAsync("prompts/list", null, session);
        var names = list.GetProperty("result").GetProperty("prompts").EnumerateArray()
            .Select(p => p.GetProperty("name").GetString()).ToList();
        Assert.Equal(4, names.Count);
        Assert.Contains("wechat-rewrite", names);
        Assert.Contains("summary-description", names);
    }

    [Fact]
    public async Task Path_traversal_is_rejected()
    {
        await using var repo = await McpFixture.CreateAsync();
        var session = await repo.InitializeAsync("Cursor");
        var call = await repo.CallAsync("tools/call", new Dictionary<string, object?>
        {
            ["name"] = "article_read",
            ["arguments"] = new Dictionary<string, object?> { ["path"] = "../../secret.md" },
        }, session);
        Assert.True(call.GetProperty("result").GetProperty("isError").GetBoolean());
    }

    private sealed class McpFixture : IAsyncDisposable
    {
        private McpFixture(string root, string auditPath, McpServer server, FakeHost host)
        {
            Root = root;
            AuditPath = auditPath;
            Server = server;
            Host = host;
            Http = new HttpClient { BaseAddress = new Uri(server.Endpoint) };
        }

        public string Root { get; }
        public string AuditPath { get; }
        public McpServer Server { get; }
        public FakeHost Host { get; }
        public HttpClient Http { get; }
        public McpOptions Options => Server.Options;
        public string ArticleFile => Path.Combine(Root, "2026", "10", "hello.md");

        public static async Task<McpFixture> CreateAsync(
            McpAccessScope scope = McpAccessScope.Repo, string[]? disabled = null)
        {
            var root = Directory.CreateTempSubdirectory("vox-mcp").FullName;
            Directory.CreateDirectory(Path.Combine(root, "2026", "10"));
            Directory.CreateDirectory(Path.Combine(root, "2026", "09"));
            Directory.CreateDirectory(Path.Combine(root, "2026", "08"));
            Directory.CreateDirectory(Path.Combine(root, "site"));

            await File.WriteAllTextAsync(Path.Combine(root, "2026", "10", "hello.md"),
                """
                ---
                title: Hello Vox
                slug: hello-vox
                date: 2026-10-01 10:00:00
                author: Dotnet9
                categories: [.NET]
                tags: [Avalonia]
                ---

                # Hello Vox

                ## 起因

                正文内容 alpha。

                ![图](../09/pic.jpg)
                """);
            await File.WriteAllBytesAsync(Path.Combine(root, "2026", "09", "pic.jpg"), [1, 2, 3, 4]);
            await File.WriteAllBytesAsync(Path.Combine(root, "2026", "08", "orphan.png"), [5, 6, 7]);
            await File.WriteAllTextAsync(Path.Combine(root, "site", "friends.json"),
                """{"avatar":"2026/09/pic.jpg"}""");

            var audit = Path.Combine(root, "audit", "tool-audit.jsonl");
            var host = new FakeHost();
            var options = new McpOptions
            {
                RepoRoot = root,
                Port = 0,
                Token = Token,
                Scope = scope,
                ConfirmWrites = true,
                DisabledTools = disabled ?? [],
                AuditPath = audit,
            };
            var server = new McpServer(options, host, () => RepoScanner.Scan(root));
            server.Start();
            return new McpFixture(root, audit, server, host);
        }

        public async Task<string> InitializeAsync(string clientName)
        {
            var response = await PostAsync(new Dictionary<string, object?>
            {
                ["jsonrpc"] = "2.0",
                ["id"] = "init",
                ["method"] = "initialize",
                ["params"] = new Dictionary<string, object?>
                {
                    ["protocolVersion"] = "2024-11-05",
                    ["capabilities"] = new Dictionary<string, object?>(),
                    ["clientInfo"] = new Dictionary<string, object?> { ["name"] = clientName, ["version"] = "1.0" },
                },
            });
            Assert.Equal(200, response.Status);
            Assert.NotNull(response.Session);
            return response.Session!;
        }

        public async Task<IReadOnlyList<string>> ToolsAsync(string session)
        {
            var response = await CallAsync("tools/list", null, session);
            return response.GetProperty("result").GetProperty("tools").EnumerateArray()
                .Select(t => t.GetProperty("name").GetString()!).ToList();
        }

        public async Task<JsonElement> CallAsync(string method, Dictionary<string, object?>? parameters, string session)
        {
            var payload = new Dictionary<string, object?>
            {
                ["jsonrpc"] = "2.0",
                ["id"] = Guid.NewGuid().ToString("N"),
                ["method"] = method,
            };
            if (parameters != null) payload["params"] = parameters;
            var response = await PostAsync(payload, session);
            Assert.Equal(200, response.Status);
            return JsonDocument.Parse(response.Body).RootElement.Clone();
        }

        public async Task<(int Status, string Body, string? Session)> PostAsync(
            Dictionary<string, object?> payload, string? session = null)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "")
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);
            if (session != null) request.Headers.TryAddWithoutValidation("Mcp-Session-Id", session);
            return await SendAsync(request);
        }

        public async Task<(int Status, string Body, string? Session)> PostRawAsync(string json, string token)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "")
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return await SendAsync(request);
        }

        private async Task<(int Status, string Body, string? Session)> SendAsync(HttpRequestMessage request)
        {
            using var response = await Http.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            var session = response.Headers.TryGetValues("Mcp-Session-Id", out var values)
                ? values.FirstOrDefault()
                : null;
            return ((int)response.StatusCode, body, session);
        }

        public ValueTask DisposeAsync()
        {
            Http.Dispose();
            Server.Dispose();
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch
            {
                // 临时目录清理失败不影响测试结论
            }
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeHost : IMcpHost
    {
        public bool AllowWrites { get; set; } = true;
        public List<McpWriteRequest> Requests { get; } = [];
        public List<string> Changed { get; } = [];

        public Task<bool> ConfirmWriteAsync(McpWriteRequest request, CancellationToken ct)
        {
            Requests.Add(request);
            return Task.FromResult(AllowWrites);
        }

        public void NotifyContentChanged(string? relativePath) => Changed.Add(relativePath ?? "");

        public Task<string?> GitStatusAsync(CancellationToken ct) =>
            Task.FromResult<string?>("""{"branch":"main","ahead":0,"behind":0}""");

        public Task<string?> PublishStatusAsync(string? articlePath, CancellationToken ct) =>
            Task.FromResult<string?>($$"""{"article":{{JsonSerializer.Serialize(articlePath)}},"channels":[]}""");
    }
}
