using CkanUI.Services.Abstractions;

namespace CkanUI.ViewModels.Tests;

/// <summary>
/// 测试替身：ISettingsService。默认把值存进内存字典，读写都成功；
/// 可以用 SetAndVerifyHandler / GetAllResult / 异常属性逐项编排失败路径。
/// </summary>
internal sealed class FakeSettingsService : ISettingsService
{
    private readonly Dictionary<string, string?> _values = new(StringComparer.Ordinal);

    /// <summary>契约事件（测试不订阅）。</summary>
    public event Action<string, string?>? SettingChanged
    {
        add { }
        remove { }
    }

    /// <summary>每一次写尝试（含失败的），按调用顺序。</summary>
    public List<(string Key, string? Value)> Attempts { get; } = new();

    /// <summary>写成功并落进字典的记录。</summary>
    public List<(string Key, string? Value)> Writes { get; } = new();

    /// <summary>自定义 SetAndVerifyAsync 行为；返回成功时才写入字典。</summary>
    public Func<string, string?, OperationResult<string?>>? SetAndVerifyHandler { get; set; }

    /// <summary>GetAllAsync 直接返回的结果（非空时优先于字典）。</summary>
    public OperationResult<IReadOnlyDictionary<string, string?>>? GetAllResult { get; set; }

    /// <summary>GetAllAsync 抛出的异常（验证异常收口）。</summary>
    public Exception? GetAllException { get; set; }

    /// <summary>SetAndVerifyAsync 抛出的异常（验证异常收口）。</summary>
    public Exception? SetAndVerifyException { get; set; }

    /// <summary>预置一个配置值。</summary>
    public FakeSettingsService Seed(string key, string? value)
    {
        _values[key] = value;
        return this;
    }

    public Task<OperationResult<string?>> GetAsync(string key, CancellationToken ct = default) =>
        Task.FromResult(OperationResult<string?>.Ok(_values.TryGetValue(key, out var value) ? value : null));

    public Task<OperationResult<bool>> SetAsync(string key, string? value, CancellationToken ct = default)
    {
        Attempts.Add((key, value));
        Writes.Add((key, value));
        _values[key] = value;
        return Task.FromResult(OperationResult<bool>.Ok(true));
    }

    public Task<OperationResult<IReadOnlyDictionary<string, string?>>> GetAllAsync(CancellationToken ct = default)
    {
        if (GetAllException is not null)
        {
            throw GetAllException;
        }

        if (GetAllResult is not null)
        {
            return Task.FromResult(GetAllResult);
        }

        IReadOnlyDictionary<string, string?> snapshot =
            new Dictionary<string, string?>(_values, StringComparer.Ordinal);
        return Task.FromResult(OperationResult<IReadOnlyDictionary<string, string?>>.Ok(snapshot));
    }

    public Task<OperationResult<string?>> SetAndVerifyAsync(string key, string? value, CancellationToken ct = default)
    {
        if (SetAndVerifyException is not null)
        {
            throw SetAndVerifyException;
        }

        Attempts.Add((key, value));

        if (SetAndVerifyHandler is not null)
        {
            var scripted = SetAndVerifyHandler(key, value);
            if (scripted.IsSuccess)
            {
                Writes.Add((key, value));
                _values[key] = value;
            }

            return Task.FromResult(scripted);
        }

        Writes.Add((key, value));
        _values[key] = value;
        return Task.FromResult(OperationResult<string?>.Ok(value));
    }
}

/// <summary>测试替身：IUiSettingsStore。内存版 ckan-ui.json，可编排保存失败与加载异常。</summary>
internal sealed class FakeUiSettingsStore : IUiSettingsStore
{
    /// <summary>当前设置（Load 返回它，Save 成功后更新它）。</summary>
    public UiSettings Current { get; private set; } = new();

    /// <summary>保存过的快照，按顺序。</summary>
    public List<UiSettings> Saved { get; } = new();

    /// <summary>保存成功后的变更通知（主题/密度宿主订阅）。</summary>
    public event Action<UiSettings>? Changed;

    /// <summary>Save 直接返回的结果（非空时优先）。</summary>
    public OperationResult<bool>? SaveResult { get; set; }

    /// <summary>Load 抛出的异常（验证异常收口）。</summary>
    public Exception? LoadException { get; set; }

    /// <summary>预置当前设置。</summary>
    public FakeUiSettingsStore With(UiSettings settings)
    {
        Current = settings;
        return this;
    }

    public UiSettings Load()
    {
        if (LoadException is not null)
        {
            throw LoadException;
        }

        return Current;
    }

    public OperationResult<bool> Save(UiSettings settings)
    {
        if (SaveResult is { IsSuccess: false })
        {
            return SaveResult;
        }

        Saved.Add(settings);
        Current = settings;
        Changed?.Invoke(settings);
        return OperationResult<bool>.Ok(true);
    }
}
