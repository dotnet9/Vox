namespace Vox.Core.Mcp;

/// <summary>一个 MCP 工具的定义：分组、说明、权限与 JSON Schema。</summary>
public sealed record McpToolDescriptor(
    string Name,
    string Group,
    string Description,
    bool ReadOnly,
    bool RequiresGit,
    string SchemaJson);

public sealed record McpPromptDescriptor(string Name, string Title, string Description, string[] Arguments);

/// <summary>18 个工具五组（文章 / 图片 / 发布 / git / 系统）与 4 条预设指令。</summary>
public static class McpCatalog
{
    public static readonly IReadOnlyList<McpToolDescriptor> Tools =
    [
        new("article_list", "article", "列出文章（年月 / 分类 / 草稿筛选）", true, false,
            """
            {"type":"object","properties":{
              "year":{"type":"string","description":"年份，如 2026"},
              "month":{"type":"string","description":"月份，如 10"},
              "category":{"type":"string","description":"分类名（包含匹配）"},
              "draft":{"type":"boolean","description":"true 仅草稿 / false 仅已发布"},
              "keyword":{"type":"string","description":"标题 / slug / 路径关键词"},
              "limit":{"type":"integer","description":"返回上限，默认 100"}}}
            """),
        new("article_read", "article", "读取正文与 front matter 元数据", true, false,
            """
            {"type":"object","properties":{
              "path":{"type":"string","description":"文章仓库相对路径，如 2026/10/slug.md"}},
             "required":["path"]}
            """),
        new("article_search", "article", "全文检索（标题 / slug / 正文）", true, false,
            """
            {"type":"object","properties":{
              "query":{"type":"string","description":"检索关键词"},
              "limit":{"type":"integer","description":"返回上限，默认 20"}},
             "required":["query"]}
            """),
        new("article_write", "article", "写回正文，可指定章节范围（附 diff 预览）", false, false,
            """
            {"type":"object","properties":{
              "path":{"type":"string","description":"文章仓库相对路径"},
              "mode":{"type":"string","enum":["replace_all","replace_section","append"],"description":"写入方式，默认 replace_all"},
              "content":{"type":"string","description":"新正文内容"},
              "section":{"type":"string","description":"replace_section 时的章节标题文本"}},
             "required":["path","content"]}
            """),
        new("article_create", "article", "新建文章，自动生成 slug 与同名 yml", false, false,
            """
            {"type":"object","properties":{
              "title":{"type":"string","description":"文章标题"},
              "slug":{"type":"string","description":"slug，缺省由标题生成"},
              "date":{"type":"string","description":"日期 yyyy-MM-dd，缺省今天"},
              "description":{"type":"string","description":"摘要"},
              "categories":{"type":"array","items":{"type":"string"}},
              "tags":{"type":"array","items":{"type":"string"}},
              "draft":{"type":"boolean","description":"是否草稿，默认 true"},
              "body":{"type":"string","description":"正文 markdown（缺省生成骨架）"}},
             "required":["title"]}
            """),
        new("article_meta", "article", "读写元数据（分类 / 标签 / 封面 / 草稿位）", false, false,
            """
            {"type":"object","properties":{
              "path":{"type":"string","description":"文章仓库相对路径"},
              "action":{"type":"string","enum":["get","set"],"description":"默认 get"},
              "title":{"type":"string"},
              "description":{"type":"string","description":"传空字符串可清空"},
              "cover":{"type":"string","description":"封面仓库相对路径或 CDN URL"},
              "draft":{"type":"boolean"},
              "categories":{"type":"array","items":{"type":"string"}},
              "tags":{"type":"array","items":{"type":"string"}},
              "albums":{"type":"array","items":{"type":"string"}},
              "author":{"type":"string"},
              "date":{"type":"string","description":"yyyy-MM-dd HH:mm:ss"}},
             "required":["path"]}
            """),
        new("image_list", "image", "图片库清单（年月 / 被引用 / 孤儿筛选）", true, false,
            """
            {"type":"object","properties":{
              "year":{"type":"string","description":"年月前缀，如 2026/09"},
              "orphan":{"type":"boolean","description":"true 仅孤儿图"},
              "missing":{"type":"boolean","description":"true 仅死链引用"},
              "keyword":{"type":"string","description":"文件名关键词"},
              "limit":{"type":"integer","description":"返回上限，默认 200"}}}
            """),
        new("image_usage", "image", "查询图片被哪些文章引用", true, false,
            """
            {"type":"object","properties":{
              "path":{"type":"string","description":"图片仓库相对路径"}},
             "required":["path"]}
            """),
        new("image_import", "image", "入库新图：网图下载 · 落位年月目录", false, false,
            """
            {"type":"object","properties":{
              "url":{"type":"string","description":"http(s) 图片地址"},
              "filename":{"type":"string","description":"目标文件名（含扩展名），缺省自动推断"},
              "year":{"type":"string","description":"落位年份，缺省当前"},
              "month":{"type":"string","description":"落位月份，缺省当前"}},
             "required":["url"]}
            """),
        new("image_replace", "image", "替换正文图片引用，旧档保留可回滚", false, false,
            """
            {"type":"object","properties":{
              "article":{"type":"string","description":"文章仓库相对路径"},
              "old":{"type":"string","description":"旧图片仓库相对路径"},
              "new":{"type":"string","description":"新图片仓库相对路径"}},
             "required":["article","old","new"]}
            """),
        new("publish_status", "publish", "各平台发布状态矩阵（含需更新原因）", true, false,
            """
            {"type":"object","properties":{
              "article":{"type":"string","description":"文章仓库相对路径；缺省返回全库"}}}
            """),
        new("publish_check", "publish", "运行发布前检查并取回结果", true, false,
            """
            {"type":"object","properties":{
              "article":{"type":"string","description":"文章仓库相对路径"}},
             "required":["article"]}
            """),
        new("publish_article", "publish", "发布 / 更新到指定平台（公众号仅草稿箱）", false, false,
            """
            {"type":"object","properties":{
              "article":{"type":"string","description":"文章仓库相对路径"},
              "platform":{"type":"string","enum":["site","wechat","cnblogs","csdn","zhihu"],"description":"目标平台"}},
             "required":["article","platform"]}
            """),
        new("git_status", "git", "变更清单 · 分支 · ahead/behind", true, false,
            """
            {"type":"object","properties":{}}
            """),
        new("git_commit", "git", "提交勾选的变更（复用自动 Summary）", false, true,
            """
            {"type":"object","properties":{
              "message":{"type":"string","description":"提交信息"},
              "paths":{"type":"array","items":{"type":"string"},"description":"勾选路径；缺省提交全部变更"}},
             "required":["message"]}
            """),
        new("git_push", "git", "推送到远端，网站随之更新", false, true,
            """
            {"type":"object","properties":{}}
            """),
        new("outline_read", "system", "读取大纲结构（导图数据）", true, false,
            """
            {"type":"object","properties":{
              "article":{"type":"string","description":"文章仓库相对路径"}},
             "required":["article"]}
            """),
        new("stats_summary", "system", "文章库统计（篇数 / 字数 / 图片 / 待发布）", true, false,
            """
            {"type":"object","properties":{}}
            """),
    ];

    public static readonly IReadOnlyList<McpPromptDescriptor> Prompts =
    [
        new("wechat-rewrite", "公众号改写", "把文章改写为公众号风格，保留事实与代码块", ["article", "instruction"]),
        new("summary-description", "摘要 description", "生成 80 字以内、可直接写入 front matter 的摘要", ["article"]),
        new("translate-en", "翻译英文", "把中文正文翻译为自然英文，保留 Markdown 结构", ["article"]),
        new("proofread", "正文校对", "校对错别字、标点与病句，逐条列出问题", ["article"]),
    ];

    public static McpToolDescriptor? Find(string name) =>
        Tools.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>工具对客户端是否可见：下线、git 总开关、授权范围三重过滤。</summary>
    public static bool IsVisible(McpToolDescriptor tool, McpOptions options)
    {
        if (options.IsToolDisabled(tool.Name)) return false;
        if (tool.RequiresGit && !options.AllowGit) return false;
        return options.Scope switch
        {
            McpAccessScope.Status => tool.ReadOnly,
            McpAccessScope.Articles => tool.Group is not ("image" or "git"),
            _ => true,
        };
    }

    public static IReadOnlyList<McpToolDescriptor> VisibleTools(McpOptions options) =>
        Tools.Where(t => IsVisible(t, options)).ToList();
}
