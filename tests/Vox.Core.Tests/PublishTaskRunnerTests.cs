using Vox.Core;
using Xunit;

public class PublishTaskRunnerTests
{
    private static (string State, string Log) TempPaths()
    {
        var dir = Directory.CreateTempSubdirectory("vox-runner");
        return (Path.Combine(dir.FullName, "state.json"), Path.Combine(dir.FullName, "log.jsonl"));
    }

    private static PublishContent Content() =>
        new("2026/10/a.md", "A", "# A", "summary", "cover.png", "HASH-A");

    private sealed class FakeAdapter(PublishResult result) : IPublishAdapter
    {
        public PublishPlatformCapabilities Capabilities { get; } =
            new(PublishChannel.Csdn, "CSDN", PublishMode.Api, SupportsUpdate: true);

        public int Calls { get; private set; }

        public Task<PublishResult> PublishAsync(PublishRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(result);
        }
    }

    [Fact]
    public async Task Success_updates_snapshot_and_appends_history()
    {
        var paths = TempPaths();
        var adapter = new FakeAdapter(PublishResult.Succeeded("remote-1", "https://example.com/a"));
        var runner = new PublishTaskRunner([adapter], statePath: paths.State, logPath: paths.Log);
        var task = PublishTaskItem.Create(Content(), adapter.Capabilities, PublishAction.First);

        var result = await runner.ExecuteAsync(task, Content());

        Assert.Equal(PublishTaskStatus.Succeeded, result.Status);
        Assert.Equal(1, result.Attempt);
        Assert.Equal("remote-1", result.RemoteArticleId);
        Assert.Equal(1, adapter.Calls);
        var states = PublishStateStore.Load(paths.State);
        Assert.Equal("HASH-A", states["2026/10/a.md"]["Csdn"].Hash);
        Assert.Single(PublishLogStore.List(paths.Log, success: true));
    }

    [Fact]
    public async Task Failure_marks_error_and_can_be_retried()
    {
        var paths = TempPaths();
        var adapter = new FakeAdapter(PublishResult.Failed("token expired"));
        var runner = new PublishTaskRunner([adapter], statePath: paths.State, logPath: paths.Log);
        var task = PublishTaskItem.Create(Content(), adapter.Capabilities, PublishAction.First);

        var failed = await runner.ExecuteAsync(task, Content());
        var retried = await runner.ExecuteAsync(failed with { Action = PublishAction.Retry }, Content());

        Assert.Equal(PublishTaskStatus.Failed, failed.Status);
        Assert.Equal(1, failed.Attempt);
        Assert.Equal(2, retried.Attempt);
        Assert.Contains("token expired", retried.Error);
        Assert.Equal(2, PublishLogStore.List(paths.Log, success: false).Count);
        Assert.Equal("token expired",
            PublishStateStore.Load(paths.State)["2026/10/a.md"]["Csdn"].Error);
    }

    [Fact]
    public async Task Changed_snapshot_is_rejected_before_adapter_call()
    {
        var paths = TempPaths();
        var adapter = new FakeAdapter(PublishResult.Succeeded());
        var runner = new PublishTaskRunner([adapter], statePath: paths.State, logPath: paths.Log);
        var task = PublishTaskItem.Create(Content(), adapter.Capabilities, PublishAction.First);
        var changed = Content() with { ContentHash = "HASH-B" };

        var result = await runner.ExecuteAsync(task, changed);

        Assert.Equal(PublishTaskStatus.Failed, result.Status);
        Assert.Equal(0, adapter.Calls);
        Assert.Contains("快照", result.Error);
    }
}
