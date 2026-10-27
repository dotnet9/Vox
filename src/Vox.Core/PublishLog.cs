using System.Text.Json;
using System.Text.Json.Serialization;

namespace Vox.Core;

/// <summary>发布动作：首发 / 更新（重发）/ 失败重试 / 复制助手。</summary>
public enum PublishAction
{
    First,
    Update,
    Retry,
    Copy,
}

/// <summary>一次发布留痕（发布历史表一行）。Platform 为 PublishChannel 名称。</summary>
public sealed class PublishLogEntry
{
    public DateTime Time { get; set; } = DateTime.Now;
    public string ArticlePath { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
    public PublishAction Action { get; set; }
    public bool Success { get; set; }
    public string? Note { get; set; }
    public string? Sha { get; set; }
    public string? Url { get; set; }
}

/// <summary>
/// 发布历史库：追加式 JSONL 存于 %AppData%/Vox/publish-log.jsonl（不污染内容仓库）。
/// 只追加、按时间倒序查询；文件超过阈值时保留最近若干行重写。
/// </summary>
public static class PublishLogStore
{
    private static readonly object Gate = new();
    private const long MaxBytes = 2 * 1024 * 1024;
    private const int TrimKeep = 3000;

    public static string DefaultPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Vox", "publish-log.jsonl");

    public static void Append(PublishLogEntry entry, string? path = null)
    {
        var file = path ?? DefaultPath();
        lock (Gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.AppendAllText(file,
                JsonSerializer.Serialize(entry, PublishLogJsonContext.Default.PublishLogEntry) + "\n");
            Trim(file);
        }
    }

    /// <summary>按时间倒序查询；platform 为通道名（PublishChannel.ToString()），success 为结果筛选。</summary>
    public static IReadOnlyList<PublishLogEntry> List(
        string? path = null, string? platform = null, bool? success = null, int limit = 300)
    {
        var file = path ?? DefaultPath();
        var entries = new List<PublishLogEntry>();
        lock (Gate)
        {
            if (!File.Exists(file)) return entries;
            foreach (var line in File.ReadAllLines(file))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                PublishLogEntry? entry;
                try
                {
                    entry = JsonSerializer.Deserialize(line, PublishLogJsonContext.Default.PublishLogEntry);
                }
                catch
                {
                    continue; // 损坏行跳过，不影响其余记录
                }
                if (entry == null) continue;
                if (platform != null && entry.Platform != platform) continue;
                if (success.HasValue && entry.Success != success.Value) continue;
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
[JsonSerializable(typeof(PublishLogEntry))]
internal sealed partial class PublishLogJsonContext : JsonSerializerContext;
