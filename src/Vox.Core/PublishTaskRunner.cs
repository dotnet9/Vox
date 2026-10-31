namespace Vox.Core;

/// <summary>可替换的单篇发布预检；返回的问题列表为空表示通过。</summary>
public interface IPublishPreflight
{
    Task<IReadOnlyList<string>> ValidateAsync(
        PublishContent content,
        PublishPlatformCapabilities platform,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 单任务发布执行器：校验快照与平台能力、执行适配器、更新既有状态库并追加发布历史。
/// 不负责调度队列；UI/后台服务可按任务逐个调用。
/// </summary>
public sealed class PublishTaskRunner
{
    private readonly IReadOnlyDictionary<PublishChannel, IPublishAdapter> _adapters;
    private readonly IPublishPreflight? _preflight;
    private readonly string? _statePath;
    private readonly string? _logPath;

    public PublishTaskRunner(
        IEnumerable<IPublishAdapter> adapters,
        IPublishPreflight? preflight = null,
        string? statePath = null,
        string? logPath = null)
    {
        _adapters = adapters.ToDictionary(x => x.Capabilities.Channel);
        _preflight = preflight;
        _statePath = statePath;
        _logPath = logPath;
    }

    public async Task<PublishTaskItem> ExecuteAsync(
        PublishTaskItem task,
        PublishContent content,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(content);

        if (task.Status is not (PublishTaskStatus.Pending or PublishTaskStatus.Failed))
            throw new InvalidOperationException($"任务状态 {task.Status} 不允许执行。");

        if (!string.Equals(task.ArticlePath, content.ArticlePath, StringComparison.Ordinal) ||
            !string.Equals(task.ContentHash, content.ContentHash, StringComparison.Ordinal))
            return Finish(task, PublishTaskStatus.Failed, "发布内容与任务创建时的快照不一致。", null);

        if (!_adapters.TryGetValue(task.Channel, out var adapter))
            return Finish(task, PublishTaskStatus.Failed, $"平台 {task.Channel} 尚未注册发布适配器。", null);

        var capabilities = adapter.Capabilities;
        if (task.Mode != capabilities.Mode)
            return Finish(task, PublishTaskStatus.Failed, "任务发布模式与平台适配器能力不一致。", null);
        if (task.Mode == PublishMode.CopyAssistant)
            return Finish(task, PublishTaskStatus.Failed, "复制助手任务应由 UI 复制流程处理，不能作为 API 任务执行。", null);
        if (task.Action == PublishAction.Update && !capabilities.SupportsUpdate)
            return Finish(task, PublishTaskStatus.Failed, $"{capabilities.DisplayName} 不支持更新操作。", null);

        if (capabilities.RequiresCover && string.IsNullOrWhiteSpace(content.Cover))
            return Finish(task, PublishTaskStatus.Failed, "缺少平台要求的封面。", null);
        if (capabilities.RequiresDescription && string.IsNullOrWhiteSpace(content.Description))
            return Finish(task, PublishTaskStatus.Failed, "缺少平台要求的摘要。", null);

        try
        {
            if (_preflight is not null)
            {
                var issues = await _preflight.ValidateAsync(content, capabilities, cancellationToken);
                if (issues.Count > 0)
                    return Finish(task, PublishTaskStatus.Failed, string.Join("；", issues), null);
            }

            cancellationToken.ThrowIfCancellationRequested();
            var running = task with
            {
                Status = PublishTaskStatus.Running,
                StartedAt = DateTime.UtcNow,
                FinishedAt = null,
                Attempt = task.Attempt + 1,
                Error = null,
            };

            var result = await adapter.PublishAsync(
                new PublishRequest(content, task.Action, task.RemoteArticleId), cancellationToken);

            if (result.Success)
            {
                var completed = running with
                {
                    Status = PublishTaskStatus.Succeeded,
                    FinishedAt = DateTime.UtcNow,
                    RemoteArticleId = result.RemoteArticleId ?? task.RemoteArticleId,
                    PublishedUrl = result.Url ?? task.PublishedUrl,
                    Error = null,
                };
                var states = PublishStateStore.Load(_statePath);
                PublishStateStore.MarkPublished(states, task.ArticlePath, task.Channel, task.ContentHash, result.Message);
                PublishStateStore.Save(states, _statePath);
                AppendLog(completed, true, result.Message, result.Url);
                return completed;
            }

            return Finish(running, PublishTaskStatus.Failed, result.Message ?? "平台返回未知错误。", result.Url, recordPlatformError: true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Finish(task, PublishTaskStatus.Cancelled, "发布任务已取消。", null);
        }
        catch (Exception ex)
        {
            return Finish(task, PublishTaskStatus.Failed, ex.Message, null);
        }
    }

    private PublishTaskItem Finish(
        PublishTaskItem task,
        PublishTaskStatus status,
        string error,
        string? url,
        bool recordPlatformError = false)
    {
        var finished = task with
        {
            Status = status,
            FinishedAt = DateTime.UtcNow,
            PublishedUrl = url ?? task.PublishedUrl,
            Error = error,
        };

        if (recordPlatformError)
        {
            var states = PublishStateStore.Load(_statePath);
            PublishStateStore.MarkError(states, task.ArticlePath, task.Channel, error);
            PublishStateStore.Save(states, _statePath);
        }

        AppendLog(finished, false, error, url);
        return finished;
    }

    private void AppendLog(PublishTaskItem task, bool success, string? note, string? url) =>
        PublishLogStore.Append(new PublishLogEntry
        {
            Time = DateTime.Now,
            ArticlePath = task.ArticlePath,
            Title = task.Title,
            Platform = task.Channel.ToString(),
            Action = task.Action,
            Success = success,
            Note = note,
            Sha = task.ContentHash,
            Url = url,
        }, _logPath);
}
