using System.Text.Json;
using System.Text.Json.Serialization;

namespace Vox.Core;

/// <summary>
/// 应用设置：仓库路径、链接策略（D6 修订：正文唯一规范形态 = 相对路径，CDN 域名只是可配置投影）、
/// 外观（语言 / 明暗 / 强调色 / 动效级别）。存 %AppData%/Vox/settings.json。
/// </summary>
public sealed class VoxSettings
{
    public string? RepoPath { get; set; }

    public string CdnBase { get; set; } = LinkStrategy.DefaultCdnBase;

    /// <summary>站点地址（发布对话框「查看线上」用）。</summary>
    public string SiteBase { get; set; } = "https://dotnet9.com";

    /// <summary>界面密度：compact / normal / roomy（字号 + 面板圆角三档）。</summary>
    public string Density { get; set; } = "normal";

    /// <summary>界面风格：Arc（柔和圆角）或 Pixel（直角像素风）。</summary>
    public string UiStyle { get; set; } = "Arc";

    /// <summary>界面语言（zh-CN / en-US）。</summary>
    public string Language { get; set; } = "zh-CN";

    /// <summary>null = 跟随系统。</summary>
    public bool? IsDark { get; set; }

    /// <summary>ArcAccentPreset 名称（Violet/Blue/Green/Orange 或任意 #RRGGBB）。</summary>
    public string Accent { get; set; } = "Violet";

    /// <summary>ArcMotionLevel 名称（Full/Reduced/Off）。</summary>
    public string MotionLevel { get; set; } = "Full";

    /// <summary>预览渲染密度基准字号（13 紧凑 / 15 正常 / 17 宽松）。</summary>
    public int MarkdownSize { get; set; } = 15;

    /// <summary>预览排版主题（CodeWF MarkdownTypographyThemes 键：Simple/Basic/InkBlack/TechnologyBlue/WeChatFormat）。</summary>
    public string MarkdownTheme { get; set; } = "Simple";

    /// <summary>脑图结构布局（与 Vex 的 MindMapLayout 设置键兼容）。</summary>
    public string MindMapLayout { get; set; } = "right";

    /// <summary>脑图节点样式（与 Vex 的 MindMapStyle 设置键兼容）。</summary>
    public string MindMapStyle { get; set; } = "quiet";

    /// <summary>脑图缩放百分比（与 Vex 的 MapZoomPercent 设置口径一致，1–200）。</summary>
    public double MapZoomPercent { get; set; } = 100;

    /// <summary>
    /// 自动保存（2026-10-27 需求）：开启时以磁盘为唯一真值——停止输入约 2 秒自动落盘，
    /// 外部程序修改当前文件静默自动载入（toast 告知），绝不弹「请先比较」；关闭时手动保存，
    /// 保存前检测外部修改并阻止覆盖。
    /// </summary>
    public bool AutoSave { get; set; }

    /// <summary>
    /// 大纲面板显示层级章节序号（2026-11-09 需求，设置键与 Vex 的 ShowOutlineNumbers 口径一致）：
    /// 默认不显示——大纲与正文标题原文一致，文章自带序号时不重复；未手写序号的文章可开。
    /// 序号由共享库 MarkdownOutlineNumbering 计算，此开关只控制显隐，写作页热应用。
    /// </summary>
    public bool ShowOutlineNumbers { get; set; }

    /// <summary>
    /// 文章列表视图（2026-11-09 需求）：true = 树形（默认），false = 列表；
    /// 切换即记忆，与 Vex 的 FileListView 设置口径一致。
    /// </summary>
    public bool ArticleTreeMode { get; set; } = true;

    /// <summary>
    /// 大纲折叠状态（2026-11-09 随大纲 UI 下沉引入，与 Vex 的 CollapsedOutlineSections 口径一致）：
    /// 转义独立行的键集合，键由共享库 OutlineExpansionKey 按文档路径 + 标题祖先链生成。
    /// </summary>
    public string? CollapsedOutlineSections { get; set; }

    /// <summary>
    /// 写作页右侧「大纲 | 元数据」面板显隐（2026-11-09 需求，设置键与 Vex 的 IsOutlineVisible 口径一致）：
    /// 默认展开；面板把手切换后写入配置，下次启动按上次状态打开。
    /// </summary>
    public bool IsOutlineVisible { get; set; } = true;

    /// <summary>写作页 AI 助手开关（右下角指令条显隐）。</summary>
    public bool LlmEnabled { get; set; } = true;

    /// <summary>AI 会话面板宽度（380–640，拖拽左缘调节；持久化避免每次重开面板再拖，2026-11-13）。</summary>
    public int AiPanelWidth { get; set; } = 420;

    /// <summary>大模型服务商标识（GLM / DeepSeek / OpenAI / Claude / Ollama），密钥按服务商存凭据管理器。</summary>
    public string LlmProvider { get; set; } = "GLM";

    /// <summary>模型名（随服务商切换写默认值，可手改）。</summary>
    public string LlmModel { get; set; } = "glm-5.3-flash";

    /// <summary>MCP 本机服务开关（仅监听 127.0.0.1，Bearer Token 鉴权）。</summary>
    public bool McpEnabled { get; set; }

    /// <summary>MCP 监听端口（1024–65535，重启生效）。</summary>
    public int McpPort { get; set; } = 17891;

    /// <summary>MCP 访问令牌（vox_ 前缀，至少 24 字符）。</summary>
    public string McpToken { get; set; } = string.Empty;

    /// <summary>授权范围：repo / articles / status。</summary>
    public string McpScope { get; set; } = "repo";

    /// <summary>写入工具调用前在应用内确认（关闭即直接执行，谨慎）。</summary>
    public bool McpConfirmWrites { get; set; } = true;

    /// <summary>允许 git 操作（开放 git_commit / git_push 两个写入工具）。</summary>
    public bool McpAllowGit { get; set; }

    /// <summary>被逐个下线的工具名（默认下线 image_replace，与原型一致）。</summary>
    public List<string> McpDisabledTools { get; set; } = ["image_replace"];

    /// <summary>提交并推送成功后，若有平台待发布/需更新则 toast 提示（平台账户弹层「发布默认项」）。</summary>
    public bool PublishAutoHintAfterPush { get; set; } = true;

    /// <summary>
    /// 用户模板目录（模板中心「我的模板」来源，2026-11-13 需求）：空 = %AppData%/Vox/templates。
    /// 目录里的 *.md（带 front matter）即被识别为模板；是否把目录纳入 git 等版本管理由用户自行决定，
    /// 程序不做任何 git 假设（打开的目录不一定是 git 仓库）。
    /// </summary>
    public string? TemplatesPath { get; set; }

    [JsonIgnore]
    public LinkOptions Link => new()
    {
        CdnBase = string.IsNullOrWhiteSpace(CdnBase) ? LinkStrategy.DefaultCdnBase : CdnBase,
        RepoRoot = RepoPath ?? string.Empty,
    };
}

public static class SettingsStore
{

    public static string DefaultPath()
    {
        if (AppContext.GetData("Vox.SettingsFile") is string { Length: > 0 } overridePath)
            return Path.GetFullPath(overridePath);
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Vox");
        return Path.Combine(dir, "settings.json");
    }

    public static VoxSettings Load(string? path = null)
    {
        path ??= DefaultPath();
        try
        {
            if (!File.Exists(path)) return new VoxSettings();
            return JsonSerializer.Deserialize(File.ReadAllText(path), VoxJsonContext.Default.VoxSettings) ?? new VoxSettings();
        }
        catch
        {
            return new VoxSettings();
        }
    }

    public static void Save(VoxSettings settings, string? path = null)
    {
        path ??= DefaultPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(settings, VoxJsonContext.Default.VoxSettings));
    }
}
