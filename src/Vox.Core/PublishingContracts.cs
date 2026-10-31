namespace Vox.Core;

/// <summary>发布执行方式。CopyAssistant 由本机渲染/剪贴板流程处理，不代表平台 API 已接入。</summary>
public enum PublishMode
{
    CopyAssistant,
    Api,
}

/// <summary>一次发布任务的生命周期。</summary>
public enum PublishTaskStatus
{
    Pending,
    Running,
    Succeeded,
    Failed,
    NeedsUpdate,
    Cancelled,
}

/// <summary>平台能力声明，供 UI 决定展示 API 发布还是复制助手。</summary>
public sealed record PublishPlatformCapabilities(
    PublishChannel Channel,
    string DisplayName,
    PublishMode Mode,
    bool SupportsUpdate,
    bool SupportsDelete = false,
    bool RequiresCover = false,
    bool RequiresDescription = false);

/// <summary>发布时使用的不可变内容快照，避免执行过程中编辑器内容变化。</summary>
public sealed record PublishContent(
    string ArticlePath,
    string Title,
    string Markdown,
    string? Description,
    string? Cover,
    string ContentHash);

/// <summary>发给单个平台适配器的请求。Credentials 由适配器通过本机凭据服务获取，不放入请求或日志。</summary>
public sealed record PublishRequest(
    PublishContent Content,
    PublishAction Action,
    string? RemoteArticleId = null);

/// <summary>平台适配器返回的统一结果。</summary>
public sealed record PublishResult(
    bool Success,
    string? RemoteArticleId = null,
    string? Url = null,
    string? Message = null,
    string? RemoteRevision = null)
{
    public static PublishResult Succeeded(string? remoteArticleId = null, string? url = null, string? message = null) =>
        new(true, remoteArticleId, url, message);

    public static PublishResult Failed(string message) =>
        new(false, Message: message);
}

/// <summary>每个平台实现一个适配器；凭据读取、HTTP 协议与平台 DTO 留在适配器内部。</summary>
public interface IPublishAdapter
{
    PublishPlatformCapabilities Capabilities { get; }

    Task<PublishResult> PublishAsync(PublishRequest request, CancellationToken cancellationToken = default);
}

/// <summary>可持久化的单篇 × 单平台任务快照；不包含凭据。</summary>
public sealed record PublishTaskItem(
    Guid Id,
    string ArticlePath,
    string Title,
    PublishChannel Channel,
    PublishMode Mode,
    PublishAction Action,
    PublishTaskStatus Status,
    string ContentHash,
    DateTime CreatedAt,
    DateTime? StartedAt = null,
    DateTime? FinishedAt = null,
    int Attempt = 0,
    string? RemoteArticleId = null,
    string? PublishedUrl = null,
    string? Error = null)
{
    public static PublishTaskItem Create(
        PublishContent content,
        PublishPlatformCapabilities platform,
        PublishAction action,
        DateTime? createdAt = null) =>
        new(Guid.NewGuid(), content.ArticlePath, content.Title, platform.Channel, platform.Mode,
            action, PublishTaskStatus.Pending, content.ContentHash, createdAt ?? DateTime.UtcNow);
}
