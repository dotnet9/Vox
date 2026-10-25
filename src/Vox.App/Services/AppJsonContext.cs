using System.Text.Json.Serialization;

namespace Vox.App.Services;

/// <summary>平台账户记录的 AOT 源生成上下文。</summary>
[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(WeChatAccount))]
[JsonSerializable(typeof(CnblogsAccount))]
[JsonSerializable(typeof(CsdnAccount))]
public sealed partial class AppJsonContext : JsonSerializerContext;
