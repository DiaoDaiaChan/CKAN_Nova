using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CKAN;
using CKAN.Configuration;
using CKAN.Games;
using CkanUI.Services.Abstractions;

namespace CkanUI.Services.Settings;

/// <summary>
/// 安装文件过滤器服务实现（F-16 / UI-31）。
/// 协调 CKAN Core 配置与 UI ISettingsService 双写或单写，提供默认规则与规范化处理。
/// </summary>
public sealed class InstallFiltersService : IInstallFiltersService
{
    private readonly IConfiguration? _configuration;
    private readonly ISettingsService? _settingsService;
    private readonly GameInstanceManager? _gameInstanceManager;

    public const string SettingsKey = "GlobalInstallFilters";

    private static readonly string[] DefaultFilters =
    [
        ".pdb",
        ".git",
        ".gitignore",
        "Thumbs.db",
        ".DS_Store",
        "*.craft",
        "README.md",
        "LICENSE.txt"
    ];

    public InstallFiltersService(
        IConfiguration? configuration = null,
        ISettingsService? settingsService = null,
        GameInstanceManager? gameInstanceManager = null)
    {
        _configuration = configuration;
        _settingsService = settingsService;
        _gameInstanceManager = gameInstanceManager;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetDefaultFilters() => DefaultFilters.ToArray();

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> LoadAsync(CancellationToken cancellationToken = default)
    {
        var game = _gameInstanceManager?.CurrentInstance?.Game;

        // 1. 尝试从 Core IConfiguration 读取
        if (_configuration is not null && game is not null)
        {
            try
            {
                var coreFilters = _configuration.GetGlobalInstallFilters(game);
                if (coreFilters != null && coreFilters.Length > 0)
                {
                    return SanitizeFilters(coreFilters);
                }
            }
            catch
            {
                // Core 获取失败时降级
            }
        }

        // 2. 尝试从 ISettingsService 读取
        if (_settingsService is not null)
        {
            var opResult = await _settingsService.GetAsync(SettingsKey, cancellationToken).ConfigureAwait(false);
            var raw = opResult.Value;
            if (!string.IsNullOrWhiteSpace(raw))
            {
                try
                {
                    var parsed = JsonSerializer.Deserialize<string[]>(raw);
                    if (parsed is not null && parsed.Length > 0)
                    {
                        return SanitizeFilters(parsed);
                    }
                }
                catch
                {
                    // JSON 解析失败则视为逗号/分号/换行分隔
                    var split = raw.Split(new[] { ';', ',', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    if (split.Length > 0)
                    {
                        return SanitizeFilters(split);
                    }
                }
            }
        }

        // 3. 默认规则
        return GetDefaultFilters();
    }

    /// <inheritdoc />
    public async Task SaveAsync(IReadOnlyCollection<string> filters, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filters);

        var sanitized = SanitizeFilters(filters);
        var game = _gameInstanceManager?.CurrentInstance?.Game;

        // 1. 写回 Core Configuration
        if (_configuration is not null && game is not null)
        {
            try
            {
                _configuration.SetGlobalInstallFilters(game, sanitized.ToArray());
            }
            catch
            {
                // 忽略或记录
            }
        }

        // 2. 写回 SettingsService
        if (_settingsService is not null)
        {
            var json = JsonSerializer.Serialize(sanitized);
            await _settingsService.SetAsync(SettingsKey, json, cancellationToken).ConfigureAwait(false);
        }
    }

    private static IReadOnlyList<string> SanitizeFilters(IEnumerable<string> items)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item)) continue;
            var trimmed = item.Trim();
            if (seen.Add(trimmed))
            {
                result.Add(trimmed);
            }
        }

        return result;
    }
}
