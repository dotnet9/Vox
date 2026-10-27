using System.Text.Json;
using System.Text.Json.Serialization;

namespace Vox.Core;

/// <summary>
/// 工具层调用审计：内置 AI 改稿与 MCP 客户端读写共用同一份留痕
/// （Client 区分「内置 AI」与外部客户端）。与内容体检 AuditEngine 无关，单独存放。
/// </summary>
public sealed class ToolAuditEntry
{
    public DateTime Time { get; set; } = DateTime.Now;
    public string Client { get; set; } = string.Empty;
    public string Tool { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;

    /// <summary>ok / okc（已确认写入）/ deny / error。</summary>
    public string Result { get; set; } = "ok";

    public string? Detail { get; set; }
    public int DurationMs { get; set; }
}

/// <summary>
/// 调用审计库：追加式 JSONL 存于 %AppData%/Vox/tool-audit.jsonl，不污染内容仓库；
/// 查询按时间倒序并可按客户端 / 工具 / 结果筛选，超限保留最近若干行。
/// </summary>
public static class ToolAuditStore
{
    private static readonly object Gate = new();
    private const long MaxBytes = 2 * 1024 * 1024;
    private const int TrimKeep = 3000;

    public static string DefaultPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Vox", "tool-audit.jsonl");

    public static void Append(ToolAuditEntry entry, string? path = null)
    {
        var file = path ?? DefaultPath();
        lock (Gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.AppendAllText(file,
                JsonSerializer.Serialize(entry, ToolAuditJsonContext.Default.ToolAuditEntry) + "\n");
            Trim(file);
        }
    }

    public static IReadOnlyList<ToolAuditEntry> List(
        string? path = null, string? client = null, string? tool = null, string? result = null, int limit = 300)
    {
        var file = path ?? DefaultPath();
        var entries = new List<ToolAuditEntry>();
        lock (Gate)
        {
            if (!File.Exists(file)) return entries;
            foreach (var line in File.ReadAllLines(file))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                ToolAuditEntry? entry;
                try
                {
                    entry = JsonSerializer.Deserialize(line, ToolAuditJsonContext.Default.ToolAuditEntry);
                }
                catch
                {
                    continue;
                }
                if (entry == null) continue;
                if (client != null && entry.Client != client) continue;
                if (tool != null && entry.Tool != tool) continue;
                if (result != null && entry.Result != result) continue;
                entries.Add(entry);
            }
        }
        entries.Sort((a, b) => b.Time.CompareTo(a.Time));
        if (entries.Count > limit) entries.RemoveRange(limit, entries.Count - limit);
        return entries;
    }

    public static void Clear(string? path = null)
    {
        var file = path ?? DefaultPath();
        lock (Gate)
        {
            if (File.Exists(file)) File.Delete(file);
        }
    }

    private static void Trim(string file)
    {
        if (new FileInfo(file).Length <= MaxBytes) return;
        var lines = File.ReadAllLines(file);
        File.WriteAllLines(file, lines[^Math.Min(lines.Length, TrimKeep)..]);
    }
}

[JsonSourceGenerationOptions(WriteIndented = false, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ToolAuditEntry))]
internal sealed partial class ToolAuditJsonContext : JsonSerializerContext;
