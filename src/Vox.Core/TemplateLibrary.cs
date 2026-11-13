namespace Vox.Core;

/// <summary>
/// 模板目录装配：内置（Vox.Core 内嵌资源 Templates/*.md，显式阅读顺序）+ 用户目录（settings.TemplatesPath，
/// 空 = %AppData%/Vox/templates）合并。加载按文件粒度兜底：单个坏文件进 Errors 由 UI toast，不影响其余模板。
/// </summary>
public static class TemplateLibrary
{
    public const string ResourcePrefix = "Vox.Core.Templates.";

    /// <summary>内置模板阅读顺序（软件研发线 → 运维线）；未列出的新模板按 Id 排序追加，无需改此清单。</summary>
    public static readonly IReadOnlyList<string> BuiltinOrder =
    [
        "可行性分析报告", "项目开发计划", "软件需求规格说明", "软件设计说明",
        "测试计划", "测试报告", "项目开发总结报告",
        "运维服务方案", "事件故障报告", "变更记录", "巡检报告", "应急预案",
    ];

    /// <summary>用户模板目录默认值；支持环境数据键 Vox.TemplatesDir 会话级覆盖（测试/便携部署用）。</summary>
    public static string DefaultUserDirectory()
    {
        if (AppContext.GetData("Vox.TemplatesDir") is string { Length: > 0 } overrideDir)
            return Path.GetFullPath(overrideDir);
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Vox", "templates");
    }

    public static string UserDirectory(VoxSettings settings) =>
        string.IsNullOrWhiteSpace(settings.TemplatesPath) ? DefaultUserDirectory() : settings.TemplatesPath!;

    /// <summary>加载内置模板；排序 = BuiltinOrder 顺序，其余按 Id 序追加。</summary>
    public static IReadOnlyList<DocumentTemplate> LoadBuiltins()
    {
        var names = typeof(TemplateLibrary).Assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal) && n.EndsWith(".md", StringComparison.Ordinal))
            .ToList();
        var parsed = new List<DocumentTemplate>(names.Count);
        foreach (var name in names)
        {
            using var stream = typeof(TemplateLibrary).Assembly.GetManifestResourceStream(name);
            if (stream == null) continue;
            using var reader = new StreamReader(stream);
            var id = name[ResourcePrefix.Length..^3];
            parsed.Add(DocumentTemplate.Parse(id, reader.ReadToEnd(), isBuiltin: true));
        }
        return parsed.OrderBy(t => Rank(t.Id)).ThenBy(t => t.Id, StringComparer.Ordinal).ToList();
    }

    private static int Rank(string id)
    {
        for (var i = 0; i < BuiltinOrder.Count; i++)
        {
            if (BuiltinOrder[i] == id) return i;
        }
        return BuiltinOrder.Count;
    }

    /// <summary>扫描用户模板目录（顶层 *.md，不递归——模板没有子目录语义）；目录不存在视为空目录，不抛错。</summary>
    public static (IReadOnlyList<DocumentTemplate> Templates, List<string> Errors) LoadUserTemplates(string? directory)
    {
        var templates = new List<DocumentTemplate>();
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)) return (templates, errors);
        foreach (var file in Directory.EnumerateFiles(directory, "*.md").OrderBy(f => f, StringComparer.Ordinal))
        {
            try
            {
                templates.Add(DocumentTemplate.Parse(
                    Path.GetFileNameWithoutExtension(file), File.ReadAllText(file), isBuiltin: false));
            }
            catch (Exception ex) when (ex is FormatException or IOException)
            {
                errors.Add($"{Path.GetFileName(file)}：{ex.Message}");
            }
        }
        return (templates, errors);
    }

    /// <summary>合并视图：内置在前（BuiltinOrder 顺序），用户模板在后（按 Id 序）。</summary>
    public static IReadOnlyList<DocumentTemplate> LoadAll(VoxSettings settings)
    {
        var (user, _) = LoadUserTemplates(UserDirectory(settings));
        return LoadBuiltins().Concat(user).ToList();
    }
}
