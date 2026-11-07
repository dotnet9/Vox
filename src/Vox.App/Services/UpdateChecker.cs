using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Vox.App.Services;

/// <summary>更新检查结果。<paramref name="PublishedDate"/> 是最新 release 的发布日期（yyyy-MM-dd），
/// 已是最新时用于「当前 x（日期 发布）」的展示。</summary>
public sealed record UpdateCheckResult(bool HasUpdate, string? NewVersion, string? ReleaseUrl, string? Notes,
    UpdateAssetInfo? Asset = null, string? PublishedDate = null)
{
    public static UpdateCheckResult UpToDate(string? publishedDate = null) =>
        new(false, null, null, null, null, publishedDate);
}

/// <summary>一个可下载的发布资产（GitHub Releases assets 里的安装包）。ChecksumUrl 指向同名 .sha256 伴生文件（可空）。</summary>
public sealed record UpdateAssetInfo(string Url, string Name, long SizeBytes, string? ChecksumUrl = null);

/// <summary>下载进度（字节口径，界面换算 MB/百分比）。</summary>
public readonly record struct UpdateDownloadProgress(long ReceivedBytes, long TotalBytes)
{
    public double Percent => TotalBytes > 0 ? Math.Clamp(ReceivedBytes * 100.0 / TotalBytes, 0, 100) : 0;
    public double DoneMB => ReceivedBytes / 1048576.0;
    public double TotalMB => TotalBytes / 1048576.0;
}

/// <summary>
/// 轻量更新检查与下载：查询 GitHub Releases 最新版并与当前程序集版本比较，选中当前平台的安装资产，
/// 流式下载到临时目录并报告进度（决策 D2：不依赖 Toolkit 系，自实现最小集；安装器握手后续里程碑再引入）。
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

    /// <summary>版本解析：主干 Version + 预发布序号（0.1.0-preview.9 → (0.1.0, 9)；无后缀记 int.MaxValue，
    /// 正式版高于任何 preview.N）。Vox 当前按 0.1.0-preview.N 命名，必须比较后缀数字否则 preview.10 识别不出比 preview.9 新。</summary>
    private static (Version Core, int Preview)? ParseVersion(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var candidate = text.Trim().TrimStart('v', 'V');
        var plus = candidate.IndexOf('+');
        if (plus >= 0) candidate = candidate[..plus];
        var dash = candidate.IndexOf('-');
        var coreText = dash > 0 ? candidate[..dash] : candidate;
        if (!Version.TryParse(coreText, out var core)) return null;
        var preview = int.MaxValue;
        if (dash > 0)
        {
            var suffix = candidate[(dash + 1)..];
            var dot = suffix.LastIndexOf('.');
            var tail = dot >= 0 ? suffix[(dot + 1)..] : suffix;
            if (!int.TryParse(tail, out preview)) preview = 0; // 非数字后缀（rc 等）垫底
        }
        return (core, preview);
    }

    private static bool IsNewer((Version Core, int Preview) latest, (Version Core, int Preview) current)
    {
        if (latest.Core != current.Core) return latest.Core > current.Core;
        return latest.Preview > current.Preview;
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
        var published = root.TryGetProperty("published_at", out var publishedEl) && publishedEl.GetString() is { Length: > 10 } date
            ? date[..10]
            : null;
        var assets = root.TryGetProperty("assets", out var assetEl) && assetEl.ValueKind == JsonValueKind.Array
            ? (JsonElement?)assetEl
            : null;

        var current = ParseVersion(CurrentVersionText);
        var latest = ParseVersion(tag ?? string.Empty);
        if (latest is null || current is null || !IsNewer(latest.Value, current.Value))
        {
            return UpdateCheckResult.UpToDate(published);
        }
        var asset = assets is null ? null : PickAsset(assets.Value);
        return new UpdateCheckResult(true, tag, url ?? $"https://github.com/{Repo}/releases/latest", notes, asset, published);
    }

    /// <summary>从 assets 里挑当前平台的安装资产：先按平台关键词过滤，再按架构匹配加分，
    /// 优先安装器（exe/msi/dmg/deb/AppImage），其次 zip，其余校验/符号文件不认。</summary>
    private static UpdateAssetInfo? PickAsset(JsonElement assets)
    {
        string[] osKeys;
        string[] installerExts;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            osKeys = ["win"];
            installerExts = [".exe", ".msi"];
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            osKeys = ["osx", "mac", "darwin"];
            installerExts = [".dmg", ".pkg"];
        }
        else
        {
            osKeys = ["linux"];
            installerExts = [".deb", ".appimage"];
        }
        var archKey = RuntimeInformation.OSArchitecture switch
        {
            Architecture.Arm64 => "arm64",
            Architecture.X86 => "x86",
            _ => "x64",
        };
        var otherArchKeys = new[] { "x64", "x86", "arm64", "arm" }.Where(k => k != archKey).ToArray();

        UpdateAssetInfo? best = null;
        var bestScore = int.MinValue;
        var checksums = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : null;
            var url = asset.TryGetProperty("browser_download_url", out var urlEl) ? urlEl.GetString() : null;
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(url)) continue;
            var lower = name.ToLowerInvariant();
            if (lower.EndsWith(".sha256", StringComparison.Ordinal))
            {
                checksums[name[..^".sha256".Length]] = url; // 伴生校验文件：产物名 → sha256 文件地址
                continue;
            }
            if (!osKeys.Any(key => lower.Contains(key, StringComparison.Ordinal))) continue;

            var ext = Path.GetExtension(lower);
            var score = 0;
            if (installerExts.Contains(ext)) score += 4;
            else if (ext == ".zip") score += 2;
            else continue; // .sha256/.sig/.txt 等附属文件不参与
            if (lower.Contains(archKey, StringComparison.Ordinal)) score += 3;
            else if (otherArchKeys.Any(key => lower.Contains(key, StringComparison.Ordinal))) score -= 3;

            if (score > bestScore)
            {
                bestScore = score;
                var size = asset.TryGetProperty("size", out var sizeEl) && sizeEl.ValueKind == JsonValueKind.Number
                    ? sizeEl.GetInt64()
                    : 0;
                best = new UpdateAssetInfo(url, name, size,
                    checksums.TryGetValue(name, out var checksumUrl) ? checksumUrl : null);
            }
        }
        return best;
    }

    /// <summary>流式下载发布资产到 %TEMP%\VoxUpdate，报告字节进度；取消时抛 OperationCanceledException（半成品文件保留）。
    /// 下载完成即校验 SHA-256（边下边算）：发布流水线为产物附 .sha256 伴生文件（内容形如「&lt;hex&gt;  &lt;文件名&gt;」）；
    /// 不匹配抛 InvalidOperationException（更新会话转失败可重试），伴生文件缺失（旧版 Release）跳过校验不阻断。</summary>
    public async Task<string> DownloadAssetAsync(UpdateAssetInfo asset,
        IProgress<UpdateDownloadProgress> progress, CancellationToken cancellationToken = default)
    {
        var directory = Path.Combine(Path.GetTempPath(), "VoxUpdate");
        Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, asset.Name);

        using var response = await Http.GetAsync(asset.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength ?? asset.SizeBytes;

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var targetStream = File.Create(target);
        using var sha = System.Security.Cryptography.SHA256.Create();
        var buffer = new byte[1 << 16];
        long received = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await targetStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            sha.TransformBlock(buffer, 0, read, null, 0);
            received += read;
            progress.Report(new UpdateDownloadProgress(received, total));
        }
        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        await VerifyChecksumAsync(target, Convert.ToHexString(sha.Hash!), asset.ChecksumUrl, cancellationToken);
        return target;
    }

    /// <summary>取伴生 .sha256（内容首段 hex）与本地哈希比对；不匹配删除坏包并抛异常，转失败可重试。</summary>
    private async Task VerifyChecksumAsync(string target, string actualHex, string? checksumUrl, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(checksumUrl)) return;
        try
        {
            var expected = await Http.GetStringAsync(checksumUrl, ct);
            var hex = expected.Split(' ', '\t', '\n')[0].Trim();
            if (!string.Equals(hex, actualHex, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(target);
                throw new InvalidOperationException(
                    $"Checksum mismatch for {Path.GetFileName(target)}: expected {hex[..Math.Min(12, hex.Length)]}…, got {actualHex[..12]}…");
            }
        }
        catch (InvalidOperationException) { throw; }
        catch (Exception ex)
        {
            // 校验文件拉不到不阻断安装（产物主体已落盘），带原因上抛由会话决定呈现
            throw new InvalidOperationException($"Checksum file unavailable: {ex.Message}", ex);
        }
    }

    public static void OpenReleasePage(string? url = null)
    {
        var target = string.IsNullOrWhiteSpace(url)
            ? $"https://github.com/{Repo}/releases/latest"
            : url;
        Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
    }
}
