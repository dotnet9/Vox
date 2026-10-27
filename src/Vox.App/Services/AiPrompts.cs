namespace Vox.App.Services;

/// <summary>一次生成的提示词：system 固定编辑角色，user 由预设任务 + 上下文拼成。</summary>
public sealed record AiPrompt(string System, string User);

/// <summary>写作页 AI 助手的指令模板（与原型六个预设一一对应；输出约定只给结果本身）。</summary>
public static class AiPrompts
{
    private const int ContextChars = 5000;

    public static readonly string[] PresetKeys = ["Continue", "Polish", "Summary", "Title", "Translate", "Alt"];

    private const string SystemPrompt =
        "你是资深中文技术博客编辑，为作者的 Markdown 文章提供可直接采用的改稿结果。" +
        "严格遵守：只输出结果本身，不解释、不加前后缀、不包裹代码围栏（除非原文就是代码）；保持 Markdown 结构与技术术语准确。";

    public static AiPrompt Build(string presetKey, string body, int caret, string selection, string custom)
    {
        var offset = Math.Clamp(caret, 0, body.Length);
        var section = SectionAt(body, offset) ?? string.Empty;
        var paragraph = ParagraphAt(body, offset);
        var source = selection.Length > 0 ? selection : paragraph.Text;
        var context = body[Math.Max(0, offset - ContextChars)..offset];
        var head = body[..Math.Min(body.Length, ContextChars)];

        var user = presetKey switch
        {
            "Continue" =>
                "任务：从光标处续写下面这一节，语气与结构保持连贯，只输出续写内容（不要重复已有段落）。\n\n" +
                $"当前章节：{(section.Length > 0 ? section : "正文")}\n\n文章（截至光标）：\n{context}",
            "Polish" =>
                "任务：润色改写下面这段文字，保留原意与 Markdown 标记，只输出改写结果。\n\n" + source,
            "Summary" =>
                "任务：为这篇文章写一句不超过 60 字的中文摘要（用作 front matter 的 description），只输出摘要。\n\n文章：\n" + head,
            "Title" =>
                "任务：为这篇文章给出 5 个中文标题候选，每行一个，不加编号与解释。\n\n文章：\n" + head,
            "Translate" =>
                "任务：把下面内容翻译成英文，保留 Markdown 结构，只输出译文。\n\n" + source,
            "Alt" =>
                "任务：为文章中的每个 Markdown 图片引用写一句中文 alt 文本，逐行输出「图片路径 → alt」，不要改动正文。\n\n文章：\n" + head,
            _ =>
                "任务：" + (custom.Trim().Length > 0 ? custom.Trim() : "改进下面这段文字，只输出改进结果。") +
                "\n\n当前章节：" + (section.Length > 0 ? section : "正文") +
                "\n\n上下文：\n" + (source.Length > 0 ? source : head),
        };
        return new AiPrompt(SystemPrompt, user);
    }

    /// <summary>文字型预设（润色 / 翻译 / 自定义）替换选区或光标所在段落；其余为插入或复制。</summary>
    public static bool ReplacesSource(string presetKey) => presetKey is "Polish" or "Translate" or "Custom";

    public static bool WritesBody(string presetKey) => presetKey is "Continue" or "Polish" or "Translate" or "Custom";

    /// <summary>光标所在段落（以空行分隔）的原文与区间；用于无选区时的润色 / 翻译源。</summary>
    public static (int Offset, int Length, string Text) ParagraphAt(string text, int caret)
    {
        var offset = Math.Clamp(caret, 0, text.Length);
        var start = offset == 0 ? 0 : text.LastIndexOf("\n\n", Math.Max(0, offset - 1), StringComparison.Ordinal);
        start = start < 0 ? 0 : start + 2;
        var end = offset >= text.Length ? text.Length : text.IndexOf("\n\n", offset, StringComparison.Ordinal);
        if (end < 0) end = text.Length;
        while (start < end && char.IsWhiteSpace(text[start])) start++;
        while (end > start && char.IsWhiteSpace(text[end - 1])) end--;
        if (start >= end)
        {
            // 光标落在空行：退化为整段上下文，避免把空串送给模型。
            start = Math.Max(0, offset - 800);
            end = Math.Min(text.Length, offset + 800);
        }
        return (start, end - start, text[start..end]);
    }

    /// <summary>光标之前最近的 Markdown 标题文本（审计与提示里标注章节）。</summary>
    public static string? SectionAt(string body, int caret)
    {
        var before = body[..Math.Clamp(caret, 0, body.Length)];
        var lines = before.Split('\n');
        for (var i = lines.Length - 1; i >= 0; i--)
        {
            var line = lines[i].Trim();
            if (!line.StartsWith('#')) continue;
            var title = line.TrimStart('#').Trim().TrimEnd('#').Trim();
            if (title.Length > 0) return title;
        }
        return null;
    }
}

/// <summary>一行 diff：' ' 上下文 / '+' 新增 / '-' 删除。</summary>
public readonly record struct AiDiffLine(char Kind, string Text);

/// <summary>行级 LCS diff：生成结果与原文的差异预览（采纳前可见改了哪里）。</summary>
public static class AiDiff
{
    public static IReadOnlyList<AiDiffLine> Build(string before, string after, int maxLines = 120)
    {
        var oldLines = Split(before);
        var newLines = Split(after);
        var ops = Lcs(oldLines, newLines);
        var compacted = Compact(ops, context: 3);
        return compacted.Count > maxLines ? compacted.GetRange(0, maxLines) : compacted;
    }

    private static string[] Split(string text) =>
        text.Replace("\r\n", "\n").Split('\n').Take(240).ToArray();

    private static List<AiDiffLine> Lcs(string[] a, string[] b)
    {
        var n = a.Length;
        var m = b.Length;
        var dp = new int[n + 1, m + 1];
        for (var i = n - 1; i >= 0; i--)
        {
            for (var j = m - 1; j >= 0; j--)
            {
                dp[i, j] = a[i] == b[j] ? dp[i + 1, j + 1] + 1 : Math.Max(dp[i + 1, j], dp[i, j + 1]);
            }
        }
        var lines = new List<AiDiffLine>();
        int x = 0, y = 0;
        while (x < n && y < m)
        {
            if (a[x] == b[y])
            {
                lines.Add(new AiDiffLine(' ', a[x]));
                x++;
                y++;
            }
            else if (dp[x + 1, y] >= dp[x, y + 1])
            {
                lines.Add(new AiDiffLine('-', a[x++]));
            }
            else
            {
                lines.Add(new AiDiffLine('+', b[y++]));
            }
        }
        while (x < n) lines.Add(new AiDiffLine('-', a[x++]));
        while (y < m) lines.Add(new AiDiffLine('+', b[y++]));
        return lines;
    }

    /// <summary>长段未改动收敛为「…」，让弹层里只留改动附近的行。</summary>
    private static List<AiDiffLine> Compact(List<AiDiffLine> ops, int context)
    {
        var result = new List<AiDiffLine>();
        var i = 0;
        while (i < ops.Count)
        {
            if (ops[i].Kind != ' ')
            {
                result.Add(ops[i++]);
                continue;
            }
            var start = i;
            while (i < ops.Count && ops[i].Kind == ' ') i++;
            var run = i - start;
            if (run <= context * 2 + 1)
            {
                result.AddRange(ops.GetRange(start, run));
            }
            else
            {
                result.AddRange(ops.GetRange(start, context));
                result.Add(new AiDiffLine(' ', "…"));
                result.AddRange(ops.GetRange(i - context, context));
            }
        }
        return result;
    }
}
