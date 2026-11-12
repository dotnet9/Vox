using Vox.Core.Llm;
using Xunit;

namespace Vox.Core.Tests;

/// <summary>会话历史整形：无论 UI 侧历史如何攒出来，请求前一律收敛为「首条 user、角色交替、无空消息」。</summary>
public class LlmChatHistoryTests
{
    [Fact]
    public void Normalize_合并相邻同角色消息()
    {
        var result = LlmChatHistory.Normalize(
        [
            new LlmChatTurn("user", "第一个问题"),
            new LlmChatTurn("user", "重试的问题"),
            new LlmChatTurn("assistant", "回答"),
        ]);

        Assert.Equal(2, result.Count);
        Assert.Equal("user", result[0].Role);
        Assert.Equal("第一个问题\n\n重试的问题", result[0].Content);
        Assert.Equal("assistant", result[1].Role);
    }

    [Fact]
    public void Normalize_丢弃前导assistant消息()
    {
        // 预设指令的回复先入史而提问缺失时，历史以 assistant 开头——Anthropic 协议要求首条 user，直接丢弃前导。
        var result = LlmChatHistory.Normalize(
        [
            new LlmChatTurn("assistant", "这是一段改写结果"),
            new LlmChatTurn("user", "继续问"),
            new LlmChatTurn("assistant", "回答"),
        ]);

        Assert.Equal(2, result.Count);
        Assert.Equal("user", result[0].Role);
    }

    [Fact]
    public void Normalize_丢弃空消息与未知角色()
    {
        var result = LlmChatHistory.Normalize(
        [
            new LlmChatTurn("user", ""),
            new LlmChatTurn("user", "  "),
            new LlmChatTurn("system", "不该出现在历史里"),
            new LlmChatTurn("user", "有效问题"),
            new LlmChatTurn("assistant", null!),
        ]);

        var turn = Assert.Single(result);
        Assert.Equal("user", turn.Role);
        Assert.Equal("有效问题", turn.Content);
    }

    [Fact]
    public void Normalize_交替历史原样保留()
    {
        var turns = new[]
        {
            new LlmChatTurn("user", "问1"),
            new LlmChatTurn("assistant", "答1"),
            new LlmChatTurn("user", "问2"),
            new LlmChatTurn("assistant", "答2"),
        };

        var result = LlmChatHistory.Normalize(turns);

        Assert.Equal(4, result.Count);
        Assert.Equal("问1", result[0].Content);
        Assert.Equal("答2", result[^1].Content);
    }

    [Fact]
    public void Normalize_全空历史收敛为空()
    {
        var result = LlmChatHistory.Normalize(
        [
            new LlmChatTurn("assistant", "没有提问的回复"),
            new LlmChatTurn("assistant", ""),
        ]);

        Assert.Empty(result);
    }

    [Fact]
    public void Normalize_前后空白被修剪()
    {
        var result = LlmChatHistory.Normalize([new LlmChatTurn("user", "  问题  ")]);

        var turn = Assert.Single(result);
        Assert.Equal("问题", turn.Content);
    }
}
