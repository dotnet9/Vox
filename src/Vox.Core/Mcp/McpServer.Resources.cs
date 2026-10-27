using System.Text.Json.Nodes;

namespace Vox.Core.Mcp;

/// <summary>MCP Resources：文章以 md 原文暴露，图片暴露相对路径与 CDN 形态，站点数据 JSON 直出。</summary>
public sealed partial class McpServer
{
    private const string ArticlePrefix = "vox://article/";
    private const string ImagePrefix = "vox://image/";
    private const string SitePrefix = "vox://site/";

    private JsonObject ListResources()
    {
        var resources = new JsonArray();
        if (_options.Scope == McpAccessScope.Status)
        {
            return new JsonObject { ["resources"] = resources };
        }

        var scan = Scan();
        foreach (var article in scan.Articles.OrderByDescending(a => a.Meta.Date))
        {
            resources.Add(new JsonObject
            {
                ["uri"] = ArticlePrefix + EncodePath(article.MarkdownPath),
                ["name"] = $"article:{article.MarkdownPath}",
                ["title"] = article.Title,
                ["mimeType"] = "text/markdown",
                ["size"] = article.BodyBytes,
            });
        }

        if (_options.Scope == McpAccessScope.Repo)
        {
            foreach (var image in scan.Images.Where(i => i.Bytes >= 0).OrderBy(i => i.RelativePath, StringComparer.Ordinal))
            {
                resources.Add(new JsonObject
                {
                    ["uri"] = ImagePrefix + EncodePath(image.RelativePath),
                    ["name"] = $"image:{image.RelativePath}",
                    ["title"] = image.FileName,
                    ["mimeType"] = ImageMime(image.RelativePath),
                    ["size"] = image.Bytes,
                });
            }
            foreach (var site in scan.SiteFiles.OrderBy(s => s.RelativePath, StringComparer.Ordinal))
            {
                var sitePath = site.RelativePath.StartsWith("site/", StringComparison.OrdinalIgnoreCase)
                    ? site.RelativePath[5..]
                    : site.RelativePath;
                resources.Add(new JsonObject
                {
                    ["uri"] = SitePrefix + EncodePath(sitePath),
                    ["name"] = $"site:{sitePath}",
                    ["title"] = Path.GetFileName(site.RelativePath),
                    ["mimeType"] = "application/json",
                    ["size"] = site.RawJson.Length,
                });
            }
        }

        return new JsonObject
        {
            ["resources"] = resources,
            ["_meta"] = new JsonObject
            {
                ["articles"] = scan.Articles.Count,
                ["images"] = _options.Scope == McpAccessScope.Repo ? scan.Images.Count(i => i.Bytes >= 0) : 0,
                ["siteFiles"] = _options.Scope == McpAccessScope.Repo ? scan.SiteFiles.Count : 0,
            },
        };
    }

    private JsonObject ReadResource(JsonObject? parameters)
    {
        var uri = McpJson.Str(parameters, "uri");
        if (string.IsNullOrWhiteSpace(uri)) throw new RpcException(-32602, "缺少 uri 参数。");
        uri = uri.Trim();

        if (uri.StartsWith(ArticlePrefix, StringComparison.Ordinal))
        {
            if (_options.Scope == McpAccessScope.Status)
            {
                throw new RpcException(-32602, "当前授权范围仅状态查询，内容资源不可读。");
            }
            var path = DecodePath(uri[ArticlePrefix.Length..]);
            var full = ResolveExisting(path, ".md");
            if (full == null) throw new RpcException(-32602, $"资源不存在：{path}");
            return Contents(uri, "text/markdown", File.ReadAllText(full));
        }

        if (uri.StartsWith(ImagePrefix, StringComparison.Ordinal))
        {
            if (_options.Scope != McpAccessScope.Repo)
            {
                throw new RpcException(-32602, "当前授权范围不开放图片资源。");
            }
            var path = DecodePath(uri[ImagePrefix.Length..]);
            var image = Scan().FindImage(path);
            if (image == null) throw new RpcException(-32602, $"资源不存在：{path}");
            var payload = ImageJson(image, includeReferences: true);
            payload["cdn"] = LinkStrategy.ToCdnUrl(image.RelativePath, _options.CdnBase);
            return Contents(uri, "application/json", payload.ToJsonString());
        }

        if (uri.StartsWith(SitePrefix, StringComparison.Ordinal))
        {
            if (_options.Scope != McpAccessScope.Repo)
            {
                throw new RpcException(-32602, "当前授权范围不开放站点数据资源。");
            }
            var path = DecodePath(uri[SitePrefix.Length..]);
            var relativePath = path.StartsWith("site/", StringComparison.OrdinalIgnoreCase) ? path : "site/" + path;
            var full = ResolveExisting(relativePath, ".json");
            if (full == null) throw new RpcException(-32602, $"资源不存在：{path}");
            var relative = RelativePath(_options.RepoRoot, full) ?? relativePath;
            if (!relative.StartsWith("site/", StringComparison.OrdinalIgnoreCase))
            {
                throw new RpcException(-32602, "站点数据资源须位于 site/ 目录。");
            }
            return Contents(uri, "application/json", File.ReadAllText(full));
        }

        throw new RpcException(-32602, $"不支持的资源 URI：{uri}");
    }

    private static JsonObject Contents(string uri, string mimeType, string text) => new()
    {
        ["contents"] = new JsonArray(new JsonObject
        {
            ["uri"] = uri,
            ["mimeType"] = mimeType,
            ["text"] = text,
        }),
    };

    private static string ImageMime(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".svg" => "image/svg+xml",
        _ => "application/octet-stream",
    };

    private static string EncodePath(string path) =>
        string.Join("/", path.Split('/').Select(Uri.EscapeDataString));

    private static string DecodePath(string path) =>
        string.Join("/", path.Split('/').Select(Uri.UnescapeDataString));
}
