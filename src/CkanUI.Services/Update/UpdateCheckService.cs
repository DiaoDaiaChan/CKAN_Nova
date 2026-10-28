using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CKAN.Versioning;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.Services.Diagnostics;

namespace CkanUI.Services.Update;

/// <summary>
/// 自动更新检查服务实现（F-52 / UI-27）。
/// 支持 GitHub Releases 与 S3 Dev Builds，提供安全降级比对与网络异常保护。
/// </summary>
public sealed class UpdateCheckService : IUpdateCheckService
{
    public const string GitHubLatestReleaseApiUrl = "https://api.github.com/repos/KSP-CKAN/CKAN/releases/latest";
    public const string S3DevBuildVersionUrl = "https://ksp-ckan.s3-us-west-2.amazonaws.com/version.json";

    private readonly ISettingsService? _settingsService;
    private readonly Func<string, CancellationToken, Task<string>> _httpGetHandler;
    private readonly string _currentVersionString;

    /// <summary>
    /// 标准依赖注入构造函数。
    /// </summary>
    public UpdateCheckService(ISettingsService? settingsService = null)
        : this(settingsService, DefaultHttpGetAsync, CoreVersionInfo.ReleaseVersion)
    {
    }

    /// <summary>
    /// 用于测试或自定义注入的构造函数。
    /// </summary>
    public UpdateCheckService(
        ISettingsService? settingsService,
        Func<string, CancellationToken, Task<string>> httpGetHandler,
        string currentVersionString)
    {
        _settingsService = settingsService;
        _httpGetHandler = httpGetHandler ?? throw new ArgumentNullException(nameof(httpGetHandler));
        _currentVersionString = string.IsNullOrWhiteSpace(currentVersionString) ? "v0.0.0" : currentVersionString;
    }

    public async Task<OperationResult<UpdateCheckResult>> CheckForUpdatesAsync(
        bool includeDevBuilds = false,
        CancellationToken ct = default)
    {
        try
        {
            var devBuildsRequested = includeDevBuilds;
            if (!devBuildsRequested && _settingsService != null)
            {
                var storedValue = await _settingsService.GetAsync("DevBuilds", ct).ConfigureAwait(false);
                if (storedValue.IsSuccess && bool.TryParse(storedValue.Value, out var parsed) && parsed)
                {
                    devBuildsRequested = true;
                }
            }

            if (devBuildsRequested)
            {
                var devResult = await CheckDevBuildAsync(ct).ConfigureAwait(false);
                if (devResult != null)
                {
                    return OperationResult<UpdateCheckResult>.Ok(devResult);
                }
            }

            var stableResult = await CheckGitHubReleaseAsync(ct).ConfigureAwait(false);
            return OperationResult<UpdateCheckResult>.Ok(stableResult);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return OperationResult<UpdateCheckResult>.Fail("检查更新已取消。");
        }
        catch (Exception ex)
        {
            return OperationResult<UpdateCheckResult>.Fail($"检查更新失败: {ex.Message}");
        }
    }

    private async Task<UpdateCheckResult?> CheckDevBuildAsync(CancellationToken ct)
    {
        try
        {
            var json = await _httpGetHandler(S3DevBuildVersionUrl, ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            string? versionStr = null;
            if (root.TryGetProperty("version", out var vProp))
            {
                versionStr = vProp.GetString();
            }

            if (string.IsNullOrWhiteSpace(versionStr))
            {
                return null;
            }

            string? buildUrl = null;
            if (root.TryGetProperty("url", out var uProp))
            {
                buildUrl = uProp.GetString();
            }
            else if (root.TryGetProperty("build", out var bProp))
            {
                buildUrl = bProp.GetString();
            }

            var hasUpdate = CompareVersions(versionStr, _currentVersionString) > 0;
            if (hasUpdate)
            {
                return new UpdateCheckResult(
                    HasUpdate: true,
                    CurrentVersion: _currentVersionString,
                    LatestVersion: versionStr,
                    ReleaseNotes: "CKAN 开发预览版本 (Dev Build)",
                    ReleaseUrl: buildUrl ?? "https://github.com/KSP-CKAN/CKAN/actions",
                    IsDevBuild: true);
            }

            return null;
        }
        catch
        {
            // Dev build 获取失败时，平滑降级到 Release 检查
            return null;
        }
    }

    private async Task<UpdateCheckResult> CheckGitHubReleaseAsync(CancellationToken ct)
    {
        var json = await _httpGetHandler(GitHubLatestReleaseApiUrl, ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json))
        {
            return new UpdateCheckResult(
                HasUpdate: false,
                CurrentVersion: _currentVersionString,
                LatestVersion: _currentVersionString,
                ReleaseNotes: null,
                ReleaseUrl: null,
                IsDevBuild: false);
        }

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var tagName = root.TryGetProperty("tag_name", out var tagProp) ? tagProp.GetString() : null;
        var htmlUrl = root.TryGetProperty("html_url", out var htmlProp) ? htmlProp.GetString() : null;
        var body = root.TryGetProperty("body", out var bodyProp) ? bodyProp.GetString() : null;

        var latestVersion = tagName ?? _currentVersionString;
        var hasUpdate = !string.IsNullOrWhiteSpace(tagName) && CompareVersions(tagName, _currentVersionString) > 0;

        // 自动安装（F-52）要有可直接下载的包：只有「有更新」时才去挑资产，
        // 否则既浪费解析也容易把旧包的地址带进弹窗。
        var asset = hasUpdate ? PickUpdateAsset(root) : default;

        return new UpdateCheckResult(
            HasUpdate: hasUpdate,
            CurrentVersion: _currentVersionString,
            LatestVersion: latestVersion,
            ReleaseNotes: body,
            ReleaseUrl: htmlUrl,
            IsDevBuild: false,
            AssetUrl: asset.Url,
            AssetSha256: asset.Sha256,
            AssetSize: asset.Size);
    }

    /// <summary>
    /// 从 GitHub release 的 <c>assets</c> 里挑出可直接下载的更新包。
    /// 排序口径：名字里带 win 的优先、带 x64/amd64 的次优先，其余 zip 兜底；
    /// 一个 zip 都没有时返回全空（界面据此降级为「打开发布页手动下载」）。
    /// </summary>
    private static (string? Url, string? Sha256, long Size) PickUpdateAsset(JsonElement root)
    {
        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
        {
            return (null, null, 0);
        }

        string? bestUrl = null;
        string? bestSha = null;
        var bestSize = 0L;
        var bestRank = -1;

        foreach (var asset in assets.EnumerateArray())
        {
            if (asset.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var url = asset.TryGetProperty("browser_download_url", out var urlProp) ? urlProp.GetString() : null;
            if (string.IsNullOrWhiteSpace(url) || !url.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var name = asset.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? string.Empty : string.Empty;
            var rank = 0;
            if (name.Contains("win", StringComparison.OrdinalIgnoreCase))
            {
                rank += 2;
            }

            if (name.Contains("x64", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("amd64", StringComparison.OrdinalIgnoreCase))
            {
                rank += 1;
            }

            if (rank <= bestRank)
            {
                continue;
            }

            bestRank = rank;
            bestUrl = url;
            bestSize = asset.TryGetProperty("size", out var sizeProp) && sizeProp.TryGetInt64(out var size) ? size : 0;
            bestSha = asset.TryGetProperty("digest", out var digestProp)
                ? NormalizeDigest(digestProp.GetString())
                : null;
        }

        return (bestUrl, bestSha, bestSize);
    }

    /// <summary>把 GitHub 的 <c>digest</c>（形如 <c>sha256:HEX</c>）归一成裸 SHA256；不认识的格式一律丢弃。</summary>
    private static string? NormalizeDigest(string? digest)
    {
        if (string.IsNullOrWhiteSpace(digest))
        {
            return null;
        }

        var text = digest.Trim();
        const string prefix = "sha256:";
        if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            text = text[prefix.Length..];
        }

        return text.Length == 64 ? text : null;
    }

    /// <summary>
    /// 比较两个版本字符串（例如 "v1.36.0", "1.36.1" 等）。
    /// 返回值 > 0 表示 v1 > v2。
    /// 优先使用 CKAN Core 提供的 ModuleVersion 语义比较，解析失败时降级到 System.Version 或字符串比较。
    /// </summary>
    public static int CompareVersions(string v1, string v2)
    {
        if (string.Equals(v1, v2, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        var clean1 = CleanVersion(v1);
        var clean2 = CleanVersion(v2);

        try
        {
            var mv1 = new ModuleVersion(clean1);
            var mv2 = new ModuleVersion(clean2);
            return mv1.CompareTo(mv2);
        }
        catch
        {
            // 降级使用 System.Version
            if (Version.TryParse(clean1, out var sysVer1) && Version.TryParse(clean2, out var sysVer2))
            {
                return sysVer1.CompareTo(sysVer2);
            }

            return string.Compare(clean1, clean2, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static string CleanVersion(string v)
    {
        if (string.IsNullOrWhiteSpace(v))
        {
            return "0.0.0";
        }

        var trimmed = v.Trim();
        if (trimmed.StartsWith("v", StringComparison.OrdinalIgnoreCase) && trimmed.Length > 1 && char.IsDigit(trimmed[1]))
        {
            trimmed = trimmed[1..];
        }

        return trimmed;
    }

    private static async Task<string> DefaultHttpGetAsync(string url, CancellationToken ct)
    {
        using var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("CKAN-UI-VersionChecker");
        client.Timeout = TimeSpan.FromSeconds(15);

        using var response = await client.GetAsync(url, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }
}
