using Vox.Core.Llm;
using Xunit;

public class LlmStreamParserTests
{
    [Fact]
    public void Openai_compatible_chunk_yields_delta_and_done_marker_finishes()
    {
        var delta = LlmStreamParser.Parse(LlmStreamProtocol.OpenAiCompatible,
            """{"choices":[{"delta":{"content":"你好"},"index":0}]}""");
        Assert.Equal("你好", delta.Text);
        Assert.False(delta.Done);

        var done = LlmStreamParser.Parse(LlmStreamProtocol.OpenAiCompatible, "[DONE]");
        Assert.True(done.Done);
        Assert.Null(done.Text);
    }

    [Fact]
    public void Openai_finish_reason_completes_stream()
    {
        var frame = LlmStreamParser.Parse(LlmStreamProtocol.OpenAiCompatible,
            """{"choices":[{"delta":{},"finish_reason":"stop"}]}""");
        Assert.True(frame.Done);
        Assert.Null(frame.Text);
    }

    [Fact]
    public void Openai_error_object_is_reported()
    {
        var frame = LlmStreamParser.Parse(LlmStreamProtocol.OpenAiCompatible,
            """{"error":{"message":"rate limited"}}""");
        Assert.Equal("rate limited", frame.Error);
        Assert.Null(frame.Text);
    }

    [Fact]
    public void Anthropic_content_block_delta_yields_text()
    {
        var frame = LlmStreamParser.Parse(LlmStreamProtocol.Anthropic,
            """{"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"Hello"}}""");
        Assert.Equal("Hello", frame.Text);
        Assert.False(frame.Done);
    }

    [Fact]
    public void Anthropic_message_stop_finishes_and_delta_stop_reason_finishes()
    {
        Assert.True(LlmStreamParser.Parse(LlmStreamProtocol.Anthropic, """{"type":"message_stop"}""").Done);
        Assert.True(LlmStreamParser.Parse(LlmStreamProtocol.Anthropic,
            """{"type":"message_delta","delta":{"stop_reason":"end_turn"}}""").Done);
    }

    [Fact]
    public void Anthropic_error_event_is_reported()
    {
        var frame = LlmStreamParser.Parse(LlmStreamProtocol.Anthropic,
            """{"type":"error","error":{"type":"overloaded_error","message":"Overloaded"}}""");
        Assert.Equal("Overloaded", frame.Error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(": keep-alive")]
    [InlineData("<html>502 Bad Gateway</html>")]
    [InlineData("""{"type":"ping"}""")]
    [InlineData("""{"choices":[]}""")]
    public void Non_payload_lines_are_ignored(string data)
    {
        var openAi = LlmStreamParser.Parse(LlmStreamProtocol.OpenAiCompatible, data);
        Assert.False(openAi.Done);
        Assert.Null(openAi.Text);
        Assert.Null(openAi.Error);

        var anthropic = LlmStreamParser.Parse(LlmStreamProtocol.Anthropic, data);
        Assert.False(anthropic.Done);
        Assert.Null(anthropic.Text);
        Assert.Null(anthropic.Error);
    }

    [Fact]
    public void Deltas_concatenate_to_full_reply()
    {
        var frames = new[]
        {
            """{"choices":[{"delta":{"content":"第一段。"}}]}""",
            """{"choices":[{"delta":{"content":"\n\n第二段。"}}]}""",
            """{"choices":[{"delta":{},"finish_reason":"stop"}]}""",
            "[DONE]",
        };
        var text = string.Concat(frames
            .Select(f => LlmStreamParser.Parse(LlmStreamProtocol.OpenAiCompatible, f))
            .Where(e => e.Text != null)
            .Select(e => e.Text));
        Assert.Equal("第一段。\n\n第二段。", text);
    }

    [Fact]
    public void Openai_reasoning_content_yields_reasoning_delta()
    {
        var frame = LlmStreamParser.Parse(LlmStreamProtocol.OpenAiCompatible,
            """{"choices":[{"delta":{"reasoning_content":"先分析问题"}}]}""");
        Assert.Equal("先分析问题", frame.Reasoning);
        Assert.Null(frame.Text);
        Assert.False(frame.Done);
    }

    [Fact]
    public void Openai_reasoning_then_content_transition()
    {
        var reasoning = LlmStreamParser.Parse(LlmStreamProtocol.OpenAiCompatible,
            """{"choices":[{"delta":{"reasoning_content":"想"}}]}""");
        var content = LlmStreamParser.Parse(LlmStreamProtocol.OpenAiCompatible,
            """{"choices":[{"delta":{"reasoning_content":"","content":"答"}}]}""");
        Assert.Equal("想", reasoning.Reasoning);
        Assert.Equal("答", content.Text); // 空 reasoning 不产生思考增量，正文正常出
        Assert.Null(content.Reasoning);
    }

    [Fact]
    public void Anthropic_thinking_delta_yields_reasoning_and_text_delta_yields_text()
    {
        var thinking = LlmStreamParser.Parse(LlmStreamProtocol.Anthropic,
            """{"type":"content_block_delta","delta":{"type":"thinking_delta","thinking":"分析"}}""");
        Assert.Equal("分析", thinking.Reasoning);
        Assert.Null(thinking.Text);

        var text = LlmStreamParser.Parse(LlmStreamProtocol.Anthropic,
            """{"type":"content_block_delta","delta":{"type":"text_delta","text":"正文"}}""");
        Assert.Equal("正文", text.Text);
        Assert.Null(text.Reasoning);
    }
}
