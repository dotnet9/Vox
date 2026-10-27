using Vox.Core;
using Xunit;

public class ToolAuditTests
{
    private static string TempFile() =>
        Path.Combine(Directory.CreateTempSubdirectory("vox-toolaudit").FullName, "tool-audit.jsonl");

    [Fact]
    public void Append_and_list_returns_newest_first()
    {
        var file = TempFile();
        ToolAuditStore.Append(new ToolAuditEntry
        {
            Time = new DateTime(2026, 10, 27, 10, 0, 0),
            Client = "内置 AI", Tool = "article_write", Target = "2026/10/a.md · §现象",
            Result = "ok", Detail = "续写本节 · 120 字", DurationMs = 1400,
        }, file);
        ToolAuditStore.Append(new ToolAuditEntry
        {
            Time = new DateTime(2026, 10, 27, 11, 0, 0),
            Client = "Cursor", Tool = "article_read", Target = "2026/10/a.md", Result = "ok",
        }, file);

        var all = ToolAuditStore.List(file);
        Assert.Equal(2, all.Count);
        Assert.Equal("Cursor", all[0].Client);
        Assert.Equal("article_read", all[0].Tool);
        Assert.Equal(1400, all[1].DurationMs);
    }

    [Fact]
    public void List_filters_by_client_tool_and_result()
    {
        var file = TempFile();
        ToolAuditStore.Append(new ToolAuditEntry { Client = "内置 AI", Tool = "article_write", Result = "ok" }, file);
        ToolAuditStore.Append(new ToolAuditEntry { Client = "Cursor", Tool = "article_write", Result = "deny" }, file);
        ToolAuditStore.Append(new ToolAuditEntry { Client = "Cursor", Tool = "article_read", Result = "ok" }, file);

        Assert.Single(ToolAuditStore.List(file, client: "Cursor", tool: "article_write"));
        Assert.Single(ToolAuditStore.List(file, tool: "article_write", result: "deny"));
        Assert.Equal(2, ToolAuditStore.List(file, result: "ok").Count);
        Assert.Empty(ToolAuditStore.List(file, client: "Claude Desktop"));
    }

    [Fact]
    public void Clear_removes_all_records()
    {
        var file = TempFile();
        ToolAuditStore.Append(new ToolAuditEntry { Client = "内置 AI", Tool = "article_write" }, file);
        Assert.Single(ToolAuditStore.List(file));

        ToolAuditStore.Clear(file);
        Assert.Empty(ToolAuditStore.List(file));
    }
}
