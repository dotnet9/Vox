using System.Text.Json;

namespace Vox.Core.Llm;

/// <summary>流式响应协议：OpenAI 兼容（GLM / DeepSeek / OpenAI / Ollama）与 Anthropic messages。</summary>
public enum LlmStreamProtocol
{
    OpenAiCompatible,
    Anthropic,
}

/// <summary>一条 SSE data 行的解析结果。</summary>
/// <param name="Text">内容增量；null 表示本行不产生正文。</param>
/// <param name="Done">流已正常结束（[DONE] / finish_reason / message_stop / message_delta 停止）。</param>
/// <param name="Error">流内错误描述；null 表示无错误。</param>
public readonly record struct LlmStreamEvent(string? Text, bool Done, string? Error)
{
    public static LlmStreamEvent Delta(string text) => new(text, false, null);
    public static readonly LlmStreamEvent Finished = new(null, true, null);
    public static LlmStreamEvent Failed(string error) => new(null, false, error);
    public static readonly LlmStreamEvent Empty = new(null, false, null);
}

/// <summary>
/// SSE data 行解析器（纯函数，供流式 LLM 通道与单测共用）。
/// 只喂入去掉 "data:" 前缀后的载荷；"event:"、"comment"、空行由传输层跳过。
/// </summary>
public static class LlmStreamParser
{
    public static LlmStreamEvent Parse(LlmStreamProtocol protocol, string data)
    {
        if (string.IsNullOrWhiteSpace(data)) return LlmStreamEvent.Empty;
        var payload = data.Trim();
        if (payload == "[DONE]") return LlmStreamEvent.Finished;
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(payload);
        }
        catch (JsonException)
        {
            // 非JSON 载荷（网关注入的心跳/HTML 片段）：跳过，不中断整条流。
            return LlmStreamEvent.Empty;
        }
        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return LlmStreamEvent.Empty;
            if (protocol == LlmStreamProtocol.Anthropic) return ParseAnthropic(root);
            return ParseOpenAiCompatible(root);
        }
    }

    private static LlmStreamEvent ParseOpenAiCompatible(JsonElement root)
    {
        if (root.TryGetProperty("error", out var error))
        {
            return LlmStreamEvent.Failed(error.ValueKind == JsonValueKind.String
                ? error.GetString() ?? "stream error"
                : error.TryGetProperty("message", out var message) && message.GetString() is { Length: > 0 } msg
                    ? msg
                    : "stream error");
        }
        string? text = null;
        var done = false;
        if (root.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array
            && choices.GetArrayLength() > 0)
        {
            var choice = choices[0];
            if (choice.TryGetProperty("delta", out var delta)
                && delta.TryGetProperty("content", out var content)
                && content.ValueKind == JsonValueKind.String)
            {
                text = content.GetString();
            }
            if (choice.TryGetProperty("finish_reason", out var finish)
                && finish.ValueKind == JsonValueKind.String)
            {
                done = true; // stop / length / content_filter：正文出完即收
            }
        }
        if (text is { Length: > 0 }) return new LlmStreamEvent(text, done, null);
        return done ? LlmStreamEvent.Finished : LlmStreamEvent.Empty;
    }

    private static LlmStreamEvent ParseAnthropic(JsonElement root)
    {
        var type = root.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String
            ? t.GetString()
            : null;
        switch (type)
        {
            case "content_block_delta":
                if (root.TryGetProperty("delta", out var delta)
                    && delta.TryGetProperty("text", out var text)
                    && text.ValueKind == JsonValueKind.String
                    && text.GetString() is { Length: > 0 } chunk)
                {
                    return LlmStreamEvent.Delta(chunk);
                }
                return LlmStreamEvent.Empty;
            case "message_stop":
                return LlmStreamEvent.Finished;
            case "message_delta":
            {
                // 带 stop_reason 的收尾帧：正文可能已出完，视为结束。
                var stopped = root.TryGetProperty("delta", out var d)
                    && d.TryGetProperty("stop_reason", out var reason)
                    && reason.ValueKind == JsonValueKind.String;
                return stopped ? LlmStreamEvent.Finished : LlmStreamEvent.Empty;
            }
            case "error":
                if (root.TryGetProperty("error", out var error)
                    && error.TryGetProperty("message", out var message)
                    && message.GetString() is { Length: > 0 } msg)
                {
                    return LlmStreamEvent.Failed(msg);
                }
                return LlmStreamEvent.Failed("stream error");
            default:
                // message_start / content_block_start / content_block_stop / ping：无正文
                return LlmStreamEvent.Empty;
        }
    }
}
