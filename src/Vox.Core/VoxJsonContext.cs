using System.Text.Json.Serialization;

namespace Vox.Core;

/// <summary>
/// NativeAOT 兼容的 JSON 序列化上下文：设置 / 发布状态库全部走源生成（反射序列化在 AOT 下缺元数据）。
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(VoxSettings))]
[JsonSerializable(typeof(Dictionary<string, Dictionary<string, ChannelSnapshot>>))]
public sealed partial class VoxJsonContext : JsonSerializerContext;
