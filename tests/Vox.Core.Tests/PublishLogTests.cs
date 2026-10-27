using Vox.Core;
using Xunit;

public class PublishLogTests
{
    private static string TempFile() =>
        Path.Combine(Directory.CreateTempSubdirectory("vox-publog").FullName, "publish-log.jsonl");

    [Fact]
    public void Append_and_list_returns_newest_first()
    {
        var file = TempFile();
        PublishLogStore.Append(new PublishLogEntry
        {
            Time = new DateTime(2026, 10, 26, 20, 31, 0),
            ArticlePath = "2026/10/a.md", Title = "A", Platform = "Site",
            Action = PublishAction.First, Success = true, Sha = "c87de8d",
        }, file);
        PublishLogStore.Append(new PublishLogEntry
        {
            Time = new DateTime(2026, 10, 27, 9, 0, 0),
            ArticlePath = "2026/10/b.md", Title = "B", Platform = "Csdn",
            Action = PublishAction.Retry, Success = false, Note = "token 过期",
        }, file);

        var all = PublishLogStore.List(file);
        Assert.Equal(2, all.Count);
        Assert.Equal("B", all[0].Title);
        Assert.False(all[0].Success);
        Assert.Equal("token 过期", all[0].Note);
        Assert.Equal(PublishAction.Retry, all[0].Action);
    }

    [Fact]
    public void List_filters_by_platform_and_result()
    {
        var file = TempFile();
        PublishLogStore.Append(new PublishLogEntry
        {
            ArticlePath = "a.md", Title = "A", Platform = "Site", Action = PublishAction.First, Success = true,
        }, file);
        PublishLogStore.Append(new PublishLogEntry
        {
            ArticlePath = "a.md", Title = "A", Platform = "Csdn", Action = PublishAction.First, Success = false,
        }, file);
        PublishLogStore.Append(new PublishLogEntry
        {
            ArticlePath = "b.md", Title = "B", Platform = "Csdn", Action = PublishAction.Copy, Success = true,
        }, file);

        Assert.Single(PublishLogStore.List(file, platform: "Csdn", success: false));
        Assert.Equal(2, PublishLogStore.List(file, platform: "Csdn").Count);
        Assert.Equal(2, PublishLogStore.List(file, success: true).Count);
        Assert.Empty(PublishLogStore.List(file, platform: "WeChat"));
    }

    [Fact]
    public void List_sorts_by_time_descending_regardless_of_append_order()
    {
        var file = TempFile();
        PublishLogStore.Append(new PublishLogEntry
        {
            Time = new DateTime(2026, 1, 5, 8, 0, 0), Title = "old", Platform = "Site",
        }, file);
        PublishLogStore.Append(new PublishLogEntry
        {
            Time = new DateTime(2026, 6, 1, 8, 0, 0), Title = "new", Platform = "Site",
        }, file);
        PublishLogStore.Append(new PublishLogEntry
        {
            Time = new DateTime(2026, 3, 1, 8, 0, 0), Title = "mid", Platform = "Site",
        }, file);

        Assert.Equal(["new", "mid", "old"], PublishLogStore.List(file).Select(e => e.Title));
    }

    [Fact]
    public void Clear_removes_all_records()
    {
        var file = TempFile();
        PublishLogStore.Append(new PublishLogEntry { ArticlePath = "a.md", Platform = "Site" }, file);
        Assert.Single(PublishLogStore.List(file));

        PublishLogStore.Clear(file);
        Assert.Empty(PublishLogStore.List(file));
    }
}
