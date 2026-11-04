using YamlDotNet.RepresentationModel;

namespace Vox.Core;

/// <summary>文章 sidecar 元数据（slug.yml 的中立映射，详见 docs/架构.md）。</summary>
public sealed class ArticleMeta
{
    public required string Title { get; set; }
    public required string Slug { get; set; }
    public string? Description { get; set; }
    public DateTime Date { get; set; }
    public DateTime? LastMod { get; set; }
    public string? Cover { get; set; }
    public List<string> Categories { get; set; } = [];
    public List<string> Albums { get; set; } = []; // 已废弃：面板/弹层不再编辑，仅存量数据与 MCP 通道保留
    public List<string> Tags { get; set; } = [];
    public string Author { get; set; } = string.Empty; // 缺 author 是体检项，不默认填充
    public bool Draft { get; set; }
    public bool? Banner { get; set; }      // 站点首页横幅展示位：null = 老文章无此字段（写回时省略）
    public string? Copyright { get; set; } // 版权声明：留空 = 原创（历史文章可能带 copyright: Original，需往返保留）

    /// <summary>front matter 未知字段（自定义键）：按文件出现顺序保存，往返保真；面板只编辑标量，复杂结构经 YAML 态编辑。</summary>
    public List<MetaExtraField> Extra { get; set; } = [];
}

/// <summary>front matter 未知字段：原样持有 YamlDotNet 节点（含原引号风格），回写时不丢键、不改值形态。</summary>
public sealed record MetaExtraField(string Key, YamlNode? Node)
{
    /// <summary>标量值文本（已去引号），面板键值对行直接编辑的就是它。</summary>
    public string? ScalarText => (Node as YamlScalarNode)?.Value;

    /// <summary>是否可在面板行内编辑（纯标量）。</summary>
    public bool IsScalar => Node is YamlScalarNode;

    /// <summary>是否为全标量序列（如 albums 形态的列表）：面板只读展示摘要，编辑走 YAML 态。</summary>
    public bool IsScalarSequence => Node is YamlSequenceNode { } seq
        && seq.Children.Count > 0 && seq.Children.All(c => c is YamlScalarNode);

    /// <summary>全标量序列的展示摘要（逗号连接）。</summary>
    public string? SequenceSummary => IsScalarSequence
        ? string.Join(", ", ((YamlSequenceNode)Node!).Children.Select(c => ((YamlScalarNode)c).Value))
        : null;

    public static MetaExtraField Scalar(string key, string value) => new(key, new YamlScalarNode(value));
}
