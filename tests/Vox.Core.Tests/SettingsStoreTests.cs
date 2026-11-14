using System.Text.Json;
using Vox.Core;
using Xunit;

public class SettingsStoreTests
{
    private static string WriteSettings(string json)
    {
        var path = Path.Combine(Path.GetTempPath(), $"vox-settings-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json);
        return path;
    }

    /// <summary>供应商库迁移守卫（2026-11-14）：旧 JSON（无 LlmProviders）Load 时播种 GLM 配置并指向激活项；
    /// Key 不在迁移范围（凭据管理器侧由 LlmService.GetApiKey 旧键回退兜底）。</summary>
    [Fact]
    public void Legacy_settings_seed_glm_provider_on_load()
    {
        var path = WriteSettings("""{"LlmProvider":"GLM","LlmModel":"glm-5.3-flash"}""");
        var settings = SettingsStore.Load(path);
        Assert.Single(settings.LlmProviders);
        var glm = settings.LlmProviders[0];
        Assert.Equal("glm", glm.Id);
        Assert.Equal("https://open.bigmodel.cn/api/coding/paas/v4", glm.BaseUrl);
        Assert.Contains("glm-5.3-flash", glm.Models);
        Assert.Equal("glm", settings.LlmActiveProvider);
    }

    /// <summary>激活项失效容错：LlmActiveProvider 指向已删除供应商时，Load 回指第一条。</summary>
    [Fact]
    public void Stale_active_provider_falls_back_to_first()
    {
        var path = WriteSettings("""{"LlmProviders":[{"Id":"a","Name":"A","BaseUrl":"https://a","Protocol":"openai","Models":["m"],"Enabled":true}],"LlmActiveProvider":"gone","LlmModel":"m"}""");
        var settings = SettingsStore.Load(path);
        Assert.Equal("a", settings.LlmActiveProvider);
    }

    /// <summary>新 JSON（已含供应商库）原样通过，不重复播种。</summary>
    [Fact]
    public void Fresh_provider_library_is_not_reseeded()
    {
        var path = WriteSettings("""{"LlmProviders":[{"Id":"openai","Name":"OpenAI","BaseUrl":"https://api.openai.com/v1","Protocol":"openai","Models":["gpt-6.1-sol"],"ThinkingSupported":false,"Enabled":true}],"LlmActiveProvider":"openai","LlmModel":"gpt-6.1-sol"}""");
        var settings = SettingsStore.Load(path);
        Assert.Single(settings.LlmProviders);
        Assert.Equal("openai", settings.LlmProviders[0].Id);
        Assert.Equal("gpt-6.1-sol", settings.LlmModel);
    }
}
