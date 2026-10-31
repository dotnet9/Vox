using System.Text.Json.Nodes;
using Vox.App.Infrastructure;
using System.Text;
using System.Xml.Linq;

namespace Vox.App.Services;

/// <summary>API 发布器统一口径：成功返回平台侧标识/链接，失败抛异常（消息直达发布对话框与状态库）。</summary>
public interface IPlatformPublisher
{
    /// <summary>发布（或写入草稿箱）。markdown 为全文、title 为标题、coverCdnUrl 为封面 CDN 地址（可空）。</summary>
    Task<string> PublishAsync(string title, string markdown, string? coverCdnUrl, CancellationToken ct = default);

    /// <summary>连通性检查（设置页「测试连接」/状态探测）。</summary>
    Task<bool> ProbeAsync(CancellationToken ct = default);
}

/// <summary>微信公众号：官方 API 仅写草稿箱（素材永久上传 thumb → draft/add），不群发。</summary>
public sealed class WeChatPublisher(WeChatAccount account) : IPlatformPublisher
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    public async Task<bool> ProbeAsync(CancellationToken ct = default)
    {
        try
        {
            var token = await GetAccessTokenAsync(ct);
            return token is not null;
        }
        catch
        {
            return false;
        }
    }

    public async Task<string> PublishAsync(string title, string markdown, string? coverCdnUrl, CancellationToken ct = default)
    {
        var token = await GetAccessTokenAsync(ct)
            ?? throw new InvalidOperationException("获取 access_token 失败：检查 AppID/AppSecret（IP 白名单需含本机出口 IP）");

        // 封面素材：公众号要求 thumb_media_id（封面图须上传为永久素材）。
        var thumbId = await UploadThumbAsync(token, coverCdnUrl, ct);
        if (thumbId is null)
        {
            throw new InvalidOperationException("封面上传失败：公众号草稿必须有封面（先在文章面板设置封面）");
        }

        // 正文：公众号不吃 Markdown，用复制导出器的内联样式 HTML。
        var html = CopyExporter.WeChatHtml(new Core.Article
        {
            MarkdownPath = "", MetaPath = "", Year = "", Month = "", Slug = "",
            Meta = new Core.ArticleMeta { Title = title, Slug = "draft", Date = DateTime.Now },
        }, markdown, AppState.Instance.Settings.Link);

        var payload = $$"""
        {
          "articles": [{
            "title": {{System.Text.Json.JsonSerializer.Serialize(title)}},
            "author": "Dotnet9",
            "content": {{System.Text.Json.JsonSerializer.Serialize(html)}},
            "thumb_media_id": {{System.Text.Json.JsonSerializer.Serialize(thumbId)}},
            "need_open_comment": 0,
            "only_fans_can_comment": 0
          }]
        }
        """;
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        var response = await Http.PostAsync($"https://api.weixin.qq.com/cgi-bin/draft/add?access_token={token}", content, ct);
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct));
        var mediaId = json?["media_id"]?.ToString();
        if (string.IsNullOrEmpty(mediaId))
        {
            throw new InvalidOperationException($"draft/add 失败：errcode={json?["errcode"]} {json?["errmsg"]}");
        }
        return $"草稿 media_id={mediaId}（请在公众号后台预览后群发）";
    }

    private async Task<string?> GetAccessTokenAsync(CancellationToken ct)
    {
        var url = $"https://api.weixin.qq.com/cgi-bin/token?grant_type=client_credential&appid={account.AppId}&secret={account.AppSecret}";
        var json = JsonNode.Parse(await Http.GetStringAsync(url, ct));
        return json?["access_token"]?.ToString() is { Length: > 0 } token ? token : null;
    }

    private async Task<string?> UploadThumbAsync(string token, string? coverUrl, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(coverUrl)) return null;
        using var image = await Http.GetStreamAsync(coverUrl, ct);
        using var form = new MultipartFormDataContent
        {
            { new StreamContent(image), "media", "cover.jpg" },
        };
        var response = await Http.PostAsync(
            $"https://api.weixin.qq.com/cgi-bin/material/add_material?access_token={token}&type=image", form, ct);
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct));
        return json?["media_id"]?.ToString();
    }

}

/// <summary>博客园 MetaWeblog（XML-RPC newPost；metaWeblog 风格博客的通用协议）。</summary>
public sealed class CnblogsPublisher(CnblogsAccount account) : IPlatformPublisher
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    public async Task<bool> ProbeAsync(CancellationToken ct = default)
    {
        try
        {
            var body = Rpc("blogger.getUsersBlogs", 
                RpcParam("string", "Vox"), RpcParam("string", ""), RpcParam("string", account.Token));
            using var content = new StringContent(body, Encoding.UTF8, "text/xml");
            var response = await Http.PostAsync(account.Url, content, ct);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<string> PublishAsync(string title, string markdown, string? coverCdnUrl, CancellationToken ct = default)
    {
        var description = $"<p><img src=\"{coverCdnUrl}\" alt=\"cover\"/></p>\n" + markdown;
        var body = Rpc("metaWeblog.newPost",
            RpcParam("string", "Vox-blog-id"), RpcParam("string", ""), RpcParam("string", account.Token),
            $"<struct>" +
            $"<member><name>title</name><value>{Escape(title)}</value></member>" +
            $"<member><name>description</name><value>{Escape(description)}</value></member>" +
            $"<member><name>categories</name><value><array><data></data></array></value></member>" +
            $"</struct>",
            RpcParam("boolean", "1"));

        using var content = new StringContent(body, Encoding.UTF8, "text/xml");
        var response = await Http.PostAsync(account.Url, content, ct);
        var xml = await response.Content.ReadAsStringAsync(ct);
        if (xml.Contains("<fault>", StringComparison.Ordinal))
        {
            var message = XDocument.Parse(xml).Descendants("value").LastOrDefault()?.Value ?? "未知错误";
            throw new InvalidOperationException($"MetaWeblog newPost 失败：{message}");
        }
        var postId = XDocument.Parse(xml).Descendants("string").FirstOrDefault()?.Value ?? "";
        return $"已发布（postid={postId}）";
    }

    private static string Rpc(string method, params string[] args) =>
        $"<?xml version=\"1.0\"?><methodCall><methodName>{method}</methodName><params>" +
        string.Join(string.Empty, args.Select(a => $"<param><value>{a}</value></param>")) +
        $"</params></methodCall>";

    private static string RpcParam(string type, string value) =>
        type == "boolean" ? $"<boolean>{value}</boolean>" : $"<{type}>{Escape(value)}</{type}>";

    private static string Escape(string text) =>
        text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}

/// <summary>知乎：无公开 API，走复制助手（发布对话框中由复制字段承载）。</summary>
public sealed class ZhihuCopyPublisher : IPlatformPublisher
{
    public Task<bool> ProbeAsync(CancellationToken ct = default) => Task.FromResult(true);

    public Task<string> PublishAsync(string title, string markdown, string? coverCdnUrl, CancellationToken ct = default) =>
        throw new InvalidOperationException("知乎无公开 API：请用「复制助手」（标题/描述/富文本已备好）");
}

/// <summary>
/// CSDN：使用编辑器内部 bizapi 接口发布。该接口并非稳定的公开 OpenAPI，
/// 需要本机登录 Cookie，并使用编辑器当前的 X-Ca HMAC 请求签名。
/// </summary>
public sealed class CsdnPublisher(CsdnAccount account) : IPlatformPublisher
{
    private const string ApiKey = "203803574";
    // CSDN Web 编辑器客户端签名密钥（公开前端协议常量，不是用户账户密码）。
    private const string ApiSecret = "9znpamsyl2c7cdrr9sas0le9vbc3r6ba";
    private const string SavePath = "/blog-console-api/v3/mdeditor/saveArticle";
    private const string ProbePath = "/blog-console-api/v3/user/isAuditStatus";
    private static readonly Uri ApiBase = new("https://bizapi.csdn.net");
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    public async Task<bool> ProbeAsync(CancellationToken ct = default)
    {
        try
        {
            using var request = CreateSignedRequest(HttpMethod.Get, ProbePath);
            using var response = await Http.SendAsync(request, ct);
            return response.IsSuccessStatusCode;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    public async Task<string> PublishAsync(string title, string markdown, string? coverCdnUrl, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(account.Token))
            throw new InvalidOperationException("未配置 CSDN 登录 Cookie，请在「设置 → 平台账户」中填写。");

        var payload = $$"""
        {
          "title": {{System.Text.Json.JsonSerializer.Serialize(title)}},
          "markdowncontent": {{System.Text.Json.JsonSerializer.Serialize(markdown)}},
          "content": {{System.Text.Json.JsonSerializer.Serialize(markdown)}},
          "readType": "public",
          "level": "0",
          "tags": "",
          "status": 0,
          "source": "pc_mdeditor",
          "cover": {{System.Text.Json.JsonSerializer.Serialize(coverCdnUrl ?? "")}}
        }
        """;
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var request = CreateSignedRequest(HttpMethod.Post, SavePath, content);
        using var response = await Http.SendAsync(request, ct);
        var text = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"CSDN 发布请求失败（HTTP {(int)response.StatusCode}）：{DescribeApiError(text)}");

        JsonNode? json;
        try
        {
            json = JsonNode.Parse(text);
        }
        catch
        {
            throw new InvalidOperationException("CSDN 返回了无法识别的响应，未确认文章是否发布。");
        }

        var codeText = json?["code"]?.ToString();
        if (!int.TryParse(codeText, out var code) || code is not (0 or 200))
            throw new InvalidOperationException($"CSDN 未确认发布成功：{DescribeApiError(text)}");

        var data = json?["data"];
        var articleId = data?["articleId"]?.ToString()
            ?? data?["article_id"]?.ToString()
            ?? data?["id"]?.ToString();
        return string.IsNullOrWhiteSpace(articleId)
            ? "CSDN 已接受发布请求（未返回文章 ID，请在个人后台确认）"
            : $"CSDN 已接受发布请求，文章 ID：{articleId}";
    }

    private HttpRequestMessage CreateSignedRequest(HttpMethod method, string path, HttpContent? content = null)
    {
        var nonce = Guid.NewGuid().ToString();
        var contentType = content?.Headers.ContentType?.MediaType ?? string.Empty;
        const string accept = "*/*";
        var canonicalHeaders = $"x-ca-key:{ApiKey}\nx-ca-nonce:{nonce}\n";
        var stringToSign = $"{method.Method}\n{accept}\n\n{contentType}\n\n{canonicalHeaders}{path}";
        using var hmac = new System.Security.Cryptography.HMACSHA256(Encoding.UTF8.GetBytes(ApiSecret));
        var signature = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(stringToSign)));

        var request = new HttpRequestMessage(method, new Uri(ApiBase, path)) { Content = content };
        request.Headers.TryAddWithoutValidation("Accept", accept);
        request.Headers.TryAddWithoutValidation("X-Ca-Key", ApiKey);
        request.Headers.TryAddWithoutValidation("X-Ca-Nonce", nonce);
        request.Headers.TryAddWithoutValidation("X-Ca-Signature-Headers", "x-ca-key,x-ca-nonce");
        request.Headers.TryAddWithoutValidation("X-Ca-Signature", signature);
        request.Headers.TryAddWithoutValidation("Cookie", account.Token);
        request.Headers.Referrer = new Uri("https://editor.csdn.net/md/");
        return request;
    }

    private static string DescribeApiError(string response)
    {
        try
        {
            var json = JsonNode.Parse(response);
            var message = json?["msg"]?.ToString()
                ?? json?["message"]?.ToString()
                ?? json?["error_msg"]?.ToString();
            if (!string.IsNullOrWhiteSpace(message)) return message;
        }
        catch
        {
            // 使用下面的安全兜底，不把整段响应（可能含个人信息）写入日志。
        }
        return "请检查登录 Cookie 是否过期、账号是否有发布权限；也可能是 CSDN 内部接口已变更。";
    }
}

public static class Publishers
{
    public static IPlatformPublisher? TryCreate(Core.PublishChannel channel) => channel switch
    {
        Core.PublishChannel.WeChat => PlatformAccounts.LoadWeChat() is { IsConfigured: true } account
            ? new WeChatPublisher(account)
            : null,
        Core.PublishChannel.Cnblogs => PlatformAccounts.LoadCnblogs() is { IsConfigured: true } account
            ? new CnblogsPublisher(account)
            : null,
        Core.PublishChannel.Csdn => PlatformAccounts.LoadCsdn() is { IsConfigured: true } account
            ? new CsdnPublisher(account)
            : null,
        Core.PublishChannel.Zhihu => new ZhihuCopyPublisher(),
        _ => null,
    };
}
