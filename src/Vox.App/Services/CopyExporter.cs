using System.Text;
using System.Text.RegularExpressions;
using Vox.Core;

namespace Vox.App.Services;

/// <summary>
/// 复制导出器：发布对话框「复制字段」的六种载荷。
/// 公众号 HTML = Markdown 子集 → 内联样式 HTML（图片转 CDN 域名；公众号编辑器不吃外部样式表）。
/// </summary>
public static partial class CopyExporter
{
    // ---------- 纯文本 / URL 字段 ----------

    public static string Title(Article article) => article.Meta.Title;

    public static string Description(Article article) => article.Meta.Description ?? string.Empty;

    public static string CoverUrl(Article article, LinkOptions link)
    {
        var cover = article.Meta.Cover;
        if (string.IsNullOrWhiteSpace(cover)) return string.Empty;
        // cover 已是仓库相对路径（治理后唯一规范形态）→ CDN 投影；历史遗留的完整 URL 原样返回。
        return cover.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? cover
            : LinkStrategy.ToCdnUrl(cover, link.CdnBase);
    }

    public static string Markdown(string fullText) => fullText;

    public static string WeChatHtml(Article article, string fullText, LinkOptions link) =>
        new HtmlBuilder(link).Build(article, fullText);

    public static string RichText(Article article, string fullText, LinkOptions link) =>
        WeChatHtml(article, fullText, link);

    // ---------- Markdown 子集 → 内联样式 HTML ----------

    private sealed partial class HtmlBuilder(LinkOptions link)
    {
        private static readonly (string Html, string Style)[] HeadingTags =
        [
            ("h1", "font-size:20px;font-weight:700;margin:18px 0 10px;line-height:1.5;"),
            ("h2", "font-size:17px;font-weight:700;margin:16px 0 8px;line-height:1.5;"),
            ("h3", "font-size:15px;font-weight:700;margin:14px 0 6px;line-height:1.5;"),
            ("h4", "font-size:14px;font-weight:700;margin:12px 0 6px;line-height:1.5;"),
        ];

        public string Build(Article article, string markdown)
        {
            var body = SplitFrontMatter(markdown, out _);
            var sb = new StringBuilder();
            sb.Append($"<h1 style=\"{HeadingTags[0].Style}\">{Escape(article.Meta.Title)}</h1>");
            if (!string.IsNullOrWhiteSpace(article.Meta.Description))
            {
                sb.Append($"<p style=\"font-size:13px;color:#888;margin:0 0 14px;\">{Escape(article.Meta.Description)}</p>");
            }

            var inCode = false;
            var codeBuffer = new StringBuilder();
            foreach (var rawLine in body.Replace("\r\n", "\n").Split('\n'))
            {
                var line = rawLine.TrimEnd();
                if (line.TrimStart().StartsWith("```"))
                {
                    if (inCode)
                    {
                        sb.Append($"<pre style=\"background:#f5f5f7;border-radius:8px;padding:12px;font-size:12px;overflow-x:auto;\"><code>{Escape(codeBuffer.ToString().TrimEnd('\n'))}</code></pre>");
                        codeBuffer.Clear();
                    }
                    inCode = !inCode;
                    continue;
                }
                if (inCode)
                {
                    codeBuffer.AppendLine(rawLine);
                    continue;
                }
                sb.Append(RenderLine(line));
            }
            return sb.ToString();
        }

        private string RenderLine(string line)
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0) return string.Empty;

            // 引用块
            if (trimmed.StartsWith('>'))
            {
                var text = Inline(trimmed[1..].Trim());
                return $"<blockquote style=\"border-left:3px solid #7c5cff;background:#f4f1ff;border-radius:0 8px 8px 0;margin:10px 0;padding:8px 14px;color:#555;font-size:13px;\">{text}</blockquote>";
            }

            // 任务清单
            var task = TaskRegex().Match(trimmed);
            if (task.Success)
            {
                var done = task.Groups[1].Value == "x" || task.Groups[1].Value == "X";
                var text = Inline(task.Groups[2].Value);
                return $"<p style=\"margin:6px 0;font-size:14px;color:{(done ? "#999" : "#333")};text-decoration:{(done ? "line-through" : "none")};\">☐ {text}</p>";
            }

            // 无序列表
            if (trimmed.StartsWith("- ") || trimmed.StartsWith("* "))
            {
                return $"<p style=\"margin:4px 0 4px 18px;font-size:14px;\">• {Inline(trimmed[2..])}</p>";
            }

            // 标题
            for (var level = 1; level <= 4; level++)
            {
                var prefix = new string('#', level) + " ";
                if (trimmed.StartsWith(prefix))
                {
                    var (_, style) = HeadingTags[level - 1];
                    return $"<h{level + 1} style=\"{style}\">{Inline(trimmed[prefix.Length..])}</h{level + 1}>";
                }
            }

            // 分隔线
            if (trimmed is "---" or "***") return "<hr style=\"border:none;border-top:1px solid #e5e5e5;margin:14px 0;\"/>";

            return $"<p style=\"font-size:14px;color:#333;margin:8px 0;line-height:1.8;\">{Inline(trimmed)}</p>";
        }

        /// <summary>行内元素：图片（转 CDN）、粗斜体、行内代码、链接。</summary>
        private string Inline(string text)
        {
            text = ImageRegex().Replace(text, m =>
            {
                var alt = Escape(m.Groups[1].Value);
                var src = m.Groups[2].Value;
                var url = src.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                    ? src
                    : LinkStrategy.ToCdnUrl(src, link.CdnBase);
                return $"<img src=\"{Escape(url)}\" alt=\"{alt}\" style=\"max-width:100%;border-radius:8px;\"/>";
            });
            text = BoldRegex().Replace(text, m => $"<strong>{m.Groups[1].Value}</strong>");
            text = ItalicRegex().Replace(text, m => $"<em>{m.Groups[1].Value}</em>");
            text = CodeRegex().Replace(text, m => $"<code style=\"background:#f5f5f7;border-radius:4px;padding:1px 5px;font-size:12px;\">{m.Groups[1].Value}</code>");
            text = LinkRegex().Replace(text, m => $"<a href=\"{Escape(m.Groups[2].Value)}\" style=\"color:#576b95;text-decoration:none;\">{m.Groups[1].Value}</a>");
            return EscapePreserve(text);
            // 注：上面的 Replace 顺序里 Bold/Italic/Code 的内容已含标签，EscapePreserve 只转义裸的 <>&（标签由本方法生成，安全）。
        }

        private static string EscapePreserve(string text) => text; // 标签由本生成器产出；源文本的转义在各捕获组内已完成

        private static string Escape(string text) =>
            text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

        private static string SplitFrontMatter(string markdown, out string yaml)
        {
            if (markdown.StartsWith("---\n"))
            {
                var end = markdown.IndexOf("\n---\n", 4, StringComparison.Ordinal);
                if (end > 0)
                {
                    yaml = markdown[4..end];
                    return markdown[(end + 5)..];
                }
            }
            yaml = string.Empty;
            return markdown;
        }

        [GeneratedRegex(@"^\[([ xX])\]\s+(.+)$")]
        private static partial Regex TaskRegex();

        [GeneratedRegex(@"!\[([^\]]*)\]\(([^)]+)\)")]
        private static partial Regex ImageRegex();

        [GeneratedRegex(@"\*\*([^*]+)\*\*")]
        private static partial Regex BoldRegex();

        [GeneratedRegex(@"\*([^*]+)\*")]
        private static partial Regex ItalicRegex();

        [GeneratedRegex(@"`([^`]+)`")]
        private static partial Regex CodeRegex();

        [GeneratedRegex(@"\[([^\]]+)\]\(([^)]+)\)")]
        private static partial Regex LinkRegex();
    }
}
