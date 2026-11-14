namespace Vox.App.Services;

/// <summary>一次生成的提示词：system 固定编辑角色，user 由预设任务 + 上下文拼成。</summary>
public sealed record AiPrompt(string System, string User);

/// <summary>写作页 AI 助手的指令模板（与原型六个预设一一对应；输出约定只给结果本身）。</summary>
public static class AiPrompts
{
    private const int ContextChars = 5000;

    public static readonly string[] PresetKeys = ["Continue", "Polish", "Summary", "Title", "Translate", "Alt"];

    /// <summary>工作区帮助注册表（全局 AI 会话页面感知，2026-11-14）：key 对应 MainWindow 工作区。
    /// 内容对齐《软件操作手册》各章——AI 回答"这个界面怎么用"时以此为据，不臆测。</summary>
    public static readonly IReadOnlyDictionary<string, string> PageHelp = new Dictionary<string, string>
    {
        ["write"] = "写作工作台：左侧文章列表（搜索/年月树），中部源码/分屏/预览/导图四态，右侧元数据/大纲/图片面板；自动保存，Ctrl+S 手动保存；正文选中文字可加入 AI 会话作为引用上下文",
        ["library"] = "文章库：统计卡（篇数/字数/图片/待发布）、筛选（年份/分类/状态/平台）、搜索（Ctrl+F）、表格列排序、行操作（编辑/重命名/删除）、分页；重命名与删除均为 Git 工作区操作可回滚",
        ["images"] = "图片库：统计、筛选（年份/引用/格式）、排序（路径/文件名/大小/引用数）、搜索、缩略图墙（点选批量操作：复制路径/删除）、引用详情、未引用图片清理、图片入库（本地/网图 URL）",
        ["publish"] = "发布中心：网站 Git 同步（变更列表/diff/提交信息/提交推送，Ctrl+Enter 快捷提交）、多平台状态矩阵（点击文章名跳编辑）、发布前检查、发布历史（双击行打开发布框）；平台 API 未配置时降级复制助手",
        ["site"] = "站点数据：site/*.json 左列表 + 右表格/原文编辑；脏标记（标题●）与切换守卫防丢失，Ctrl+S 保存；删除供应商/条目走确认流",
        ["mcp"] = "MCP 服务：服务启停与端口/令牌配置（本机 127.0.0.1，Bearer 鉴权）、授权范围、客户端接入、工具目录、调用审计（分页筛导出）；写操作确认策略",
        ["settings"] = "设置：仓库/自动保存/大纲序号、链接策略（引用形态/域名映射）、Git 凭据、平台账户、模型供应商管理（添加供应商→配 Key→启用）、AI 助手开关、外观/语言/密度",
    };

    public const string SystemPrompt =
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
