using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Vox.Core.Llm;
using Avalonia.Threading;
using Vox.App.Infrastructure;
using Vox.Core.Mcp;
using ModelContextProtocol.Protocol;
using I18n = Vox.App.Infrastructure.I18n;

namespace Vox.App.Services;

// ---------- Agent 线格式（OpenAI 协议的工具消息族） ----------

public sealed class AiFuncCall
{
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("arguments")] public string Arguments { get; set; } = "{}";
}

public sealed class AiToolCallSpec
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("type")] public string Type { get; set; } = "function";
    [JsonPropertyName("function")] public AiFuncCall Function { get; set; } = new();
}

public sealed class AiToolDef
{
    [JsonPropertyName("type")] public string Type { get; set; } = "function";
    [JsonPropertyName("function")] public JsonNode Function { get; set; } = new JsonObject();
}

/// <summary>Agent 线消息：role = user / assistant / tool / system；assistant 可携 tool_calls。</summary>
public sealed class AiWireMessage
{
    [JsonPropertyName("role")] public string Role { get; set; } = "user";
    [JsonPropertyName("content")] public JsonNode? Content { get; set; }
    [JsonPropertyName("tool_calls")] public AiToolCallSpec[]? ToolCalls { get; set; }
    [JsonPropertyName("tool_call_id")] public string? ToolCallId { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
}

/// <summary>一次工具执行的回显快照（UI 卡片与日志共用）。</summary>
public sealed record AiToolRun(string Name, string ArgsSummary, bool Ok, string ResultSummary, TimeSpan Elapsed);

/// <summary>Agent 循环结果：最终回复 + 全部工具执行记录。</summary>
public sealed record AiAgentResult(bool Ok, string Text, string? Error, TimeSpan Elapsed, IReadOnlyList<AiToolRun> ToolRuns);

/// <summary>
/// 内置 AI 的 Agent 工具循环（2026-11-14 意图执行）：LLM 输出 tool_calls 时经
/// <see cref="McpServer.CallTool"/> 进程内直调（复用 MCP 17 工具与确认/审计线），
/// 结果回喂直至最终回复；最多 <paramref name="maxRounds"/> 轮防失控。client 固定"内置AI"走审计线。
/// </summary>
public static class AiAgentLoop
{
    private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };

    /// <summary>Agent 可用工具白名单（只读 + 确认流保护的写操作；正文写入走会话内 diff 流，不开放自动写）。
    /// git 提交/推送 2026-11-15 纳入：外部仍受用户授权范围（Scope/AllowGit）与写入确认卡双重过滤。</summary>
    public static readonly string[] AllowedTools =
    [
        "article_list", "article_read", "article_search", "article_meta", "article_create",
        "image_list", "image_usage", "image_import",
        "publish_status", "publish_check",
        "git_status", "git_commit", "git_push",
        "outline_read", "stats_summary",
    ];

    public static IReadOnlyList<AiToolDef> ToolDefs() =>
        McpCatalog.Tools.Where(t => AllowedTools.Contains(t.Name))
            .Select(t =>
            {
                var fn = new JsonObject
                {
                    ["name"] = t.Name,
                    ["description"] = t.Description,
                };
                try { fn["parameters"] = JsonNode.Parse(t.SchemaJson); } catch { fn["parameters"] = new JsonObject(); }
                return new AiToolDef { Function = fn };
            })
            .ToList();

    public static async Task<AiAgentResult> RunAsync(
        string providerId,
        string model,
        string system,
        List<AiWireMessage> messages,
        Action<string> onDelta,
        Action<string>? onReasoning = null,
        Action<AiToolRun>? onToolRun = null,
        int maxRounds = 6,
        int maxTokens = 8192,
        TimeSpan? timeout = null,
        bool disableThinking = false,
        CancellationToken cancellationToken = default)
    {
        var toolDefs = ToolDefs();
        var runs = new List<AiToolRun>();
        var watch = Stopwatch.StartNew();
        var server = McpService.Ensure();

        for (var round = 0; round < maxRounds; round++)
        {
            var pending = new Dictionary<int, (string Id, string Name, StringBuilder Args)>();
            StringBuilder? textBuf = null;
            string? streamError = null;
            var finished = false;
            var finishIsToolCall = false;

            using var roundCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            roundCts.CancelAfter(timeout ?? TimeSpan.FromSeconds(120));

            try
            {
                var toolJson = JsonSerializer.Serialize(toolDefs, AiJsonContext.Default.ListAiToolDef);
                var payload = new JsonObject
                {
                    ["model"] = model,
                    ["messages"] = SerializeMessages(system, messages),
                    ["max_tokens"] = maxTokens,
                    ["stream"] = true,
                    ["tools"] = JsonNode.Parse(toolJson),
                };
                // GLM 私有 thinking 参数（供应商声明支持时下发）
                if (disableThinking && providerId == "glm")
                    payload["thinking"] = new JsonObject { ["type"] = "disabled" };

                using var request = new HttpRequestMessage(HttpMethod.Post,
                    LlmService.EndpointFor(providerId) + "/chat/completions")
                {
                    Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json"),
                };
                LlmService.AttachAuth(request, providerId, apiKeyOverride: null);

                using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, roundCts.Token);
                if (!response.IsSuccessStatusCode)
                {
                    var errBody = await response.Content.ReadAsStringAsync(roundCts.Token);
                    return new AiAgentResult(false, string.Empty, LlmService.DescribeErrorPublic(errBody, (int)response.StatusCode), watch.Elapsed, runs);
                }

                var stream = await response.Content.ReadAsStreamAsync(roundCts.Token);
                using var reader = new StreamReader(stream);
                while (await reader.ReadLineAsync(roundCts.Token) is { } line)
                {
                    if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
                    var data = line["data:".Length..];
                    if (data.Trim() == "[DONE]") { finished = true; break; }

                    JsonDocument doc;
                    try { doc = JsonDocument.Parse(data); }
                    catch (JsonException) { continue; }
                    using (doc)
                    {
                        var root = doc.RootElement;
                        if (root.TryGetProperty("error", out var err))
                        {
                            streamError = err.ValueKind == JsonValueKind.String
                                ? err.GetString() ?? "stream error"
                                : err.TryGetProperty("message", out var m) && m.GetString() is { Length: > 0 } mm ? mm : "stream error";
                            break;
                        }
                        if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0) continue;
                        var choice = choices[0];

                        // 思考增量（GLM reasoning_content / 部分 deepseek-r 系）
                        if (choice.TryGetProperty("delta", out var delta))
                        {
                            if (delta.TryGetProperty("reasoning_content", out var rc) && rc.ValueKind == JsonValueKind.String && rc.GetString() is { Length: > 0 } think)
                                onReasoning?.Invoke(think);
                            if (delta.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String && content.GetString() is { Length: > 0 } piece)
                            {
                                textBuf ??= new StringBuilder();
                                textBuf.Append(piece);
                                onDelta?.Invoke(piece);
                            }
                            // 工具调用增量聚合（按 index 分桶，id/name/args 分段到达）
                            if (delta.TryGetProperty("tool_calls", out var tcs) && tcs.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var tc in tcs.EnumerateArray())
                                {
                                    var idx = tc.TryGetProperty("index", out var ix) && ix.ValueKind == JsonValueKind.Number ? ix.GetInt32() : 0;
                                    if (!pending.TryGetValue(idx, out var slot))
                                    {
                                        string newId = tc.TryGetProperty("id", out var tid) && tid.GetString() is { Length: > 0 } id0 ? id0 : $"call_{idx}_{Guid.NewGuid():N}";
                                        string newName = tc.TryGetProperty("function", out var f0) && f0.TryGetProperty("name", out var n0) && n0.GetString() is { Length: > 0 } n1 ? n1 : "";
                                        slot = (newId, newName, new StringBuilder());
                                        pending[idx] = slot;
                                    }
                                    if (tc.TryGetProperty("function", out var fn))
                                    {
                                        if (fn.TryGetProperty("name", out var fname) && fname.GetString() is { Length: > 0 } fnName && slot.Item2.Length == 0)
                                            pending[idx] = (slot.Item1, fnName, slot.Item3);
                                        if (fn.TryGetProperty("arguments", out var args) && args.GetString() is { Length: > 0 } argPart)
                                            pending[idx].Item3.Append(argPart);
                                    }
                                }
                            }
                        }

                        if (choice.TryGetProperty("finish_reason", out var finish) && finish.ValueKind == JsonValueKind.String)
                        {
                            finished = true;
                            finishIsToolCall = finish.GetString() == "tool_calls";
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return new AiAgentResult(false, string.Empty, I18n.T("Vox.Ai.Error.Timeout"), watch.Elapsed, runs);
            }
            catch (OperationCanceledException)
            {
                throw; // 用户取消向上传播（会话停止）
            }

            // 聚合本轮 tool_calls → 执行
            var calls = pending.OrderBy(kv => kv.Key).Select(kv => (kv.Value.Item1, kv.Value.Item2, kv.Value.Item3.ToString())).ToList();
            if (streamError is not null)
                return new AiAgentResult(false, string.Empty, streamError, watch.Elapsed, runs);
            if (finished && !finishIsToolCall && calls.Count == 0)
            {
                var final = textBuf?.ToString().Trim() ?? string.Empty;
                return new AiAgentResult(final.Length > 0, final, final.Length == 0 ? I18n.T("Vox.Ai.Error.Empty") : null, watch.Elapsed, runs);
            }

            if (calls.Count == 0)
            {
                // 无正文无工具：再给一轮机会（罕见，防死循环交给 maxRounds）
                if (round == maxRounds - 1)
                    return new AiAgentResult(false, string.Empty, I18n.T("Vox.Ai.Error.Empty"), watch.Elapsed, runs);
                continue;
            }

            // assistant(tool_calls) 消息入线
            var assistantMsg = new AiWireMessage
            {
                Role = "assistant",
                ToolCalls = calls.Select(c => new AiToolCallSpec { Id = c.Item1, Function = new AiFuncCall { Name = c.Item2, Arguments = c.Item3 } }).ToArray(),
                Content = textBuf?.Length > 0 ? textBuf.ToString() : null,
            };
            messages.Add(assistantMsg);

            // 逐个执行（进程内直调 MCP 工具；审计/确认随 CallTool 主线）
            foreach (var (id, name, argsJson) in calls)
            {
                var sw = Stopwatch.StartNew();
                var argsSummary = ArgsSummary(argsJson);
                JsonObject? argsNode = null;
                try { argsNode = JsonNode.Parse(argsJson) as JsonObject; } catch { }
                // 参数非法时直通引擎（引擎返回错误内容，行为与 MCP 客户端一致）
                var outcome = await server.CallTool(
                    argsNode is null ? new JsonObject { ["name"] = name } : new JsonObject { ["name"] = name, ["arguments"] = argsNode },
                    "内置AI", roundCts.Token, McpService.AgentOptions);
                sw.Stop();
                var resultText = string.Join("\n", (outcome.Content ?? [])
                    .OfType<ModelContextProtocol.Protocol.TextContentBlock>()
                    .Select(c => c.Text ?? string.Empty));
                var ok = outcome.IsError != true;

                var run = new AiToolRun(name, argsSummary, ok, Truncate(resultText, 300), sw.Elapsed);
                runs.Add(run);
                onToolRun?.Invoke(run);

                messages.Add(new AiWireMessage
                {
                    Role = "tool",
                    ToolCallId = id,
                    Content = Truncate(resultText, 4000),
                });
            }
        }

        return new AiAgentResult(false, string.Empty, I18n.T("Vox.Ai.Error.Empty"), watch.Elapsed, runs);
    }

    private static string ArgsSummary(string argsJson)
    {
        try
        {
            if (JsonNode.Parse(argsJson) is JsonObject o)
            {
                var parts = o.Select(kv => $"{kv.Key}={Truncate(kv.Value?.ToString() ?? "", 40)}");
                return Truncate(string.Join(" · ", parts), 120);
            }
        }
        catch { }
        return Truncate(argsJson, 80);
    }

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..max] + "…";

    private static JsonArray SerializeMessages(string system, List<AiWireMessage> messages)
    {
        var arr = new JsonArray
        {
            new JsonObject { ["role"] = "system", ["content"] = system },
        };
        foreach (var m in messages)
            arr.Add(JsonSerializer.SerializeToNode(m, AiJsonContext.Default.AiWireMessage));
        return arr;
    }
}

public sealed class McpToolContent
{
    [JsonPropertyName("type")] public string Type { get; set; } = "text";
    [JsonPropertyName("text")] public string Text { get; set; } = string.Empty;
}

public sealed class CallToolResult
{
    [JsonPropertyName("content")] public List<McpToolContent> Content { get; set; } = [];
    [JsonPropertyName("isError")] public bool IsError { get; set; }
}

[JsonSourceGenerationOptions(WriteIndented = false, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(AiWireMessage))]
[JsonSerializable(typeof(AiToolDef))]
[JsonSerializable(typeof(List<AiToolDef>))]
internal sealed partial class AiJsonContext : JsonSerializerContext;
