namespace Vox.Core.Llm;

/// <summary>多轮会话中的一条消息（user / assistant）：会话历史的最小单元，与通道层请求 DTO 解耦。</summary>
public sealed record LlmChatTurn(string Role, string Content);

/// <summary>
/// 会话历史整形（纯函数）：无论 UI 侧历史如何产生（预设指令、发送失败重试、停止截断），
/// 请求前统一收敛为「首条 user、角色交替、无空消息」——Anthropic messages 协议对序列有硬校验，
/// OpenAI 兼容协议虽宽容，收敛后上下文也不含歧义。
/// </summary>
public static class LlmChatHistory
{
    /// <summary>相邻同角色合并（\n\n 连接）、丢弃空消息与前导 assistant 消息。</summary>
    public static IReadOnlyList<LlmChatTurn> Normalize(IEnumerable<LlmChatTurn> turns)
    {
        var result = new List<LlmChatTurn>();
        foreach (var turn in turns)
        {
            var role = turn.Role?.Trim();
            var content = turn.Content?.Trim();
            if (content is not { Length: > 0 } || role is not ("user" or "assistant")) continue;
            // 首条必须是 user：会话中途构造出的前导 assistant（如预设回复先入史）没有对应提问，丢弃。
            if (result.Count == 0 && role != "user") continue;
            if (result.Count > 0 && result[^1].Role == role)
            {
                result[^1] = result[^1] with { Content = result[^1].Content + "\n\n" + content };
                continue;
            }
            result.Add(new LlmChatTurn(role, content));
        }
        return result;
    }
}
