using System.Text.Json;

namespace Vox.Core;

/// <summary>AI 会话持久化的一条消息：display 为界面呈现文本（用户原始输入 / 回复正文），
/// content 为模型线完整内容（用户轮含上下文包装）——恢复时对话线与渲染线各取所需。</summary>
public sealed record AiPersistTurn(string Role, string Display, string Content);

/// <summary>
/// AI 会话历史持久化（2026-11-15）：应用重启后面板恢复最近对话，长会话不因退出丢失。
/// 存 appdata/Vox/ai-session.json，写入走临时文件+替换的原子路径；只保留最近 MaxTurns 条
/// 且首条对齐 user（截断到 assistant 开头的历史没有提问，恢复渲染与续聊都无意义）。
/// 读写全部容错：会话历史丢失不影响任何主功能。
/// </summary>
public static class AiSessionStore
{
    /// <summary>保留上限：40 条 ≈ 20 轮问答，文件体积与恢复渲染成本都有界。</summary>
    public const int MaxTurns = 40;

    public static string DefaultPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Vox", "ai-session.json");

    public static List<AiPersistTurn> Load()
    {
        try
        {
            if (!File.Exists(DefaultPath())) return [];
            return JsonSerializer.Deserialize(File.ReadAllText(DefaultPath()), VoxJsonContext.Default.ListAiPersistTurn)
                   ?? [];
        }
        catch
        {
            return [];
        }
    }

    public static void Save(IReadOnlyList<AiPersistTurn> turns)
    {
        if (turns.Count == 0)
        {
            Clear();
            return;
        }
        // 首条对齐 user：从后往前取 MaxTurns 条后，把开头连续的 assistant 丢掉。
        var tail = turns.Skip(Math.Max(0, turns.Count - MaxTurns)).ToList();
        while (tail.Count > 0 && tail[0].Role != "user") tail.RemoveAt(0);
        if (tail.Count == 0)
        {
            Clear();
            return;
        }
        try
        {
            var path = DefaultPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(tail, VoxJsonContext.Default.ListAiPersistTurn));
            File.Move(tmp, path, overwrite: true);
        }
        catch
        {
            // 存储失败静默：会话持久化是增强能力，不应打断对话流程。
        }
    }

    public static void Clear()
    {
        try
        {
            if (File.Exists(DefaultPath())) File.Delete(DefaultPath());
        }
        catch
        {
            // 同上：删除失败静默。
        }
    }
}
