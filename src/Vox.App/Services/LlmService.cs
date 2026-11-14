using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Vox.App.Infrastructure;
using Vox.Core.Llm;
using LlmProviderConfig = Vox.Core.LlmProviderConfig;
using Vox.Git;

namespace Vox.App.Services;

/// <summary>添加供应商的预设模板（ZCode 口径）：名称/端点/协议/预置模型/思考支持——选中即创建可编辑配置。</summary>
public sealed record LlmPreset(string Name, string BaseUrl, string Protocol, string[] Models, bool Thinking);

public static class LlmPresets
{
    public static readonly LlmPreset[] All =
    [
        new("GLM 编码套餐", "https://open.bigmodel.cn/api/coding/paas/v4", "openai", ["glm-5.3-flash", "glm-5.3"], true),
        new("GLM API", "https://open.bigmodel.cn/api/paas/v4", "openai", ["glm-5.3-flash", "glm-5.3"], true),
        // DeepSeek：官方 model 参数现为 deepseek-flash（=V4.1-Flash-1017）/ deepseek-v4-pro；deepseek-chat 为过渡别名（将停用）。
        // OpenAI：2026-11 官方模型页 ids（gpt-6.1-sol 为主推性价比旗舰；terra 视觉/写作，luna 快速低价）。
        new("DeepSeek", "https://api.deepseek.com/v1", "openai", ["deepseek-flash", "deepseek-v4-pro", "deepseek-chat"], true),
        new("OpenAI", "https://api.openai.com/v1", "openai", ["gpt-6.1-sol", "gpt-6-sol", "gpt-5.6-sol", "gpt-6.1-terra", "gpt-6-luna"], false),
        new("Anthropic", "https://api.anthropic.com/v1", "anthropic", ["claude-sonnet-4-5"], false),
        new("Ollama 本机", "http://127.0.0.1:11434/v1", "openai", ["qwen3:14b"], false),
        new("自定义", "", "openai", [], false),
    ];
}

public sealed record LlmResult(bool Ok, string Text, string? Error, TimeSpan Elapsed);

/// <summary>
/// 大模型通道：密钥按服务商存系统凭据管理器（vox/secret/llm/&lt;provider&gt;），请求 / 响应全部走源生成 JSON。
/// 写作页 AI 助手与「测试连接」共用这一入口。
/// </summary>
public static class LlmService
{
    // 客户端级超时置为无限：流式回复的时长由每次调用的 CancelAfter（流式 120s / 非流式 75s）统一裁决。
    // 保留默认 100s 会先于 CancelAfter 掐断长流，120s 的流式上限便永远不可达。
    private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };

    /// <summary>按配置 Id 取 Key：新键 vox/secret/llm/p/&lt;id&gt;；缺失时回退旧键 llm/glm（迁移前的 GLM 单提供商口径）。</summary>
    public static string? GetApiKey(string providerId)
    {
        try
        {
            var store = CredentialStoreFactory.Create();
            var key = store.Get("llm/p/" + providerId);
            if (!string.IsNullOrWhiteSpace(key)) return key;
            // 旧键迁移读：只认 glm（迁移前的单提供商）
            return providerId.Equals("glm", StringComparison.OrdinalIgnoreCase) ? store.Get("llm/glm") : null;
        }
        catch
        {
            return null;
        }
    }

    public static bool HasApiKey(string providerId) => !string.IsNullOrWhiteSpace(GetApiKey(providerId));

    public static void SaveApiKey(string providerId, string key)
    {
        var store = CredentialStoreFactory.Create();
        if (string.IsNullOrWhiteSpace(key)) store.Delete("llm/p/" + providerId);
        else store.Save("llm/p/" + providerId, key.Trim());
    }

    /// <summary>按 Id 取供应商配置；找不到回退第一个已启用项（设置清空时的容错）。</summary>
    public static LlmProviderConfig? GetProvider(string? providerId)
    {
        var providers = AppState.Instance.Settings.LlmProviders;
        if (providers.Count == 0) return null;
        return providers.FirstOrDefault(p => p.Id == providerId)
            ?? providers.FirstOrDefault(p => p.Enabled)
            ?? providers[0];
    }

    public static async Task<LlmResult> CompleteAsync(
        string providerId,
        string model,
        string system,
        string user,
        int maxTokens = 1400,
        TimeSpan? timeout = null,
        bool disableThinking = false,
        CancellationToken cancellationToken = default)
    {
        var provider = GetProvider(providerId) ?? throw new InvalidOperationException(I18n.T("Vox.Ai.Error.NoProvider"));
        var resolvedModel = string.IsNullOrWhiteSpace(model) ? provider.Models.FirstOrDefault() ?? "gpt-4o" : model.Trim();
        var apiKey = GetApiKey(provider.Id);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new LlmResult(false, string.Empty, I18n.T("Vox.Ai.Error.NoKey"), TimeSpan.Zero);
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout ?? TimeSpan.FromSeconds(75));
        var watch = Stopwatch.StartNew();
        try
        {
            var isClaude = provider.Protocol == "anthropic";
            using var request = isClaude
                ? BuildAnthropic(provider, resolvedModel, apiKey, system, user, maxTokens)
                : BuildOpenAiCompatible(provider, resolvedModel, apiKey, system, user, maxTokens, disableThinking);
            using var response = await Http.SendAsync(request, cts.Token);
            var raw = await response.Content.ReadAsStringAsync(cts.Token);
            watch.Stop();
            if (!response.IsSuccessStatusCode)
            {
                return new LlmResult(false, string.Empty, DescribeError(raw, (int)response.StatusCode), watch.Elapsed);
            }

            var text = isClaude ? ParseAnthropic(raw) : ParseOpenAiCompatible(raw);
            return string.IsNullOrWhiteSpace(text)
                ? new LlmResult(false, string.Empty, I18n.T("Vox.Ai.Error.Empty"), watch.Elapsed)
                : new LlmResult(true, text.Trim(), null, watch.Elapsed);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 调用方主动取消（如页面切换）：按取消回告，不算超时失败。
            watch.Stop();
            return new LlmResult(false, string.Empty, I18n.T("Vox.Ai.Stopped"), watch.Elapsed);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            watch.Stop();
            return new LlmResult(false, string.Empty, I18n.T("Vox.Ai.Error.Timeout"), watch.Elapsed);
        }
        catch (Exception ex)
        {
            watch.Stop();
            return new LlmResult(false, string.Empty, ex.Message, watch.Elapsed);
        }
    }

    /// <summary>
    /// 多轮流式对话：SSE 增量经 <paramref name="onDelta"/> 回吐（每收到一段就调用，调用方自行调度到 UI 线程）。
    /// 用户主动取消（<paramref name="cancellationToken"/>）时返回已生成的部分文本（Ok 视为有内容）；
    /// 超时或流内错误返回失败。解析逻辑在 Vox.Core.Llm.LlmStreamParser（有单测）。
    /// </summary>
    public static async Task<LlmResult> StreamAsync(
        string providerId,
        string model,
        string system,
        IReadOnlyList<LlmChatTurn> turns,
        Action<string> onDelta,
        int maxTokens = 8192, // glm-5.x 思考计入 max_tokens：1400 被 26 秒思考烧尽致 content 空（2026-11-14 实测"模型没有返回内容"根因），8192 给思考+正文留足预算
        TimeSpan? timeout = null,
        bool disableThinking = false,
        Action<string>? onReasoning = null,
        CancellationToken cancellationToken = default)
    {
        var provider = GetProvider(providerId) ?? throw new InvalidOperationException(I18n.T("Vox.Ai.Error.NoProvider"));
        var resolvedModel = string.IsNullOrWhiteSpace(model) ? provider.Models.FirstOrDefault() ?? "gpt-4o" : model.Trim();
        var apiKey = GetApiKey(provider.Id);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new LlmResult(false, string.Empty, I18n.T("Vox.Ai.Error.NoKey"), TimeSpan.Zero);
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout ?? TimeSpan.FromSeconds(120));
        var watch = Stopwatch.StartNew();
        try
        {
            var isClaude = provider.Protocol == "anthropic";
            // 历史先整形（首条 user、角色交替）：预设指令 / 失败重试等路径攒出的历史可能有连续同角色，Anthropic 协议会直接 400。
            var normalized = LlmChatHistory.Normalize(turns);
            var messages = normalized.Select(t => new LlmMessage { Role = t.Role, Content = t.Content }).ToList();
            // OpenAI 兼容协议没有独立 system 字段：system 一律作为首条消息下发（与非流式 BuildOpenAiCompatible 同口径）。
            if (!isClaude && !string.IsNullOrWhiteSpace(system))
            {
                messages.Insert(0, new LlmMessage { Role = "system", Content = system });
            }
            HttpRequestMessage Build()
            {
                HttpRequestMessage request;
                if (isClaude)
                {
                    var payload = new AnthropicRequest
                    {
                        Model = resolvedModel, MaxTokens = maxTokens, System = system, Messages = [.. messages], Stream = true,
                    };
                    request = new HttpRequestMessage(HttpMethod.Post, provider.BaseUrl.TrimEnd('/') + "/messages")
                    {
                        Content = new StringContent(
                            JsonSerializer.Serialize(payload, LlmJsonContext.Default.AnthropicRequest),
                            Encoding.UTF8, "application/json"),
                    };
                    request.Headers.TryAddWithoutValidation("x-api-key", apiKey);
                    request.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
                }
                else
                {
                    var payload = new OpenAiRequest
                    {
                        Model = resolvedModel, Messages = [.. messages], MaxTokens = maxTokens, Stream = true,
                    };
                    ApplyThinking(payload, provider, disableThinking);
                    request = new HttpRequestMessage(HttpMethod.Post, provider.BaseUrl.TrimEnd('/') + "/chat/completions")
                    {
                        Content = new StringContent(
                            JsonSerializer.Serialize(payload, LlmJsonContext.Default.OpenAiRequest),
                            Encoding.UTF8, "application/json"),
                    };
                    if (!string.IsNullOrWhiteSpace(apiKey))
                    {
                        request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + apiKey);
                    }
                }
                return request;
            }

            using var request = Build();
            using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            if (!response.IsSuccessStatusCode)
            {
                var raw = await response.Content.ReadAsStringAsync(cts.Token);
                return new LlmResult(false, string.Empty, DescribeError(raw, (int)response.StatusCode), watch.Elapsed);
            }

            var full = new StringBuilder();
            var protocol = isClaude ? LlmStreamProtocol.Anthropic : LlmStreamProtocol.OpenAiCompatible;
            var stream = await response.Content.ReadAsStreamAsync(cts.Token);
            using var reader = new StreamReader(stream);
            while (await reader.ReadLineAsync(cts.Token) is { } line)
            {
                if (!line.StartsWith("data:", StringComparison.Ordinal)) continue; // event:/注释/空行跳过
                var @event = LlmStreamParser.Parse(protocol, line["data:".Length..]);
                if (@event.Error != null)
                {
                    return new LlmResult(full.Length > 0, full.ToString(), @event.Error, watch.Elapsed);
                }
                if (@event.Text is { Length: > 0 } chunk)
                {
                    full.Append(chunk);
                    onDelta(chunk);
                }
                if (@event.Reasoning is { Length: > 0 } thought)
                {
                    onReasoning?.Invoke(thought);
                }
                if (@event.Done) break;
            }
            watch.Stop();
            var text = full.ToString().Trim();
            return text.Length == 0
                ? new LlmResult(false, string.Empty, I18n.T("Vox.Ai.Error.Empty"), watch.Elapsed)
                : new LlmResult(true, text, null, watch.Elapsed);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 用户点「停止」：已生成的部分保留在会话里，不算失败。
            return new LlmResult(true, string.Empty, null, watch.Elapsed);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new LlmResult(false, string.Empty, I18n.T("Vox.Ai.Error.Timeout"), watch.Elapsed);
        }
        catch (Exception ex)
        {
            return new LlmResult(false, string.Empty, ex.Message, watch.Elapsed);
        }
    }

    /// <summary>GLM 私有 thinking 开关：仅声明支持的供应商附带（OpenAI 拒绝未知参数），其余不冒进。</summary>
    private static void ApplyThinking(OpenAiRequest payload, LlmProviderConfig provider, bool disableThinking)
    {
        if (disableThinking && provider.ThinkingSupported)
        {
            payload.Thinking = new ThinkingConfig();
        }
    }

    private static HttpRequestMessage BuildOpenAiCompatible(
        LlmProviderConfig provider, string model, string? apiKey, string system, string user, int maxTokens, bool disableThinking = false)
    {
        var payload = new OpenAiRequest
        {
            Model = model,
            Messages =
            [
                new LlmMessage { Role = "system", Content = system },
                new LlmMessage { Role = "user", Content = user },
            ],
            MaxTokens = maxTokens,
        };
        // glm-5.x 为自适应推理模型，简单提问也可能思考取尽 max_tokens 致 content 为空
        // （2026-11-13 测试连接“模型没有返回内容”实测复现），调用方可禁思考。
        ApplyThinking(payload, provider, disableThinking);
        var request = new HttpRequestMessage(HttpMethod.Post, provider.BaseUrl.TrimEnd('/') + "/chat/completions")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(payload, LlmJsonContext.Default.OpenAiRequest),
                Encoding.UTF8, "application/json"),
        };
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + apiKey);
        }
        return request;
    }

    private static HttpRequestMessage BuildAnthropic(
        LlmProviderConfig provider, string model, string apiKey, string system, string user, int maxTokens)
    {
        var payload = new AnthropicRequest
        {
            Model = model,
            MaxTokens = maxTokens,
            System = system,
            Messages = [new LlmMessage { Role = "user", Content = user }],
        };
        var request = new HttpRequestMessage(HttpMethod.Post, provider.BaseUrl.TrimEnd('/') + "/messages")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(payload, LlmJsonContext.Default.AnthropicRequest),
                Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("x-api-key", apiKey);
        request.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
        return request;
    }

    private static string ParseOpenAiCompatible(string raw)
    {
        try
        {
            var response = JsonSerializer.Deserialize(raw, LlmJsonContext.Default.OpenAiResponse);
            return response?.Choices?.FirstOrDefault()?.Message?.Content ?? string.Empty;
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }

    private static string ParseAnthropic(string raw)
    {
        try
        {
            var response = JsonSerializer.Deserialize(raw, LlmJsonContext.Default.AnthropicResponse);
            var blocks = response?.Content;
            return blocks == null
                ? string.Empty
                : string.Join("\n", blocks.Where(b => b.Text != null).Select(b => b.Text));
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }

    /// <summary>错误体取 error.message，否则截断原文（HTML 网关页也只留一句可读提示）。</summary>
    private static string DescribeError(string raw, int status)
    {
        try
        {
            using var document = JsonDocument.Parse(raw);
            if (document.RootElement.TryGetProperty("error", out var error))
            {
                if (error.ValueKind == JsonValueKind.Object
                    && error.TryGetProperty("message", out var message)
                    && message.GetString() is { Length: > 0 } text)
                {
                    return text;
                }
                if (error.ValueKind == JsonValueKind.String && error.GetString() is { Length: > 0 } plain)
                {
                    return plain;
                }
            }
        }
        catch (JsonException)
        {
            // 非 JSON 错误体：走截断兜底
        }
        var collapsed = string.Join(' ', raw.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        var detail = collapsed.Length > 240 ? collapsed[..240] + "…" : collapsed;
        return detail.Length == 0 ? $"HTTP {status}" : $"HTTP {status} · {detail}";
    }
}

public sealed class LlmMessage
{
    [JsonPropertyName("role")] public string Role { get; set; } = "user";
    [JsonPropertyName("content")] public string Content { get; set; } = string.Empty;
}

public sealed class OpenAiRequest
{
    [JsonPropertyName("model")] public string Model { get; set; } = string.Empty;
    [JsonPropertyName("messages")] public LlmMessage[] Messages { get; set; } = [];
    [JsonPropertyName("max_tokens")] public int MaxTokens { get; set; } = 1400;
    [JsonPropertyName("temperature")] public double Temperature { get; set; } = 0.6;
    [JsonPropertyName("stream")] public bool Stream { get; set; }
    [JsonPropertyName("thinking")] public ThinkingConfig? Thinking { get; set; }
}

/// <summary>GLM 私有推理开关（BigModel）。DefaultIgnoreCondition=WhenWritingNull 保证其余服务商不收到该字段。</summary>
public sealed class ThinkingConfig
{
    [JsonPropertyName("type")] public string Type { get; set; } = "disabled";
}

public sealed class OpenAiResponse
{
    [JsonPropertyName("choices")] public OpenAiChoice[]? Choices { get; set; }
}

public sealed class OpenAiChoice
{
    [JsonPropertyName("message")] public OpenAiMessage? Message { get; set; }
}

public sealed class OpenAiMessage
{
    [JsonPropertyName("content")] public string? Content { get; set; }
}

public sealed class AnthropicRequest
{
    [JsonPropertyName("model")] public string Model { get; set; } = string.Empty;
    [JsonPropertyName("max_tokens")] public int MaxTokens { get; set; } = 1400;
    [JsonPropertyName("system")] public string System { get; set; } = string.Empty;
    [JsonPropertyName("messages")] public LlmMessage[] Messages { get; set; } = [];
    [JsonPropertyName("temperature")] public double Temperature { get; set; } = 0.6;
    [JsonPropertyName("stream")] public bool Stream { get; set; }
}

public sealed class AnthropicResponse
{
    [JsonPropertyName("content")] public AnthropicBlock[]? Content { get; set; }
}

public sealed class AnthropicBlock
{
    [JsonPropertyName("type")] public string? Type { get; set; }
    [JsonPropertyName("text")] public string? Text { get; set; }
}

[JsonSourceGenerationOptions(WriteIndented = false, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(OpenAiRequest))]
[JsonSerializable(typeof(OpenAiResponse))]
[JsonSerializable(typeof(AnthropicRequest))]
[JsonSerializable(typeof(AnthropicResponse))]
internal sealed partial class LlmJsonContext : JsonSerializerContext;
