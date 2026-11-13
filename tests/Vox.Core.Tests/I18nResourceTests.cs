using System.Text.Json;
using Xunit;

public class I18nResourceTests
{
    private static string FindI18nDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 8 && dir is not null; dir = dir.Parent, i++)
        {
            var candidate = Path.Combine(dir.FullName, "src", "Vox.App", "I18n");
            if (Directory.Exists(candidate)) return candidate;
        }
        throw new InvalidOperationException("I18n directory not found from " + AppContext.BaseDirectory);
    }

    /// <summary>四语语言包守卫（2026-11-13）：JSON 合法（反序列化抛错即失败）+ 键集跨语言一致。
    /// 曾发生 en-US 值含未转义引号致整个文件解析失败（切英文语言包加载崩）——此测试防止复发。</summary>
    [Fact]
    public void Four_language_files_are_valid_json_with_consistent_keys()
    {
        var files = Directory.GetFiles(FindI18nDirectory(), "Vox.*.json").OrderBy(f => f).ToArray();
        Assert.Equal(4, files.Length);

        HashSet<string>? keys = null;
        foreach (var file in files)
        {
            var data = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(file));
            Assert.NotNull(data);
            Assert.True(data!.Count > 1000, $"{file} 键数异常：{data.Count}");
            if (keys is null)
            {
                keys = [.. data.Keys];
            }
            else
            {
                var missing = keys.Except(data.Keys).ToArray();
                var extra = data.Keys.Except(keys).ToArray();
                Assert.True(missing.Length == 0 && extra.Length == 0,
                    $"{Path.GetFileName(file)} 键集不一致：缺 {missing.FirstOrDefault()}，多 {extra.FirstOrDefault()}");
            }
        }
    }
}
