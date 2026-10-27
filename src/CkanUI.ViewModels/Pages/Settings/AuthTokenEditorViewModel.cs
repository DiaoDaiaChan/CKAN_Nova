using System.ComponentModel;
using CkanUI.Services.Abstractions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CkanUI.ViewModels.Pages.Settings;

/// <summary>
/// 认证令牌组（12 篇 F-11 / 06 篇 UI-34 / VM-03）：列出 <c>AuthToken:&lt;host&gt;</c> 键，
/// 支持新增、改值与删除；界面只见脱敏预览。写路径统一走 <c>ISettingsService.SetAndVerifyAsync</c>
/// （改一项写一项 + 读回校验，02 篇 §3），值写 null 即删除该令牌（Core 语义）。
/// </summary>
public sealed partial class AuthTokenEditorViewModel : ObservableObject
{
    private readonly ISettingsService _settings;

    /// <summary>创建令牌组编辑器。</summary>
    /// <param name="settings">设置服务（增删改的唯一出口）。</param>
    public AuthTokenEditorViewModel(ISettingsService settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        AddCommand = new AsyncRelayCommand(AddAsync, () => CanAdd);
        RetryFailedCommand = new AsyncRelayCommand(RetryFailedAsync);
    }

    /// <summary>当前令牌行（整体替换，不做增量集合通知，team-brief §4 规则 7）。</summary>
    public IReadOnlyList<AuthTokenItemViewModel> Tokens { get; private set; } = Array.Empty<AuthTokenItemViewModel>();

    /// <summary>新增时的主机名输入。</summary>
    [ObservableProperty]
    private string _newHost = string.Empty;

    /// <summary>新增时的令牌输入（密码框，界面不明文显示）。</summary>
    [ObservableProperty]
    private string _newToken = string.Empty;

    /// <summary>新增区的行内错误。</summary>
    [ObservableProperty]
    private string? _errorText;

    /// <summary>是否正在与服务交互。</summary>
    [ObservableProperty]
    private bool _isBusy;

    /// <summary>新增命令。</summary>
    public IAsyncRelayCommand AddCommand { get; }

    /// <summary>重试所有可重试的失败项。</summary>
    public IAsyncRelayCommand RetryFailedCommand { get; }

    /// <summary>是否有行内错误。</summary>
    public bool HasError => !string.IsNullOrEmpty(ErrorText);

    /// <summary>是否还没有任何令牌。</summary>
    public bool IsEmpty => Tokens.Count == 0;

    /// <summary>是否有未保存的改动（含新增区里未提交的输入）。</summary>
    public bool HasChanges
    {
        get
        {
            if (NewHost.Length > 0 || NewToken.Length > 0)
            {
                return true;
            }

            foreach (var token in Tokens)
            {
                if (token.IsDirty)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>是否有可重试的失败项。</summary>
    public bool HasRetryableFailures
    {
        get
        {
            foreach (var token in Tokens)
            {
                if (token.IsRetryable)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>当前输入能否新增：主机名与令牌都非空、且主机名尚无令牌。</summary>
    public bool CanAdd
    {
        get
        {
            var host = NewHost.Trim();
            return host.Length > 0 && NewToken.Length > 0 && !IsBusy && FindToken(host) is null;
        }
    }

    /// <summary>从 <c>GetAllAsync</c> 的结果投影出令牌行（键前缀 <c>AuthToken:</c>）。</summary>
    /// <param name="all">全部设置项的键值快照。</param>
    public void LoadFrom(IReadOnlyDictionary<string, string?>? all)
    {
        var rows = new List<AuthTokenItemViewModel>();
        if (all is not null)
        {
            foreach (var pair in all)
            {
                if (!pair.Key.StartsWith(SettingsKeyNames.AuthTokenPrefix, StringComparison.Ordinal))
                {
                    continue;
                }

                var host = pair.Key[SettingsKeyNames.AuthTokenPrefix.Length..];
                if (host.Length == 0)
                {
                    continue;
                }

                var item = CreateItem(host);
                item.Load(pair.Value);
                rows.Add(item);
            }
        }

        rows.Sort((left, right) => string.Compare(left.Host, right.Host, StringComparison.Ordinal));
        SetTokens(rows);
        NewHost = string.Empty;
        NewToken = string.Empty;
        ErrorText = null;
    }

    /// <summary>新增一条令牌：主机名重复或为空都在行内报错，不触达服务。</summary>
    public async Task AddAsync()
    {
        var host = NewHost.Trim();
        if (host.Length == 0)
        {
            ErrorText = "请输入主机名";
            return;
        }

        if (NewToken.Length == 0)
        {
            ErrorText = "请输入令牌";
            return;
        }

        if (FindToken(host) is not null)
        {
            ErrorText = $"主机 {host} 已有令牌，请直接修改或先删除";
            return;
        }

        IsBusy = true;
        try
        {
            var result = await _settings.SetAndVerifyAsync(SettingsKeyNames.AuthToken(host), NewToken).ConfigureAwait(true);
            if (!result.IsSuccess)
            {
                ErrorText = ComposeFailure(result.Message ?? "新增令牌失败", result.Detail, result.Retryable);
                return;
            }

            var item = CreateItem(host);
            item.Load(result.Value ?? NewToken);
            var rows = new List<AuthTokenItemViewModel>(Tokens) { item };
            rows.Sort((left, right) => string.Compare(left.Host, right.Host, StringComparison.Ordinal));
            SetTokens(rows);
            NewHost = string.Empty;
            NewToken = string.Empty;
            ErrorText = null;
        }
        catch (Exception ex)
        {
            // 服务契约不抛异常；真抛了也不能穿透（01 篇 §8）。
            ErrorText = ComposeFailure("新增令牌失败", ex.Message, retryable: true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>保存单条令牌。</summary>
    /// <param name="item">要保存的令牌行。</param>
    public async Task SaveAsync(AuthTokenItemViewModel? item)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (item.Validate() || !item.IsDirty)
        {
            return;
        }

        item.IsSaving = true;
        try
        {
            var result = await _settings.SetAndVerifyAsync(item.SettingKey, item.Token).ConfigureAwait(true);
            if (result.IsSuccess)
            {
                item.Load(result.Value ?? item.Token);
                item.MarkSaved();
            }
            else
            {
                item.MarkFailed(result.Message ?? "保存令牌失败", result.Detail, result.Retryable);
            }
        }
        catch (Exception ex)
        {
            item.MarkFailed("保存令牌失败", ex.Message, retryable: true);
        }
        finally
        {
            item.IsSaving = false;
        }
    }

    /// <summary>删除单条令牌（写 null 即删除，02 篇 §3）。</summary>
    /// <param name="item">要删除的令牌行。</param>
    public async Task RemoveAsync(AuthTokenItemViewModel? item)
    {
        ArgumentNullException.ThrowIfNull(item);

        item.IsSaving = true;
        try
        {
            var result = await _settings.SetAndVerifyAsync(item.SettingKey, null).ConfigureAwait(true);
            if (!result.IsSuccess)
            {
                item.MarkFailed(result.Message ?? "删除令牌失败", result.Detail, result.Retryable);
                return;
            }

            var rows = new List<AuthTokenItemViewModel>(Tokens);
            rows.Remove(item);
            SetTokens(rows);
        }
        catch (Exception ex)
        {
            item.MarkFailed("删除令牌失败", ex.Message, retryable: true);
        }
        finally
        {
            item.IsSaving = false;
        }
    }

    /// <summary>重试所有可重试的失败项。</summary>
    public async Task RetryFailedAsync()
    {
        foreach (var item in Tokens.ToArray())
        {
            if (item.IsRetryable)
            {
                await SaveAsync(item).ConfigureAwait(true);
            }
        }
    }

    /// <summary>放弃未保存的改动手（含新增区输入）。</summary>
    public void Discard()
    {
        foreach (var item in Tokens)
        {
            item.Load(item.SavedToken);
        }

        NewHost = string.Empty;
        NewToken = string.Empty;
        ErrorText = null;
    }

    /// <summary>令牌行查找（按主机名，序数比较）。</summary>
    /// <param name="host">主机名。</param>
    public AuthTokenItemViewModel? FindToken(string host)
    {
        foreach (var item in Tokens)
        {
            if (string.Equals(item.Host, host, StringComparison.Ordinal))
            {
                return item;
            }
        }

        return null;
    }

    partial void OnNewHostChanged(string value)
    {
        RaiseDerived();
        AddCommand.NotifyCanExecuteChanged();
    }

    partial void OnNewTokenChanged(string value) => RaiseDerived();

    partial void OnIsBusyChanged(bool value) => AddCommand.NotifyCanExecuteChanged();

    partial void OnErrorTextChanged(string? value) => OnPropertyChanged(nameof(HasError));

    private AuthTokenItemViewModel CreateItem(string host) => new(host, SaveAsync, RemoveAsync);

    private void SetTokens(List<AuthTokenItemViewModel> rows)
    {
        foreach (var previous in Tokens)
        {
            previous.PropertyChanged -= OnTokenPropertyChanged;
        }

        foreach (var next in rows)
        {
            next.PropertyChanged += OnTokenPropertyChanged;
        }

        Tokens = rows;
        OnPropertyChanged(nameof(Tokens));
        OnPropertyChanged(nameof(IsEmpty));
        RaiseDerived();
    }

    private void OnTokenPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AuthTokenItemViewModel.IsDirty) or nameof(AuthTokenItemViewModel.IsRetryable) or nameof(AuthTokenItemViewModel.ErrorText))
        {
            RaiseDerived();
        }
    }

    private void RaiseDerived()
    {
        OnPropertyChanged(nameof(HasChanges));
        OnPropertyChanged(nameof(HasRetryableFailures));
        OnPropertyChanged(nameof(CanAdd));
    }

    private static string ComposeFailure(string message, string? detail, bool retryable) =>
        string.IsNullOrEmpty(detail) ? message : $"{message}（{detail}）";
}
