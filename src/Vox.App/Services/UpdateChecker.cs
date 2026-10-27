using System.Diagnostics;
using System.Text.Json;

namespace Vox.App.Services;

/// <summary>更新检查结果。</summary>
public sealed record UpdateCheckResult(bool HasUpdate, string? NewVersion, string? ReleaseUrl, string? Notes)
{
    public static UpdateCheckResult UpToDate() => new(false, null, null, null);
}

/// <summary>
/// 轻量更新检查：查询 GitHub Releases 最新版并与当前程序集版本比较（决策 D2：不依赖 Toolkit 系，
/// 自实现最小集；下载安装后续里程碑再引入安装器握手）。
/// </summary>
public sealed class UpdateChecker
{
    public const string Repo = "dotnet9/Vox";
    private static readonly HttpClient Http = CreateHttp();

    public string CurrentVersionText { get; }

    public UpdateChecker()
    {
        CurrentVersionText = GetCurrentVersion();
    }

    private static string GetCurrentVersion()
    {
        var exe = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(exe))
        {
            var info = FileVersionInfo.GetVersionInfo(exe);
            if (!string.IsNullOrEmpty(info.ProductVersion))
            {
                // 产品版本带 +提交号 的构建元数据，界面只显示语义版本。
                var plus = info.ProductVersion.IndexOf('+');
                return plus > 0 ? info.ProductVersion[..plus] : info.ProductVersion;
            }
        }
        var name = typeof(UpdateChecker).Assembly.GetName().Version;
        return name is null ? "0.0.0" : name.ToString();
    }

    private static HttpClient CreateHttp()
    {
        var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Vox-UpdateChecker");
        http.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json");
        return http;
    }

    private static Version? ParseVersion(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var candidate = text.Trim().TrimStart('v', 'V');
        var plus = candidate.IndexOf('+');
        if (plus >= 0) candidate = candidate[..plus];
        return Version.TryParse(candidate, out var v) ? v : null;
    }

    /// <summary>查询 releases/latest；仓库尚无任何 release 时视为已是最新。</summary>
    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get, $"https://api.github.com/repos/{Repo}/releases/latest");
        using var response = await Http.SendAsync(request, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return UpdateCheckResult.UpToDate();
        }
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = json.RootElement;
        var tag = root.TryGetProperty("tag_name", out var tagEl) ? tagEl.GetString() : null;
        var url = root.TryGetProperty("html_url", out var urlEl) ? urlEl.GetString() : null;
        var notes = root.TryGetProperty("body", out var bodyEl) ? bodyEl.GetString() : null;

        var current = ParseVersion(CurrentVersionText);
        var latest = ParseVersion(tag ?? string.Empty);
        if (latest is null || current is null || latest <= current)
        {
            return UpdateCheckResult.UpToDate();
        }
        return new UpdateCheckResult(true, tag, url ?? $"https://github.com/{Repo}/releases/latest", notes);
    }

    public static void OpenReleasePage(string? url = null)
    {
        var target = string.IsNullOrWhiteSpace(url)
            ? $"https://github.com/{Repo}/releases/latest"
            : url;
        Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
    }
}
