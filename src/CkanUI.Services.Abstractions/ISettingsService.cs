namespace CkanUI.Services.Abstractions;

/// <summary>
/// 包装 Core 的 IConfiguration（02 篇 §3）。键使用 Core 的配置键字符串，值统一字符串化，
/// 避免把 CKAN 类型泄漏到 VM 层。读-改-写由实现内部串行化。
/// </summary>
public interface ISettingsService
{
    event Action<string, string?>? SettingChanged;

    Task<OperationResult<string?>> GetAsync(string key, CancellationToken ct = default);

    Task<OperationResult<bool>> SetAsync(string key, string? value, CancellationToken ct = default);

    Task<OperationResult<IReadOnlyDictionary<string, string?>>> GetAllAsync(CancellationToken ct = default);

    /// <summary>写回后读回校验（02 篇 §3 要求），不一致返回失败。</summary>
    Task<OperationResult<string?>> SetAndVerifyAsync(string key, string? value, CancellationToken ct = default);
}
