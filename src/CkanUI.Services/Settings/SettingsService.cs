using System.Globalization;
using System.Text.Json;
using CKAN.Configuration;
using CKAN.Games;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Logging;
using log4net;

namespace CkanUI.Services.Settings;

/// <summary>
/// ISettingsService 实现（02 篇 §3）：直接包装 Core 的 IConfiguration，不缓存快照——
/// 每次读都现取 IConfiguration，每次写都立即交给 Core 落盘（Core 属性 setter 即 SaveConfig）。
/// 所有读写经 SemaphoreSlim(1) 串行化，因为 Core 的 Config 内部字典非线程安全且落盘不是原子的（SP-03）。
/// SettingChanged 经 IDispatcher.Post 封送到 UI 线程（service-contracts §3 规则 2）。
/// </summary>
public sealed class SettingsService : ISettingsService, IDisposable
{
    private static readonly ILog Log = LoggingSetup.GetLogger(LoggingSetup.UiLoggerName);

    private readonly IConfiguration _config;
    private readonly IDispatcher _dispatcher;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public SettingsService(IConfiguration config, IDispatcher dispatcher)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    public event Action<string, string?>? SettingChanged;

    public Task<OperationResult<string?>> GetAsync(string key, CancellationToken ct = default) =>
        RunAsync(key, () =>
        {
            var value = Read(key, out var known);
            return known
                ? OperationResult<string?>.Ok(value)
                : OperationResult<string?>.Fail($"未知设置项：{key}");
        }, ct);

    public async Task<OperationResult<bool>> SetAsync(string key, string? value, CancellationToken ct = default)
    {
        var result = await RunAsync(key, () => WriteAndReadBack(key, value), ct).ConfigureAwait(false);
        return result.IsSuccess
            ? OperationResult<bool>.Ok(true)
            : OperationResult<bool>.Fail(result.Message ?? "设置写入失败", result.Detail, result.Retryable);
    }

    public Task<OperationResult<IReadOnlyDictionary<string, string?>>> GetAllAsync(CancellationToken ct = default) =>
        RunAsync<IReadOnlyDictionary<string, string?>>("*", () =>
        {
            var all = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach (var key in SettingKeys.Scalars)
            {
                all[key] = Read(key, out _);
            }

            foreach (var host in _config.GetAuthTokenHosts().ToArray())
            {
                all[SettingKeys.AuthTokenPrefix + host] = _config.TryGetAuthToken(host, out var token) ? token : null;
            }

            foreach (var game in KnownGames.knownGames)
            {
                var filters = _config.GetGlobalInstallFilters(game);
                if (filters.Length > 0)
                {
                    all[SettingKeys.GlobalInstallFiltersPrefix + game.ShortName] = JsonSerializer.Serialize(filters);
                }
            }

            return OperationResult<IReadOnlyDictionary<string, string?>>.Ok(all);
        }, ct);

    public async Task<OperationResult<string?>> SetAndVerifyAsync(string key, string? value, CancellationToken ct = default)
    {
        var result = await RunAsync(key, () =>
        {
            var written = WriteAndReadBack(key, value);
            if (!written.IsSuccess)
            {
                return written;
            }

            var expected = Normalize(key, value);
            return string.Equals(expected, written.Value, StringComparison.Ordinal)
                ? written
                // Core 静默丢弃非法值（如 Language 不在 Utilities.AvailableLanguages 内）不抛异常，
                // 读回即旧值。这是「输入被拒绝」而非「暂时性故障」：换成合法值可成功，
                // 但用原值重试永远不会成功，所以 Retryable=false（与 §4.3 锁定失败同类）。
                : OperationResult<string?>.Fail(
                    $"设置项 {key} 的值被拒绝",
                    $"期望 '{expected ?? "<null>"}'，读回 '{written.Value ?? "<null>"}'（Core 静默忽略了该值）",
                    retryable: false);
        }, ct).ConfigureAwait(false);
        return result;
    }

    public void Dispose() => _gate.Dispose();

    // 统一的串行化 + 异常收口：接口异常不穿透（01 篇 §8）。
    // Task.Run 把 Core 的同步落盘挪离调用线程，避免 UI 线程卡在磁盘 IO 上。
    private async Task<OperationResult<T>> RunAsync<T>(string key, Func<OperationResult<T>> body, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return OperationResult<T>.Fail("设置项键名不能为空");
        }

        try
        {
            await _gate.WaitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return OperationResult<T>.Fail("操作已取消", retryable: true);
        }
        catch (ObjectDisposedException)
        {
            return OperationResult<T>.Fail("设置服务已释放");
        }

        try
        {
            return await Task.Run(body, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"设置项 {key} 读写失败", ex);
            return OperationResult<T>.Fail($"无法读写配置文件（{key}）", ex.Message, retryable: true);
        }
        catch (Exception ex) when (ex is FormatException or OverflowException or JsonException or ArgumentException)
        {
            return OperationResult<T>.Fail($"设置项 {key} 的值格式不正确", ex.Message);
        }
        finally
        {
            _gate.Release();
        }
    }

    // 必须在 _gate 内调用。写一项 → 立即从 IConfiguration 读回该项（02 篇 §3 规则 2）。
    private OperationResult<string?> WriteAndReadBack(string key, string? value)
    {
        if (!Write(key, value))
        {
            return OperationResult<string?>.Fail($"未知设置项：{key}");
        }

        var readBack = Read(key, out _);
        RaiseChanged(key, readBack);
        return OperationResult<string?>.Ok(readBack);
    }

    private void RaiseChanged(string key, string? value)
    {
        var handler = SettingChanged;
        if (handler is not null)
        {
            _dispatcher.Post(() => handler(key, value));
        }
    }

    private string? Read(string key, out bool known)
    {
        known = true;
        switch (key)
        {
            case SettingKeys.AutoStartInstance:
                return _config.AutoStartInstance;
            case SettingKeys.DownloadCacheDir:
                return _config.DownloadCacheDir;
            case SettingKeys.CacheSizeLimit:
                return _config.CacheSizeLimit?.ToString(CultureInfo.InvariantCulture);
            case SettingKeys.RefreshRate:
                return _config.RefreshRate.ToString(CultureInfo.InvariantCulture);
            case SettingKeys.Language:
                return _config.Language;
            case SettingKeys.DevBuilds:
                return _config.DevBuilds is { } dev ? (dev ? "true" : "false") : null;
            case SettingKeys.PreferredHosts:
                return _config.PreferredHosts.Length == 0 ? null : JsonSerializer.Serialize(_config.PreferredHosts);
        }

        if (TrySub(key, SettingKeys.AuthTokenPrefix, out var host))
        {
            return _config.TryGetAuthToken(host, out var token) ? token : null;
        }

        if (TrySub(key, SettingKeys.GlobalInstallFiltersPrefix, out var shortName) && KnownGames.GameByShortName(shortName) is { } game)
        {
            var filters = _config.GetGlobalInstallFilters(game);
            return filters.Length == 0 ? null : JsonSerializer.Serialize(filters);
        }

        known = false;
        return null;
    }

    private bool Write(string key, string? value)
    {
        switch (key)
        {
            case SettingKeys.AutoStartInstance:
                _config.AutoStartInstance = NullIfEmpty(value);
                return true;
            case SettingKeys.DownloadCacheDir:
                _config.DownloadCacheDir = NullIfEmpty(value);
                return true;
            case SettingKeys.CacheSizeLimit:
                _config.CacheSizeLimit = NullIfEmpty(value) is { } size ? long.Parse(size, NumberStyles.Integer, CultureInfo.InvariantCulture) : null;
                return true;
            case SettingKeys.RefreshRate:
                _config.RefreshRate = NullIfEmpty(value) is { } rate ? int.Parse(rate, NumberStyles.Integer, CultureInfo.InvariantCulture) : 0;
                return true;
            case SettingKeys.Language:
                _config.Language = NullIfEmpty(value);
                return true;
            case SettingKeys.DevBuilds:
                _config.DevBuilds = NullIfEmpty(value) is { } dev ? bool.Parse(dev) : null;
                return true;
            case SettingKeys.PreferredHosts:
                _config.PreferredHosts = ParseArray(value) ?? Array.Empty<string?>();
                return true;
        }

        if (TrySub(key, SettingKeys.AuthTokenPrefix, out var host))
        {
            _config.SetAuthToken(host, NullIfEmpty(value));
            return true;
        }

        if (TrySub(key, SettingKeys.GlobalInstallFiltersPrefix, out var shortName) && KnownGames.GameByShortName(shortName) is { } game)
        {
            var filters = ParseArray(value)?.Where(f => !string.IsNullOrEmpty(f)).Select(f => f!).ToArray();
            _config.SetGlobalInstallFilters(game, filters ?? Array.Empty<string>());
            return true;
        }

        return false;
    }

    // 期望的读回值：把输入按 Core 的规则规整后再比较，避免「写 5 读 5」之外的合法差异被判为失败。
    private static string? Normalize(string key, string? value)
    {
        var v = NullIfEmpty(value);
        switch (key)
        {
            case SettingKeys.DownloadCacheDir:
                return v is null ? null : Path.GetFullPath(v);
            case SettingKeys.CacheSizeLimit:
                return v is null || long.Parse(v, NumberStyles.Integer, CultureInfo.InvariantCulture) < 0
                    ? null
                    : long.Parse(v, NumberStyles.Integer, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
            case SettingKeys.RefreshRate:
                var rate = v is null ? 0 : int.Parse(v, NumberStyles.Integer, CultureInfo.InvariantCulture);
                return (rate <= 0 ? 0 : rate).ToString(CultureInfo.InvariantCulture);
            case SettingKeys.DevBuilds:
                return v is null ? null : (bool.Parse(v) ? "true" : "false");
            case SettingKeys.PreferredHosts:
                var hosts = ParseArray(v);
                return hosts is { Length: > 0 } ? JsonSerializer.Serialize(hosts) : null;
        }

        if (key.StartsWith(SettingKeys.GlobalInstallFiltersPrefix, StringComparison.Ordinal))
        {
            var filters = ParseArray(v)?.Where(f => !string.IsNullOrEmpty(f)).ToArray();
            return filters is { Length: > 0 } ? JsonSerializer.Serialize(filters) : null;
        }

        return v;
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;

    private static string?[]? ParseArray(string? value) =>
        NullIfEmpty(value) is { } json ? JsonSerializer.Deserialize<string?[]>(json) : null;

    private static bool TrySub(string key, string prefix, out string sub)
    {
        if (key.StartsWith(prefix, StringComparison.Ordinal) && key.Length > prefix.Length)
        {
            sub = key[prefix.Length..];
            return true;
        }

        sub = string.Empty;
        return false;
    }
}
