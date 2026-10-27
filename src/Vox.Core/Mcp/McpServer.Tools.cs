using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Vox.Core.Mcp;

/// <summary>MCP 工具层：只读工具直接映射 RepoScanner，写入工具经界面确认后落盘。</summary>
public sealed partial class McpServer
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(45) };
    private const long MaxImageBytes = 30 * 1024 * 1024;

    private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".gif", ".webp", ".svg"];

    /// <summary>本次调用是否经应用内确认（审计区分「成功」与「已确认」，并发请求各行其道）。</summary>
    private sealed class CallState
    {
        public bool Confirmed;
    }

    private static readonly AsyncLocal<CallState?> CurrentCall = new();

    private sealed record McpToolOutcome(bool Ok, string Text, string Target, string Detail, bool Denied = false);

    private async Task<JsonObject> CallToolAsync(JsonObject? parameters, Session session, CancellationToken ct)
    {
        var started = Stopwatch.GetTimestamp();
        var call = new CallState();
        CurrentCall.Value = call;
        var name = McpJson.Str(parameters, "name") ?? string.Empty;
        var args = parameters?["arguments"] as JsonObject ?? [];
        var tool = McpCatalog.Find(name);

        McpToolOutcome outcome;
        if (tool == null)
        {
            outcome = Fail($"未知工具：{name}", name);
        }
        else if (!McpCatalog.IsVisible(tool, _options))
        {
            outcome = Deny("工具在当前授权范围或开关下不可见。", tool.Name);
        }
        else
        {
            try
            {
                outcome = await ExecuteToolAsync(tool, args, session.Name, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception error)
            {
                outcome = Fail(error.Message, tool.Name);
            }
        }

        var result = outcome.Denied ? "deny"
            : !outcome.Ok ? "error"
            : call.Confirmed ? "okc"
            : "ok";
        Audit(session.Name, tool?.Name ?? name, string.IsNullOrWhiteSpace(outcome.Target) ? name : outcome.Target,
            result, outcome.Detail, ElapsedMs(started));
        lock (_sessionGate) session.Calls++;

        return new JsonObject
        {
            ["content"] = new JsonArray(new JsonObject
            {
                ["type"] = "text",
                ["text"] = outcome.Text,
            }),
            ["isError"] = !outcome.Ok,
        };
    }

    private async Task<McpToolOutcome> ExecuteToolAsync(
        McpToolDescriptor tool, JsonObject args, string client, CancellationToken ct) => tool.Name switch
    {
        "article_list" => ArticleList(args),
        "article_read" => ArticleRead(args),
        "article_search" => ArticleSearch(args),
        "article_write" => await ArticleWriteAsync(args, client, ct).ConfigureAwait(false),
        "article_create" => await ArticleCreateAsync(args, client, ct).ConfigureAwait(false),
        "article_meta" => await ArticleMetaAsync(args, client, ct).ConfigureAwait(false),
        "image_list" => ImageList(args),
        "image_usage" => ImageUsage(args),
        "image_import" => await ImageImportAsync(args, client, ct).ConfigureAwait(false),
        "image_replace" => await ImageReplaceAsync(args, client, ct).ConfigureAwait(false),
        "publish_status" => await PublishStatusAsync(args, ct).ConfigureAwait(false),
        "publish_check" => await PublishCheckAsync(args, ct).ConfigureAwait(false),
        "publish_article" => await PublishArticleAsync(args, client, ct).ConfigureAwait(false),
        "git_status" => await GitStatusAsync(ct).ConfigureAwait(false),
        "git_commit" => await GitCommitAsync(args, client, ct).ConfigureAwait(false),
        "git_push" => await GitPushAsync(client, ct).ConfigureAwait(false),
        "outline_read" => OutlineRead(args),
        "stats_summary" => StatsSummary(),
        _ => Fail($"工具未实现：{tool.Name}", tool.Name),
    };

    // ---------- 文章 ----------

    private McpToolOutcome ArticleList(JsonObject args)
    {
        var scan = Scan();
        IEnumerable<Article> query = scan.Articles;
        var year = McpJson.Str(args, "year");
        var month = McpJson.Str(args, "month");
        var category = McpJson.Str(args, "category");
        var keyword = McpJson.Str(args, "keyword");
        var draft = McpJson.Has(args, "draft") ? McpJson.Bool(args, "draft") : (bool?)null;

        if (!string.IsNullOrWhiteSpace(year)) query = query.Where(a => a.Year == year.Trim());
        if (!string.IsNullOrWhiteSpace(month))
        {
            var wanted = month.Trim().PadLeft(2, '0');
            query = query.Where(a => a.Month == wanted);
        }
        if (!string.IsNullOrWhiteSpace(category))
        {
            query = query.Where(a => a.Meta.Categories.Any(c =>
                c.Contains(category.Trim(), StringComparison.OrdinalIgnoreCase)));
        }
        if (draft.HasValue) query = query.Where(a => a.Meta.Draft == draft.Value);
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var needle = keyword.Trim();
            query = query.Where(a =>
                a.Title.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
                a.Slug.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
                a.MarkdownPath.Contains(needle, StringComparison.OrdinalIgnoreCase));
        }

        var matches = query.OrderByDescending(a => a.Meta.Date).ToList();
        var limit = Math.Clamp(McpJson.Int(args, "limit", 100), 1, 1000);
        var items = new JsonArray();
        foreach (var article in matches.Take(limit))
        {
            items.Add(new JsonObject
            {
                ["path"] = article.MarkdownPath,
                ["title"] = article.Title,
                ["slug"] = article.Slug,
                ["year"] = article.Year,
                ["month"] = article.Month,
                ["date"] = article.Meta.Date == default ? null : article.Meta.Date.ToString("yyyy-MM-dd"),
                ["categories"] = McpJson.Array(article.Meta.Categories),
                ["tags"] = McpJson.Array(article.Meta.Tags),
                ["draft"] = article.Meta.Draft,
                ["words"] = article.WordCount,
            });
        }

        var json = new JsonObject
        {
            ["total"] = matches.Count,
            ["returned"] = items.Count,
            ["items"] = items,
        };
        return Ok(json.ToJsonString(), "全库文章", $"返回 {items.Count}/{matches.Count} 篇");
    }

    private McpToolOutcome ArticleRead(JsonObject args)
    {
        var path = McpJson.Str(args, "path");
        var full = ResolveExisting(path, ".md");
        if (full == null) return Fail("文章不存在或路径越界。", path ?? "—");
        var relative = RelativePath(_options.RepoRoot, full) ?? path!;
        var raw = File.ReadAllText(full);
        YamlSidecar.TrySplitFrontMatter(raw, out _, out var body);
        var meta = YamlSidecar.LoadFromMarkdown(full);

        var json = new JsonObject
        {
            ["path"] = relative,
            ["meta"] = McpJson.Meta(meta),
            ["body"] = body,
            ["bytes"] = new FileInfo(full).Length,
            ["words"] = TextMetrics.CountWords(body),
        };
        return Ok(json.ToJsonString(), relative, $"{meta.Title} · {body.Length} 字符");
    }

    private McpToolOutcome ArticleSearch(JsonObject args)
    {
        var query = McpJson.Str(args, "query");
        if (string.IsNullOrWhiteSpace(query)) return Fail("缺少 query 参数。", "全文检索");
        var needle = query.Trim();
        var limit = Math.Clamp(McpJson.Int(args, "limit", 20), 1, 200);
        var hits = new List<JsonObject>();

        foreach (var article in Scan().Articles.OrderByDescending(a => a.Meta.Date))
        {
            var titleHit = article.Title.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
                           article.Slug.Contains(needle, StringComparison.OrdinalIgnoreCase);
            string body;
            try
            {
                body = File.ReadAllText(LinkStrategy.ToLocalFile(_options.RepoRoot, article.MarkdownPath));
            }
            catch
            {
                continue;
            }
            var bodyIndex = body.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
            if (!titleHit && bodyIndex < 0) continue;

            hits.Add(new JsonObject
            {
                ["path"] = article.MarkdownPath,
                ["title"] = article.Title,
                ["slug"] = article.Slug,
                ["where"] = titleHit ? "title" : "body",
                ["snippet"] = bodyIndex >= 0 ? Snippet(body, bodyIndex, needle.Length) : article.Title,
            });
            if (hits.Count >= limit) break;
        }

        var json = new JsonObject
        {
            ["query"] = needle,
            ["total"] = hits.Count,
            ["items"] = new JsonArray(hits.ToArray<JsonNode?>()),
        };
        return Ok(json.ToJsonString(), $"关键词「{needle}」", $"命中 {hits.Count} 篇");
    }

    private async Task<McpToolOutcome> ArticleWriteAsync(JsonObject args, string client, CancellationToken ct)
    {
        var path = McpJson.Str(args, "path");
        var full = ResolveExisting(path, ".md");
        if (full == null) return Fail("文章不存在或路径越界。", path ?? "—");
        var content = McpJson.Str(args, "content");
        if (content == null) return Fail("缺少 content 参数。", path!);

        var relative = RelativePath(_options.RepoRoot, full) ?? path!;
        var mode = (McpJson.Str(args, "mode") ?? "replace_all").Trim().ToLowerInvariant();
        var section = McpJson.Str(args, "section");
        var raw = File.ReadAllText(full);
        var (prefix, body) = SplitArticle(raw);

        string nextBody;
        string target;
        switch (mode)
        {
            case "replace_all":
                nextBody = Newline(content.TrimEnd()) + "\n";
                target = relative;
                break;
            case "append":
                nextBody = body.TrimEnd() + "\n\n" + Newline(content.Trim()) + "\n";
                target = $"{relative} · 追加文末";
                break;
            case "replace_section":
                if (string.IsNullOrWhiteSpace(section)) return Fail("replace_section 需要 section 参数。", relative);
                nextBody = ReplaceSection(body, section.Trim(), content);
                target = $"{relative} · §{section.Trim()}";
                break;
            default:
                return Fail($"未知写入方式：{mode}。", relative);
        }

        var detail = $"{mode} · {content.Length} 字符（原正文 {body.Length}）";
        if (!await ConfirmAsync("article_write", client, target, detail, Head(content), ct).ConfigureAwait(false))
        {
            return Deny("写操作未在 Vox 中确认。", target, detail);
        }

        File.WriteAllText(full, prefix + nextBody, new UTF8Encoding(false));
        Host?.NotifyContentChanged(relative);

        var json = new JsonObject
        {
            ["path"] = relative,
            ["mode"] = mode,
            ["chars"] = nextBody.Length,
            ["previousChars"] = body.Length,
        };
        return Ok(json.ToJsonString(), target, detail);
    }

    private async Task<McpToolOutcome> ArticleCreateAsync(JsonObject args, string client, CancellationToken ct)
    {
        var title = McpJson.Str(args, "title");
        if (string.IsNullOrWhiteSpace(title)) return Fail("缺少 title 参数。", "新建文章");

        var date = DateTime.Now;
        var rawDate = McpJson.Str(args, "date");
        if (!string.IsNullOrWhiteSpace(rawDate) &&
            !DateTime.TryParse(rawDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
        {
            return Fail("date 须为 yyyy-MM-dd 格式。", "新建文章");
        }

        var preview = ArticleScaffolder.Scaffold(new NewArticleSpec
        {
            Title = title.Trim(),
            Slug = McpJson.Str(args, "slug"),
            Date = date,
            Description = McpJson.Str(args, "description"),
            Draft = McpJson.Has(args, "draft") ? McpJson.Bool(args, "draft", true) : true,
        });
        var detail = $"新建文章 · {title.Trim()}";
        if (!await ConfirmAsync("article_create", client, preview.MarkdownPath, detail, Head(title.Trim()), ct)
                .ConfigureAwait(false))
        {
            return Deny("写操作未在 Vox 中确认。", preview.MarkdownPath, detail);
        }

        var spec = new NewArticleSpec
        {
            Title = title.Trim(),
            Slug = McpJson.Str(args, "slug"),
            Date = date,
            Description = McpJson.Str(args, "description"),
            Draft = McpJson.Has(args, "draft") ? McpJson.Bool(args, "draft", true) : true,
        };
        var created = ArticleScaffolder.Create(_options.RepoRoot, spec);
        var full = LinkStrategy.ToLocalFile(_options.RepoRoot, created.MarkdownPath);

        var categories = McpJson.Strings(args, "categories") ?? [];
        var tags = McpJson.Strings(args, "tags") ?? [];
        if (categories.Count > 0 || tags.Count > 0)
        {
            YamlSidecar.RewriteMarkdown(full, meta =>
            {
                if (categories.Count > 0) meta.Categories = categories.ToList();
                if (tags.Count > 0) meta.Tags = tags.ToList();
                return meta;
            });
        }

        var body = McpJson.Str(args, "body");
        if (!string.IsNullOrWhiteSpace(body))
        {
            var existing = File.ReadAllText(full);
            var (prefix, _) = SplitArticle(existing);
            var text = body.Trim();
            var markdown = text.StartsWith("# ", StringComparison.Ordinal)
                ? text + "\n"
                : $"# {title.Trim()}\n\n{text}\n";
            File.WriteAllText(full, prefix + markdown, new UTF8Encoding(false));
        }

        Host?.NotifyContentChanged(created.MarkdownPath);
        var json = new JsonObject
        {
            ["path"] = created.MarkdownPath,
            ["metaPath"] = created.MetaPath,
            ["title"] = title.Trim(),
        };
        return Ok(json.ToJsonString(), created.MarkdownPath, detail);
    }

    private async Task<McpToolOutcome> ArticleMetaAsync(JsonObject args, string client, CancellationToken ct)
    {
        var path = McpJson.Str(args, "path");
        var full = ResolveExisting(path, ".md");
        if (full == null) return Fail("文章不存在或路径越界。", path ?? "—");
        var relative = RelativePath(_options.RepoRoot, full) ?? path!;
        var action = (McpJson.Str(args, "action") ?? "get").Trim().ToLowerInvariant();

        if (action == "get")
        {
            var meta = YamlSidecar.LoadFromMarkdown(full);
            return Ok(McpJson.Meta(meta).ToJsonString(), relative, "读取元数据");
        }
        if (action != "set") return Fail($"未知 action：{action}。", relative);

        var changed = new List<string>();
        if (McpJson.Has(args, "title")) changed.Add("title");
        if (McpJson.Has(args, "description")) changed.Add("description");
        if (McpJson.Has(args, "cover")) changed.Add("cover");
        if (McpJson.Has(args, "draft")) changed.Add("draft");
        if (McpJson.Has(args, "categories")) changed.Add("categories");
        if (McpJson.Has(args, "tags")) changed.Add("tags");
        if (McpJson.Has(args, "albums")) changed.Add("albums");
        if (McpJson.Has(args, "author")) changed.Add("author");
        if (McpJson.Has(args, "date")) changed.Add("date");
        if (changed.Count == 0) return Fail("未提供要修改的元数据字段。", relative);

        var detail = $"元数据更新 · {string.Join(" / ", changed)}";
        if (!await ConfirmAsync("article_meta", client, relative, detail, null, ct).ConfigureAwait(false))
        {
            return Deny("写操作未在 Vox 中确认。", relative, detail);
        }

        YamlSidecar.RewriteMarkdown(full, meta =>
        {
            var title = McpJson.Str(args, "title");
            if (!string.IsNullOrWhiteSpace(title)) meta.Title = title.Trim();
            if (McpJson.Has(args, "description")) meta.Description = NullIfBlank(McpJson.Str(args, "description"));
            if (McpJson.Has(args, "cover")) meta.Cover = NullIfBlank(McpJson.Str(args, "cover"));
            if (McpJson.Has(args, "draft")) meta.Draft = McpJson.Bool(args, "draft", meta.Draft);
            if (McpJson.Has(args, "categories")) meta.Categories = (McpJson.Strings(args, "categories") ?? []).ToList();
            if (McpJson.Has(args, "tags")) meta.Tags = (McpJson.Strings(args, "tags") ?? []).ToList();
            if (McpJson.Has(args, "albums")) meta.Albums = (McpJson.Strings(args, "albums") ?? []).ToList();
            var author = McpJson.Str(args, "author");
            if (!string.IsNullOrWhiteSpace(author)) meta.Author = author.Trim();
            var date = McpJson.Str(args, "date");
            if (!string.IsNullOrWhiteSpace(date) &&
                DateTime.TryParse(date, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            {
                meta.Date = parsed;
            }
            return meta;
        });

        Host?.NotifyContentChanged(relative);
        var updated = YamlSidecar.LoadFromMarkdown(full);
        return Ok(McpJson.Meta(updated).ToJsonString(), relative, detail);
    }

    // ---------- 图片 ----------

    private McpToolOutcome ImageList(JsonObject args)
    {
        var scan = Scan();
        IEnumerable<ImageAsset> query = scan.Images;
        var year = McpJson.Str(args, "year");
        var keyword = McpJson.Str(args, "keyword");
        if (!string.IsNullOrWhiteSpace(year))
        {
            var prefix = year.Trim().TrimEnd('/') + "/";
            query = query.Where(i => i.RelativePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        }
        if (McpJson.Has(args, "orphan") && McpJson.Bool(args, "orphan")) query = query.Where(i => i.IsOrphan);
        if (McpJson.Has(args, "missing") && McpJson.Bool(args, "missing")) query = query.Where(i => i.IsMissing);
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var needle = keyword.Trim();
            query = query.Where(i => i.FileName.Contains(needle, StringComparison.OrdinalIgnoreCase));
        }

        var matches = query.OrderBy(i => i.RelativePath, StringComparer.OrdinalIgnoreCase).ToList();
        var limit = Math.Clamp(McpJson.Int(args, "limit", 200), 1, 1000);
        var items = new JsonArray();
        foreach (var image in matches.Take(limit))
        {
            items.Add(ImageJson(image, includeReferences: false));
        }

        var json = new JsonObject
        {
            ["total"] = matches.Count,
            ["returned"] = items.Count,
            ["items"] = items,
        };
        return Ok(json.ToJsonString(), "图片库", $"返回 {items.Count}/{matches.Count} 张");
    }

    private McpToolOutcome ImageUsage(JsonObject args)
    {
        var path = McpJson.Str(args, "path");
        if (string.IsNullOrWhiteSpace(path)) return Fail("缺少 path 参数。", "图片引用");
        var image = Scan().FindImage(path.Trim());
        if (image == null) return Fail("图片不在索引中。", path.Trim());
        return Ok(ImageJson(image, includeReferences: true).ToJsonString(), image.RelativePath,
            $"被 {image.ReferencedBy.Count} 处引用");
    }

    private async Task<McpToolOutcome> ImageImportAsync(JsonObject args, string client, CancellationToken ct)
    {
        var rawUrl = McpJson.Str(args, "url");
        if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return Fail("url 须为 http(s) 图片地址。", "图片入库");
        }

        var fileName = McpJson.Str(args, "filename");
        if (string.IsNullOrWhiteSpace(fileName)) fileName = Path.GetFileName(uri.AbsolutePath);
        fileName = SanitizeFileName(fileName);
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = Path.GetExtension(uri.AbsolutePath).ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(extension)) return Fail("无法推断图片扩展名，请传 filename。", "图片入库");
            fileName += extension;
        }
        if (!ImageExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            return Fail($"不支持的图片格式：{extension}。", "图片入库");
        }

        var year = McpJson.Str(args, "year") ?? DateTime.Now.Year.ToString("0000");
        var month = McpJson.Str(args, "month") ?? DateTime.Now.Month.ToString("00");
        if (!Regex.IsMatch(year, @"^\d{4}$") || !Regex.IsMatch(month, @"^\d{2}$"))
        {
            return Fail("year / month 格式无效（2026 / 10）。", "图片入库");
        }

        var relative = $"{year}/{month}/{fileName}";
        var full = LinkStrategy.ToLocalFile(_options.RepoRoot, relative);
        var index = 2;
        while (File.Exists(full))
        {
            var stem = Path.GetFileNameWithoutExtension(fileName);
            relative = $"{year}/{month}/{stem}-{index}{extension}";
            full = LinkStrategy.ToLocalFile(_options.RepoRoot, relative);
            index++;
        }

        var detail = $"网图下载 · {uri.Host}{uri.AbsolutePath}";
        if (!await ConfirmAsync("image_import", client, relative, detail, uri.ToString(), ct).ConfigureAwait(false))
        {
            return Deny("写操作未在 Vox 中确认。", relative, detail);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        long total = 0;
        try
        {
            using var response = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return Fail($"下载失败：HTTP {(int)response.StatusCode}。", relative);
            }
            var mediaType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
            if (mediaType.StartsWith("text/html", StringComparison.OrdinalIgnoreCase))
            {
                return Fail("目标地址返回网页而非图片。", relative);
            }
            await using var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using var target = new FileStream(full, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                81920, useAsync: true);
            var buffer = new byte[81920];
            int read;
            while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                total += read;
                if (total > MaxImageBytes)
                {
                    throw new InvalidOperationException("图片超过 30 MB 上限。");
                }
                await target.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            }
        }
        catch
        {
            if (File.Exists(full)) File.Delete(full);
            throw;
        }

        Host?.NotifyContentChanged(relative);
        var json = new JsonObject
        {
            ["path"] = relative,
            ["bytes"] = total,
            ["cdn"] = LinkStrategy.ToCdnUrl(relative, _options.CdnBase, _options.RepoRoot),
        };
        return Ok(json.ToJsonString(), relative, $"{total / 1024} KB · {detail}");
    }

    private async Task<McpToolOutcome> ImageReplaceAsync(JsonObject args, string client, CancellationToken ct)
    {
        var article = McpJson.Str(args, "article");
        var oldPath = McpJson.Str(args, "old");
        var newPath = McpJson.Str(args, "new");
        var full = ResolveExisting(article, ".md");
        if (full == null) return Fail("文章不存在或路径越界。", article ?? "—");
        if (string.IsNullOrWhiteSpace(oldPath) || string.IsNullOrWhiteSpace(newPath))
        {
            return Fail("缺少 old / new 图片路径。", article!);
        }
        var relative = RelativePath(_options.RepoRoot, full) ?? article!;
        var raw = File.ReadAllText(full);
        var oldCdn = LinkStrategy.ToCdnUrl(oldPath.Trim(), _options.CdnBase, _options.RepoRoot);
        var newCdn = LinkStrategy.ToCdnUrl(newPath.Trim(), _options.CdnBase, _options.RepoRoot);
        var count = CountOccurrences(raw, oldPath.Trim()) + CountOccurrences(raw, oldCdn);
        if (count == 0) return Fail("正文未出现旧图片引用。", relative);

        var detail = $"{oldPath.Trim()} → {newPath.Trim()} · {count} 处";
        if (!await ConfirmAsync("image_replace", client, relative, detail, null, ct).ConfigureAwait(false))
        {
            return Deny("写操作未在 Vox 中确认。", relative, detail);
        }

        var next = raw.Replace(oldPath.Trim(), newPath.Trim(), StringComparison.Ordinal)
            .Replace(oldCdn, newCdn, StringComparison.Ordinal);
        File.WriteAllText(full, next, new UTF8Encoding(false));
        Host?.NotifyContentChanged(relative);

        var json = new JsonObject { ["path"] = relative, ["replacements"] = count };
        return Ok(json.ToJsonString(), relative, detail);
    }

    // ---------- 发布 / git（宿主注入） ----------

    private async Task<McpToolOutcome> PublishStatusAsync(JsonObject args, CancellationToken ct)
    {
        if (Host == null) return Fail("发布状态服务未连接（App 未注入宿主）。", McpJson.Str(args, "article") ?? "全库");
        var article = McpJson.Str(args, "article");
        var result = await Host.PublishStatusAsync(article, ct).ConfigureAwait(false);
        if (result == null) return Fail("发布状态服务未连接（App 未注入宿主）。", article ?? "全库");
        return Ok(result, article ?? "全库", "发布状态矩阵");
    }

    private async Task<McpToolOutcome> PublishCheckAsync(JsonObject args, CancellationToken ct)
    {
        var article = McpJson.Str(args, "article");
        if (string.IsNullOrWhiteSpace(article) || ResolveExisting(article, ".md") == null)
        {
            return Fail("文章不存在或路径越界。", article ?? "发布检查");
        }
        if (Host == null) return Fail("发布检查未连接（App 未注入宿主）。", article);
        var result = await Host.PublishCheckAsync(article, ct).ConfigureAwait(false);
        if (result == null) return Fail("发布检查未连接（App 未注入宿主）。", article);
        return Ok(result, article, "发布前检查");
    }

    private async Task<McpToolOutcome> PublishArticleAsync(JsonObject args, string client, CancellationToken ct)
    {
        var article = McpJson.Str(args, "article");
        var platform = McpJson.Str(args, "platform")?.Trim().ToLowerInvariant();
        var allowed = new[] { "site", "wechat", "cnblogs", "csdn", "zhihu" };
        if (string.IsNullOrWhiteSpace(article) || ResolveExisting(article, ".md") == null)
        {
            return Fail("文章不存在或路径越界。", article ?? "发布");
        }
        if (platform == null || !allowed.Contains(platform))
        {
            return Fail("platform 须为 site / wechat / cnblogs / csdn / zhihu。", article);
        }

        var target = $"{article} · {platform}";
        if (!await ConfirmAsync("publish_article", client, target, "发布 / 更新到指定平台", null, ct)
                .ConfigureAwait(false))
        {
            return Deny("写操作未在 Vox 中确认。", target);
        }
        if (Host == null) return Fail("发布服务未连接（App 未注入宿主）。", target);
        var result = await Host.PublishArticleAsync(article, platform, ct).ConfigureAwait(false);
        if (result == null) return Fail("发布服务未连接（App 未注入宿主）。", target);
        return Ok(result, target, "发布完成");
    }

    private async Task<McpToolOutcome> GitStatusAsync(CancellationToken ct)
    {
        if (Host == null) return Fail("git 服务未连接（App 未注入宿主）。", "工作区");
        var result = await Host.GitStatusAsync(ct).ConfigureAwait(false);
        if (result == null) return Fail("git 服务未连接（App 未注入宿主）。", "工作区");
        return Ok(result, "工作区", "变更清单");
    }

    private async Task<McpToolOutcome> GitCommitAsync(JsonObject args, string client, CancellationToken ct)
    {
        var message = McpJson.Str(args, "message");
        if (string.IsNullOrWhiteSpace(message)) return Fail("缺少 message 参数。", "git commit");
        var paths = McpJson.Strings(args, "paths");
        var target = paths is { Count: > 0 } ? $"git commit · {paths.Count} 个路径" : "git commit · 全部变更";
        if (!await ConfirmAsync("git_commit", client, target, message.Trim(), null, ct).ConfigureAwait(false))
        {
            return Deny("写操作未在 Vox 中确认。", target);
        }
        if (Host == null) return Fail("git 服务未连接（App 未注入宿主）。", target);
        var result = await Host.GitCommitAsync(message.Trim(), paths, ct).ConfigureAwait(false);
        if (result == null) return Fail("git 服务未连接（App 未注入宿主）。", target);
        return Ok(result, target, message.Trim());
    }

    private async Task<McpToolOutcome> GitPushAsync(string client, CancellationToken ct)
    {
        const string target = "git push · origin";
        if (!await ConfirmAsync("git_push", client, target, "推送到远端", null, ct).ConfigureAwait(false))
        {
            return Deny("写操作未在 Vox 中确认。", target);
        }
        if (Host == null) return Fail("git 服务未连接（App 未注入宿主）。", target);
        var result = await Host.GitPushAsync(ct).ConfigureAwait(false);
        if (result == null) return Fail("git 服务未连接（App 未注入宿主）。", target);
        return Ok(result, target, "已推送");
    }

    // ---------- 系统 ----------

    private McpToolOutcome OutlineRead(JsonObject args)
    {
        var article = McpJson.Str(args, "article");
        var full = ResolveExisting(article, ".md");
        if (full == null) return Fail("文章不存在或路径越界。", article ?? "—");
        var relative = RelativePath(_options.RepoRoot, full) ?? article!;
        var raw = File.ReadAllText(full);
        YamlSidecar.TrySplitFrontMatter(raw, out _, out var body);
        var headings = new JsonArray();
        var inFence = false;
        var lines = body.Replace("\r\n", "\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal) ||
                line.TrimStart().StartsWith("~~~", StringComparison.Ordinal))
            {
                inFence = !inFence;
                continue;
            }
            if (inFence) continue;
            var match = Regex.Match(line, @"^(#{1,6})\s+(.+?)\s*#*\s*$");
            if (!match.Success) continue;
            headings.Add(new JsonObject
            {
                ["level"] = match.Groups[1].Value.Length,
                ["text"] = match.Groups[2].Value.Trim(),
                ["line"] = i + 1,
            });
        }
        var json = new JsonObject
        {
            ["article"] = relative,
            ["count"] = headings.Count,
            ["headings"] = headings,
        };
        return Ok(json.ToJsonString(), relative, $"{headings.Count} 个标题");
    }

    private McpToolOutcome StatsSummary()
    {
        var scan = Scan();
        var articles = scan.Articles;
        var yearMonths = articles
            .GroupBy(a => $"{a.Year}/{a.Month}")
            .OrderByDescending(g => g.Key, StringComparer.Ordinal)
            .Select(g => new JsonObject { ["yearMonth"] = g.Key, ["count"] = g.Count() });

        var json = new JsonObject
        {
            ["articles"] = articles.Count,
            ["drafts"] = articles.Count(a => a.Meta.Draft),
            ["words"] = articles.Sum(a => a.WordCount),
            ["cjk"] = articles.Sum(a => a.CjkCount),
            ["images"] = scan.Images.Count(i => i.Bytes >= 0),
            ["orphans"] = scan.Images.Count(i => i.IsOrphan),
            ["missing"] = scan.Images.Count(i => i.IsMissing),
            ["siteFiles"] = scan.SiteFiles.Count,
            ["yearMonths"] = new JsonArray(yearMonths.ToArray<JsonNode?>()),
        };
        return Ok(json.ToJsonString(), "全库", $"{articles.Count} 篇 · {json["images"]} 图");
    }

    // ---------- 公共辅助 ----------

    private RepoScan Scan() =>
        ScanProvider?.Invoke() ?? RepoScanner.Scan(_options.RepoRoot, new LinkOptions { CdnBase = _options.CdnBase });

    private string? ResolveExisting(string? relativePath, string extension)
    {
        var full = ResolveRepoPath(relativePath);
        if (full == null || !File.Exists(full)) return null;
        return full.EndsWith(extension, StringComparison.OrdinalIgnoreCase) ? full : null;
    }

    private async Task<bool> ConfirmAsync(
        string tool, string client, string target, string detail, string? preview, CancellationToken ct)
    {
        if (!_options.ConfirmWrites) return true;
        if (Host == null) return false;
        try
        {
            var granted = await Host.ConfirmWriteAsync(new McpWriteRequest(tool, client, target, detail, preview), ct)
                .ConfigureAwait(false);
            if (granted && CurrentCall.Value is { } call) call.Confirmed = true;
            return granted;
        }
        catch
        {
            return false;
        }
    }

    private static JsonObject ImageJson(ImageAsset image, bool includeReferences) => new()
    {
        ["path"] = image.RelativePath,
        ["bytes"] = image.Bytes,
        ["orphan"] = image.IsOrphan,
        ["missing"] = image.IsMissing,
        ["referencedBy"] = McpJson.Array(image.ReferencedBy),
        ["references"] = includeReferences ? image.ReferencedBy.Count : null,
    };

    private static (string Prefix, string Body) SplitArticle(string raw)
    {
        if (!YamlSidecar.TrySplitFrontMatter(raw, out _, out var body)) return (string.Empty, raw);
        return (raw[..(raw.Length - body.Length)], body);
    }

    private static string ReplaceSection(string body, string section, string content)
    {
        var normalized = body.Replace("\r\n", "\n");
        var lines = normalized.Split('\n');
        var wanted = section.Trim().TrimStart('#').Trim();
        var start = -1;
        var level = 0;
        for (var i = 0; i < lines.Length; i++)
        {
            var match = Regex.Match(lines[i], @"^(#{1,6})\s+(.+?)\s*#*\s*$");
            if (!match.Success) continue;
            if (!string.Equals(match.Groups[2].Value.Trim(), wanted, StringComparison.OrdinalIgnoreCase)) continue;
            start = i;
            level = match.Groups[1].Value.Length;
            break;
        }
        if (start < 0) throw new InvalidOperationException($"未找到章节「{section}」。");

        var end = lines.Length;
        for (var i = start + 1; i < lines.Length; i++)
        {
            var match = Regex.Match(lines[i], @"^(#{1,6})\s+(.+?)\s*#*\s*$");
            if (match.Success && match.Groups[1].Value.Length <= level)
            {
                end = i;
                break;
            }
        }

        var head = string.Join("\n", lines[..(start + 1)]);
        var tail = string.Join("\n", lines[end..]).TrimStart('\n');
        var middle = Newline(content.Trim()).Trim('\n');
        return tail.Length == 0
            ? head + "\n\n" + middle + "\n"
            : head + "\n\n" + middle + "\n\n" + tail + (body.EndsWith('\n') ? "\n" : string.Empty);
    }

    private static string Newline(string text) => text.Replace("\r\n", "\n");

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? Head(string text, int max = 600)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0) return null;
        return trimmed.Length <= max ? trimmed : trimmed[..max] + "\n…";
    }

    private static string SanitizeFileName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "image";
        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.Trim().Select(c => invalid.Contains(c) || c is '/' or '\\' ? '-' : c).ToArray();
        var clean = new string(chars).Trim('.', ' ');
        return string.IsNullOrWhiteSpace(clean) ? "image" : clean;
    }

    private static int CountOccurrences(string text, string needle) =>
        string.IsNullOrEmpty(needle) ? 0 : text.Split(needle, StringSplitOptions.None).Length - 1;

    private static string Snippet(string text, int index, int length)
    {
        var start = Math.Max(0, index - 60);
        var end = Math.Min(text.Length, index + length + 60);
        var snippet = text[start..end].Replace('\r', ' ').Replace('\n', ' ').Trim();
        return (start > 0 ? "…" : "") + snippet + (end < text.Length ? "…" : "");
    }

    private static McpToolOutcome Ok(string json, string target, string detail) => new(true, json, target, detail);

    private static McpToolOutcome Fail(string message, string target, string detail = "") =>
        new(false, "失败：" + message, target, string.IsNullOrWhiteSpace(detail) ? message : detail);

    private static McpToolOutcome Deny(string message, string target, string? detail = null) =>
        new(false, "已拒绝：" + message, target, detail ?? message, true);
}
