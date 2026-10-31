using Vox.Core;
using Xunit;

public class PublishingContractsTests
{
    [Fact]
    public void Create_captures_article_platform_and_content_snapshot()
    {
        var content = new PublishContent(
            "2026/10/vox.md", "Vox 发布闭环", "# Vox", "摘要", "cover.png", "ABC123");
        var platform = new PublishPlatformCapabilities(
            PublishChannel.Csdn, "CSDN", PublishMode.Api, SupportsUpdate: true);

        var task = PublishTaskItem.Create(content, platform, PublishAction.First);

        Assert.NotEqual(Guid.Empty, task.Id);
        Assert.Equal("2026/10/vox.md", task.ArticlePath);
        Assert.Equal(PublishChannel.Csdn, task.Channel);
        Assert.Equal(PublishMode.Api, task.Mode);
        Assert.Equal(PublishTaskStatus.Pending, task.Status);
        Assert.Equal("ABC123", task.ContentHash);
        Assert.Equal(0, task.Attempt);
        Assert.Null(task.Error);
    }

    [Fact]
    public void Result_factories_preserve_success_and_failure_semantics()
    {
        var success = PublishResult.Succeeded("remote-1", "https://example.com/post/1");
        var failure = PublishResult.Failed("token expired");

        Assert.True(success.Success);
        Assert.Equal("remote-1", success.RemoteArticleId);
        Assert.Equal("https://example.com/post/1", success.Url);
        Assert.False(failure.Success);
        Assert.Equal("token expired", failure.Message);
    }
}
