using Vox.Git;
using Xunit;

public class CredentialStoreTests
{
    /// <summary>Windows 凭据管理器真实写入/读取/删除往返（2026-11-13 回归护栏）：此前 CredWrite 用
    /// `ref` + 非 blittable 类声明，现代 .NET 按二级指针封送致 ERROR_INVALID_PARAMETER(87)，路径全灭。</summary>
    [Fact]
    public void Save_Get_Delete_roundtrip()
    {
        if (!OperatingSystem.IsWindows()) return; // 非 Windows 走 ProtectedFileCredentialStore，往返语义在 Core.Tests 覆盖
        var store = CredentialStoreFactory.Create();
        Assert.IsType<WindowsCredentialStore>(store);

        const string key = "test/roundtrip";
        try
        {
            store.Save(key, "secret-值-123");
            Assert.Equal("secret-值-123", store.Get(key));
        }
        finally
        {
            store.Delete(key);
        }
        Assert.Null(store.Get(key));
    }
}
