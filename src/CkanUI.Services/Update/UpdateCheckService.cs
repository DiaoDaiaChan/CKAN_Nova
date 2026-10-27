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

        return new UpdateCheckResult(
            HasUpdate: hasUpdate,
            CurrentVersion: _currentVersionString,
            LatestVersion: latestVersion,
            ReleaseNotes: body,
            ReleaseUrl: htmlUrl,
            IsDevBuild: false);
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
