using System.Text.Json;
using Vox.Git;

namespace Vox.App.Services;

/// <summary>平台账户配置（凭据本体存系统凭据管理器，此处只承载 JSON 序列化与状态口径）。</summary>
public sealed record WeChatAccount(string AppId, string AppSecret)
{
    public bool IsConfigured => !string.IsNullOrWhiteSpace(AppId) && !string.IsNullOrWhiteSpace(AppSecret);
}

public sealed record CnblogsAccount(string Url, string Token)
{
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Url) && !string.IsNullOrWhiteSpace(Token);
}

/// <summary>Token 字段兼容既有凭据存储格式；当前保存的是 CSDN 登录 Cookie。</summary>
public sealed record CsdnAccount(string Token)
{
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Token);
}

/// <summary>平台账户存取：value 为 JSON blob 经 ICredentialStore 落系统凭据管理器（vox/secret/platform/*）。</summary>
public static class PlatformAccounts
{
    private static readonly ICredentialStore Store = CredentialStoreFactory.Create();

    public static WeChatAccount? LoadWeChat() => Load<WeChatAccount>("platform/wechat");
    public static CnblogsAccount? LoadCnblogs() => Load<CnblogsAccount>("platform/cnblogs");
    public static CsdnAccount? LoadCsdn() => Load<CsdnAccount>("platform/csdn");

    public static void SaveWeChat(WeChatAccount account) => Save("platform/wechat", account);
    public static void SaveCnblogs(CnblogsAccount account) => Save("platform/cnblogs", account);
    public static void SaveCsdn(CsdnAccount account) => Save("platform/csdn", account);

    /// <summary>账户连接态：配置且有最近成功记录 = 已连接；配置但上次 401 = 失效；未配置。</summary>
    public static string ProbeWeChat() => LoadWeChat() is { IsConfigured: true } ? "connected" : "none";

    private static T? Load<T>(string key) where T : class
    {
        var raw = Store.Get(key);
        if (string.IsNullOrWhiteSpace(raw)) return null;
        try
        {
            return (T?)JsonSerializer.Deserialize(raw, typeof(T), AppJsonContext.Default);
        }
        catch
        {
            return null;
        }
    }

    private static void Save<T>(string key, T value) where T : class =>
        Store.Save(key, JsonSerializer.Serialize(value, value.GetType(), AppJsonContext.Default));
}
