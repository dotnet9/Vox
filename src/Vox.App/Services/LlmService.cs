using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Vox.App.Infrastructure;
using Vox.Git;

namespace Vox.App.Services;

/// <summary>大模型服务商预置：OpenAI 兼容端点（DeepSeek / OpenAI / Ollama），Claude 走 messages 协议。</summary>
public sealed record LlmPreset(string Key, string BaseUrl, string DefaultModel, bool NeedsKey);

public static class LlmPresets
{
    public static readonly LlmPreset[] All =
    [
        new("DeepSeek", "https://api.deepseek.com/v1", "deepseek-chat", true),
        new("OpenAI", "https://api.openai.com/v1", "gpt-4o", true),
        new("Claude", "https://api.anthropic.com/v1", "claude-sonnet-4-5", true),
        new("Ollama", "http://127.0.0.1:11434/v1", "qwen3:14b", false),
    ];

    public static LlmPreset Get(string? key) =>
        All.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase)) ?? All[0];

    public static string DisplayName(LlmPreset preset) => I18n.T("Vox.Settings.Llm.Provider." + preset.Key);
}

public sealed record LlmResult(bool Ok, string Text, string? Error, TimeSpan Elapsed);

/// <summary>
/// 大模型通道：密钥按服务商存系统凭据管理器（vox/secret/llm/&lt;provider&gt;），请求 / 响应全部走源生成 JSON。
/// 写作页 AI 助手与「测试连接」共用这一入口。
/// </summary>
public static class LlmService
{
    private static readonly HttpClient Http = new();

    public static string CredentialKey(string provider) => "llm/" + provider.ToLowerInvariant();

    public static string? GetApiKey(string provider)
    {
        try
        {
            return CredentialStoreFactory.Create().Get(CredentialKey(provider));
        }
        catch
        {
            return null;
        }
    }

    public static bool HasApiKey(string provider) => !string.IsNullOrWhiteSpace(GetApiKey(provider));

    public static void SaveApiKey(string provider, string key)
    {
        var store = CredentialStoreFactory.Create();
        if (string.IsNullOrWhiteSpace(key)) store.Delete(CredentialKey(provider));
        else store.Save(CredentialKey(provider), key.Trim());
    }

    public static async Task<LlmResult> CompleteAsync(
        string provider,
        string model,
        string system,
        string user,
        int maxTokens = 1400,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var preset = LlmPresets.Get(provider);
        var resolvedModel = string.IsNullOrWhiteSpace(model) ? preset.DefaultModel : model.Trim();
        var apiKey = GetApiKey(preset.Key);
        if (preset.NeedsKey && string.IsNullOrWhiteSpace(apiKey))
        {
            return new LlmResult(false, string.Empty, I18n.T("Vox.Ai.Error.NoKey"), TimeSpan.Zero);
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout ?? TimeSpan.FromSeconds(75));
        var watch = Stopwatch.StartNew();
        try
        {
            var isClaude = preset.Key == "Claude";
            using var request = isClaude
                ? BuildAnthropic(preset, resolvedModel, apiKey!, system, user, maxTokens)
                : BuildOpenAiCompatible(preset, resolvedModel, apiKey, system, user, maxTokens);
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

    private static HttpRequestMessage BuildOpenAiCompatible(
        LlmPreset preset, string model, string? apiKey, string system, string user, int maxTokens)
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
        var request = new HttpRequestMessage(HttpMethod.Post, preset.BaseUrl.TrimEnd('/') + "/chat/completions")
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
        LlmPreset preset, string model, string apiKey, string system, string user, int maxTokens)
    {
        var payload = new AnthropicRequest
        {
            Model = model,
            MaxTokens = maxTokens,
            System = system,
            Messages = [new LlmMessage { Role = "user", Content = user }],
        };
        var request = new HttpRequestMessage(HttpMethod.Post, preset.BaseUrl.TrimEnd('/') + "/messages")
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
