using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CkanUI.ViewModels.Pages.Settings;

/// <summary>
/// 单个设置项的视图模型（03 篇 §7 / 04 篇 §2）：同时持有「已落盘值」与「编辑中的值」，
/// 因此天然支持脏标记与「放弃修改」；行内校验错误与失败可重试标记也挂在它身上。
/// 本类**不做任何 IO**：保存动作由页面注入（<paramref name="saveHandler"/> 由
/// <c>SettingsPageViewModel</c> 提供），保证 VM 可 headless 单测且违反分层时编不过。
/// </summary>
public sealed partial class SettingItemViewModel : ObservableObject
{
    private readonly Func<SettingItemViewModel, Task>? _saveHandler;
    private readonly Func<string, string>? _toWire;
    private readonly Func<string?, string>? _fromWire;

    /// <summary>创建一个设置项。</summary>
    /// <param name="key">配置键（Core 的键名，或 ckan-ui.json 的字段键）。</param>
    /// <param name="displayName">界面显示名。</param>
    /// <param name="kind">编辑形态，决定视图控件与校验规则。</param>
    /// <param name="persistence">保存去向，决定走哪个服务。</param>
    /// <param name="group">所属分组。</param>
    /// <param name="description">行内说明（可为空）。</param>
    /// <param name="requiresRestart">是否「重启后生效」。</param>
    /// <param name="choices">单选候选。</param>
    /// <param name="min">整数下限。</param>
    /// <param name="max">整数上限。</param>
    /// <param name="toWire">编辑空间 → 线格式的自定义转换（如 MiB → 字节）；为空时按 kind 走默认转换。</param>
    /// <param name="fromWire">线格式 → 编辑空间的自定义转换；为空时按 kind 走默认转换。</param>
    /// <param name="saveHandler">页面提供的保存委托；为空时保存命令什么都不做（设计时预览用）。</param>
    public SettingItemViewModel(
        string key,
        string displayName,
        SettingItemKind kind,
        SettingPersistence persistence,
        SettingGroupKey group,
        string? description = null,
        bool requiresRestart = false,
        IReadOnlyList<SettingChoice>? choices = null,
        long? min = null,
        long? max = null,
        Func<string, string>? toWire = null,
        Func<string?, string>? fromWire = null,
        Func<SettingItemViewModel, Task>? saveHandler = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentException.ThrowIfNullOrEmpty(displayName);

        Key = key;
        DisplayName = displayName;
        Kind = kind;
        Persistence = persistence;
        Group = group;
        Description = description;
        RequiresRestart = requiresRestart;
        Choices = choices ?? Array.Empty<SettingChoice>();
        Min = min;
        Max = max;
        _toWire = toWire;
        _fromWire = fromWire;
        _saveHandler = saveHandler;

        SaveCommand = new AsyncRelayCommand(ExecuteSaveAsync, () => CanSave);
    }

    /// <summary>配置键。</summary>
    public string Key { get; }

    /// <summary>界面显示名。</summary>
    public string DisplayName { get; }

    /// <summary>行内说明。</summary>
    public string? Description { get; }

    /// <summary>是否有行内说明。</summary>
    public bool HasDescription => !string.IsNullOrEmpty(Description);

    /// <summary>编辑形态。</summary>
    public SettingItemKind Kind { get; }

    /// <summary>保存去向。</summary>
    public SettingPersistence Persistence { get; }

    /// <summary>所属分组。</summary>
    public SettingGroupKey Group { get; }

    /// <summary>是否重启后生效。</summary>
    public bool RequiresRestart { get; }

    /// <summary>右侧提示文案：重启后生效（外观类设置即时生效，不给提示）。</summary>
    public string? HintText => RequiresRestart ? "重启后生效" : null;

    /// <summary>是否有提示文案。</summary>
    public bool HasHint => HintText is not null;

    /// <summary>单选候选；实例下拉等场景要到加载后才知道候选，故可通知替换。</summary>
    [ObservableProperty]
    private IReadOnlyList<SettingChoice> _choices = Array.Empty<SettingChoice>();

    /// <summary>整数下限。</summary>
    public long? Min { get; }

    /// <summary>整数上限。</summary>
    public long? Max { get; }

    /// <summary>视图是否用文本框（含多行列表）。</summary>
    public bool IsTextKind => Kind is SettingItemKind.Text or SettingItemKind.Path or SettingItemKind.Integer or SettingItemKind.LineList;

    /// <summary>
    /// 视图是否用单行文本框：只有文本类、不是列表形态、也不是整数（整数走数值微调器）才走单行；
    /// 布尔/下拉项虽然也「不是多行」，但它们各自有开关与下拉框，不能同时露一个空文本框。
    /// </summary>
    public bool IsSingleLine => IsTextKind && !IsMultiline && !IsNumericKind;

    /// <summary>整数形态：用数值微调器编辑（自带范围校验与上下步进）。</summary>
    public bool IsNumericKind => Kind == SettingItemKind.Integer;

    /// <summary>数值微调器的双向绑定入口（整数形态专用；解析失败时退回下限或 0）。</summary>
    public double NumericValue
    {
        get => long.TryParse(EditValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : Min ?? 0;
        set => EditValue = ((long)Math.Round(value, MidpointRounding.AwayFromZero))
            .ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>数值微调器下限（没有声明下限时取 0）。</summary>
    public double NumericMinimum => Min ?? 0;

    /// <summary>数值微调器上限（没有声明上限时给一个足够大的值，避免微调器禁用）。</summary>
    public double NumericMaximum => Max ?? 999_999_999;

    /// <summary>视图是否用复选框。</summary>
    public bool IsBooleanKind => Kind == SettingItemKind.Boolean;

    /// <summary>视图是否用下拉框。</summary>
    public bool IsChoiceKind => Kind == SettingItemKind.Choice;

    /// <summary>文本框是否接受回车（列表形态需要）。</summary>
    public bool IsMultiline => Kind == SettingItemKind.LineList;

    /// <summary>编辑框最小高度：列表形态给三行。</summary>
    public double EditorMinHeight => IsMultiline ? 84 : 32;

    /// <summary>编辑中的值（字符串化；布尔为 "true"/"false"）。</summary>
    [ObservableProperty]
    private string _editValue = string.Empty;

    /// <summary>已落盘的值（编辑空间）。与 <see cref="EditValue"/> 不同即脏。</summary>
    [ObservableProperty]
    private string _savedValue = string.Empty;

    /// <summary>行内错误文案。</summary>
    [ObservableProperty]
    private string? _errorText;

    /// <summary>上次保存失败是否可重试（来自 <c>OperationResult.Retryable</c> 语义）。</summary>
    [ObservableProperty]
    private bool _isRetryable;

    /// <summary>是否正在保存。</summary>
    [ObservableProperty]
    private bool _isSaving;

    /// <summary>是否可编辑；依赖当前实例的项（如稳定版容差）在拿不到实例时置 false。</summary>
    [ObservableProperty]
    private bool _isEnabled = true;

    /// <summary>保存命令（由页面注入的委托执行）。</summary>
    public IAsyncRelayCommand SaveCommand { get; }

    /// <summary>
    /// 编辑停顿多久后自动落盘。设置页不再给每行一个「保存」按钮，改完就生效；
    /// 停顿一下再写，避免文本框逐字符触发磁盘写入。
    /// </summary>
    public TimeSpan AutoSaveDelay { get; set; } = TimeSpan.FromMilliseconds(450);

    /// <summary>是否启用自动保存（装载与批量回填期间需要临时关掉）。</summary>
    public bool AutoSave { get; set; } = true;

    private CancellationTokenSource? _autoSaveCts;

    /// <summary>行内辅助动作的文案（例如「打开目录」）；没有辅助动作时为 null。</summary>
    public string? AuxiliaryLabel { get; private set; }

    /// <summary>行内辅助动作的命令（例如打开缓存目录）；没有时为 null。</summary>
    public IAsyncRelayCommand? AuxiliaryCommand { get; private set; }

    /// <summary>是否有行内辅助动作。</summary>
    public bool HasAuxiliaryAction => AuxiliaryCommand is not null;

    /// <summary>是否有未保存修改。</summary>
    public bool IsDirty => !string.Equals(EditValue, SavedValue, StringComparison.Ordinal);

    /// <summary>是否有行内错误。</summary>
    public bool HasError => !string.IsNullOrEmpty(ErrorText);

    /// <summary>是否允许点保存：有修改、无错误、不在保存中。</summary>
    public bool CanSave => IsDirty && !HasError && !IsSaving;

    /// <summary>要写进配置的线格式值（列表形态序列化为 JSON 字符串数组，缓存上限换成字节）。</summary>
    public string WireValue => _toWire is not null ? _toWire(EditValue) : SettingWireFormat.ToWire(Kind, EditValue);

    /// <summary>布尔形态的双向绑定入口。</summary>
    public bool BooleanValue
    {
        get => string.Equals(EditValue, "true", StringComparison.Ordinal);
        set => EditValue = value ? "true" : "false";
    }

    /// <summary>单选形态的双向绑定入口；赋 null 视为用户清空（不改值）。</summary>
    public SettingChoice? SelectedChoice
    {
        get
        {
            foreach (var choice in Choices)
            {
                if (string.Equals(choice.Value, EditValue, StringComparison.Ordinal))
                {
                    return choice;
                }
            }

            return null;
        }

        set
        {
            if (value is not null)
            {
                EditValue = value.Value;
            }
        }
    }

    /// <summary>替换单选候选（实例列表启动后才拿得到，故候选要能在加载时更新）。</summary>
    /// <param name="choices">新的候选列表。</param>
    public void SetChoices(IReadOnlyList<SettingChoice> choices)
    {
        ArgumentNullException.ThrowIfNull(choices);

        Choices = choices;
        OnPropertyChanged(nameof(Choices));
        OnPropertyChanged(nameof(SelectedChoice));
    }

    /// <summary>挂上（或清掉）行内辅助动作。</summary>
    /// <param name="label">按钮文案。</param>
    /// <param name="command">命令；null 表示不显示按钮。</param>
    public void SetAuxiliaryAction(string? label, IAsyncRelayCommand? command)
    {
        AuxiliaryLabel = command is null ? null : label;
        AuxiliaryCommand = command;
        OnPropertyChanged(nameof(AuxiliaryLabel));
        OnPropertyChanged(nameof(AuxiliaryCommand));
        OnPropertyChanged(nameof(HasAuxiliaryAction));
    }

    /// <summary>按当前 kind 重新校验，返回是否不合法。</summary>
    public bool Validate()
    {
        var error = SettingValidator.Validate(Kind, EditValue, Choices, Min, Max);
        ErrorText = error;
        if (error is null)
        {
            IsRetryable = false;
        }

        return error is not null;
    }

    /// <summary>把服务读回的线格式值装进编辑空间，并清掉错误与脏标记。</summary>
    public void Load(string? wireValue) =>
        ApplyLoaded(_fromWire is not null ? _fromWire(wireValue) : SettingWireFormat.FromWire(Kind, wireValue));

    /// <summary>保存成功：把编辑值确认为已落盘值，清掉错误与可重试标记。</summary>
    public void MarkSaved()
    {
        SavedValue = EditValue;
        ErrorText = null;
        IsRetryable = false;
    }

    /// <summary>放弃未保存修改。</summary>
    public void Revert()
    {
        CancelAutoSave();
        EditValue = SavedValue;
        ErrorText = null;
        IsRetryable = false;
    }

    /// <summary>保存失败的行内呈现：可重试与永久失败在文案上必须能区分（01 篇 §8 的 Retryable 语义）。</summary>
    /// <param name="message">用户可读消息。</param>
    /// <param name="detail">技术细节。</param>
    /// <param name="retryable">是否可重试。</param>
    public void MarkFailed(string message, string? detail, bool retryable)
    {
        // 文案只说事实（消息 + 技术细节），「能不能重试」交给 FailureHint 表达，
        // 免得用户在错误消息里读到半句指令（01 篇 §8 的 Retryable 语义）。
        ErrorText = string.IsNullOrEmpty(detail) ? message : $"{message}（{detail}）";
        IsRetryable = retryable;
    }

    /// <summary>失败处理提示：区分「可重试」与「永久失败」（<c>OperationResult.Retryable</c> 的界面呈现）。</summary>
    public string? FailureHint => HasError
        ? IsRetryable
            ? "可重试：确认服务与文件权限后再点保存，或用「重试失败项」批量重试"
            : "该项不会被接受：请修改为合法值后重新保存"
        : null;

    /// <summary>是否显示失败处理提示。</summary>
    public bool HasFailureHint => HasError;

    /// <summary>页面加载时直接写入已落盘值（不经线格式转换，用于 UI 自有设置）。</summary>
    /// <param name="editValue">编辑空间的值。</param>
    public void ApplyLoaded(string editValue)
    {
        CancelAutoSave();
        SavedValue = editValue;
        ErrorText = null;
        IsRetryable = false;
        EditValue = editValue;
    }

    partial void OnEditValueChanged(string value)
    {
        Validate();
        RaiseDerived();
        ScheduleAutoSave();
    }

    partial void OnSavedValueChanged(string value) => RaiseDerived();

    partial void OnErrorTextChanged(string? value) => RaiseDerived();

    partial void OnIsSavingChanged(bool value) => RaiseDerived();

    partial void OnIsRetryableChanged(bool value)
    {
        OnPropertyChanged(nameof(HasHint));
        OnPropertyChanged(nameof(FailureHint));
    }

    private void RaiseDerived()
    {
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(HasFailureHint));
        OnPropertyChanged(nameof(FailureHint));
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(BooleanValue));
        OnPropertyChanged(nameof(SelectedChoice));
        OnPropertyChanged(nameof(NumericValue));
        SaveCommand.NotifyCanExecuteChanged();
    }

    private void CancelAutoSave()
    {
        _autoSaveCts?.Cancel();
        _autoSaveCts?.Dispose();
        _autoSaveCts = null;
    }

    private void ScheduleAutoSave()
    {
        if (!AutoSave || _saveHandler is null || !IsDirty || HasError)
        {
            return;
        }

        CancelAutoSave();
        var cts = new CancellationTokenSource();
        _autoSaveCts = cts;
        _ = AutoSaveAfterDelayAsync(cts);
    }

    private async Task AutoSaveAfterDelayAsync(CancellationTokenSource cts)
    {
        try
        {
            await Task.Delay(AutoSaveDelay, cts.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (cts.IsCancellationRequested || !CanSave)
        {
            return;
        }

        if (ReferenceEquals(_autoSaveCts, cts))
        {
            _autoSaveCts = null;
        }

        cts.Dispose();

        try
        {
            await SaveCommand.ExecuteAsync(null).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            // 保存委托内部通常已把失败写进 ErrorText；万一漏到这一层，也只在行内报错，不让异常逃逸到后台任务。
            MarkFailed("自动保存失败", ex.Message, retryable: true);
        }
    }

    private Task ExecuteSaveAsync() => _saveHandler?.Invoke(this) ?? Task.CompletedTask;
}
