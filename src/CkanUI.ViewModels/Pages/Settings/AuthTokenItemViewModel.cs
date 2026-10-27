using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CkanUI.ViewModels.Pages.Settings;

/// <summary>
/// 一条认证令牌（12 篇 F-11）：主机名 + 令牌，界面上只显示脱敏预览，明文仅存在于 VM 内存里，
/// 不写日志、不进任何提示文案（16 篇 H-10）。增删由 <see cref="AuthTokenEditorViewModel"/> 注入的命令执行。
/// </summary>
public sealed partial class AuthTokenItemViewModel : ObservableObject
{
    /// <summary>创建一个令牌行。</summary>
    /// <param name="host">主机名（配置键的后半段）。</param>
    /// <param name="saveHandler">保存委托。</param>
    /// <param name="removeHandler">删除委托。</param>
    public AuthTokenItemViewModel(
        string host,
        Func<AuthTokenItemViewModel, Task>? saveHandler = null,
        Func<AuthTokenItemViewModel, Task>? removeHandler = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(host);

        Host = host;
        SaveCommand = new AsyncRelayCommand(ExecuteSaveAsync, () => CanSave);
        RemoveCommand = new AsyncRelayCommand(ExecuteRemoveAsync);
        _saveHandler = saveHandler;
        _removeHandler = removeHandler;
    }

    private readonly Func<AuthTokenItemViewModel, Task>? _saveHandler;
    private readonly Func<AuthTokenItemViewModel, Task>? _removeHandler;

    /// <summary>主机名。</summary>
    public string Host { get; }

    /// <summary>对应的配置键（<c>AuthToken:&lt;host&gt;</c>）。</summary>
    public string SettingKey => SettingsKeyNames.AuthToken(Host);

    /// <summary>编辑中的令牌明文（界面上用密码框输入，从不直接显示）。</summary>
    [ObservableProperty]
    private string _token = string.Empty;

    /// <summary>已落盘的令牌明文。</summary>
    [ObservableProperty]
    private string _savedToken = string.Empty;

    /// <summary>行内错误文案。</summary>
    [ObservableProperty]
    private string? _errorText;

    /// <summary>上次保存失败是否可重试。</summary>
    [ObservableProperty]
    private bool _isRetryable;

    /// <summary>是否正在保存。</summary>
    [ObservableProperty]
    private bool _isSaving;

    /// <summary>保存命令。</summary>
    public IAsyncRelayCommand SaveCommand { get; }

    /// <summary>删除命令。</summary>
    public IAsyncRelayCommand RemoveCommand { get; }

    /// <summary>脱敏预览（前 4 后 4）。</summary>
    public string MaskedPreview => $"脱敏预览：{AuthTokenMasking.Mask(Token)}";

    /// <summary>是否有未保存修改。</summary>
    public bool IsDirty => !string.Equals(Token, SavedToken, StringComparison.Ordinal);

    /// <summary>是否有行内错误。</summary>
    public bool HasError => !string.IsNullOrEmpty(ErrorText);

    /// <summary>是否允许点保存。</summary>
    public bool CanSave => IsDirty && !HasError && !IsSaving;

    /// <summary>按当前令牌值校验（令牌不能为空）。</summary>
    public bool Validate()
    {
        ErrorText = Token.Length == 0 ? "令牌不能为空" : null;
        if (ErrorText is null)
        {
            IsRetryable = false;
        }

        return ErrorText is not null;
    }

    /// <summary>把配置读回的明文令牌装进来（界面只看 <see cref="MaskedPreview"/>）。</summary>
    public void Load(string? token)
    {
        var value = token ?? string.Empty;
        SavedToken = value;
        ErrorText = null;
        IsRetryable = false;
        Token = value;
    }

    /// <summary>保存成功。</summary>
    public void MarkSaved()
    {
        SavedToken = Token;
        ErrorText = null;
        IsRetryable = false;
    }

    /// <summary>保存失败的行内呈现（可重试 / 永久失败文案不同）。</summary>
    public void MarkFailed(string message, string? detail, bool retryable)
    {
        ErrorText = string.IsNullOrEmpty(detail) ? message : $"{message}（{detail}）";
        IsRetryable = retryable;
    }

    /// <summary>失败处理提示：区分「可重试」与「永久失败」。</summary>
    public string? FailureHint => HasError
        ? IsRetryable
            ? "可重试：确认网络后再次保存"
            : "该项不会被接受：请检查主机名与令牌格式"
        : null;

    /// <summary>是否显示失败处理提示。</summary>
    public bool HasFailureHint => HasError;

    partial void OnTokenChanged(string value)
    {
        Validate();
        OnPropertyChanged(nameof(MaskedPreview));
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(CanSave));
        SaveCommand.NotifyCanExecuteChanged();
    }

    partial void OnSavedTokenChanged(string value)
    {
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(CanSave));
        SaveCommand.NotifyCanExecuteChanged();
    }

    partial void OnErrorTextChanged(string? value)
    {
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(HasFailureHint));
        OnPropertyChanged(nameof(FailureHint));
    }

    partial void OnIsRetryableChanged(bool value) => OnPropertyChanged(nameof(FailureHint));

    partial void OnIsSavingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanSave));
        SaveCommand.NotifyCanExecuteChanged();
    }

    private Task ExecuteSaveAsync() => _saveHandler?.Invoke(this) ?? Task.CompletedTask;

    private Task ExecuteRemoveAsync() => _removeHandler?.Invoke(this) ?? Task.CompletedTask;
}
