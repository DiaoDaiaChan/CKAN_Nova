using System.Globalization;
using CkanUI.Services.Abstractions.KspConfig;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CkanUI.Plugin.KspConfig;

/// <summary>
/// 单个 KSP 设置项的视图模型，负责类型安全双向绑定、脏状态追踪与单项重置。
/// </summary>
public sealed partial class KspSettingItemViewModel : ObservableObject
{
    private readonly KspSettingItem _model;
    private readonly Action? _onChanged;
    private string? _localizedName;

    public KspSettingItemViewModel(KspSettingItem model, Action? onChanged = null)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _onChanged = onChanged;
        _value = model.Value;
        OriginalValue = model.OriginalValue;
    }

    public KspSettingItem Model => _model;

    public string Key => _model.Key;

    public string DisplayName => _localizedName ?? _model.DisplayName;

    /// <summary>
    /// 用语言包里的人工译名覆盖 settings.cfg 的原始键名；传 null 退回模型自带的名字
    /// （精选项是官方词条译文，其余是友好化后的键名）。搜索与显示都读 <see cref="DisplayName"/>。
    /// </summary>
    public void ApplyLocalizedName(string? localized)
    {
        if (string.Equals(_localizedName, localized, StringComparison.Ordinal))
        {
            return;
        }

        _localizedName = localized;
        OnPropertyChanged(nameof(DisplayName));
    }

    public string? Description => _model.Description;

    public string? LocKey => _model.LocKey;

    public KspSettingCategory Category => _model.Category;

    public KspSettingType SettingType => _model.SettingType;

    public IReadOnlyList<KspOptionEntry>? Options => _model.Options;

    public double MinValue => _model.MinValue ?? 0;

    public double MaxValue => _model.MaxValue ?? 100;

    public double Step => _model.Step ?? 1;

    public int Priority => _model.Priority;

    public bool IsCommon => _model.Priority > 0;

    public bool IsBoolean => SettingType == KspSettingType.Boolean;

    public bool IsFloat => SettingType == KspSettingType.Float;

    public bool IsInteger => SettingType == KspSettingType.Integer;

    public bool IsPreset => SettingType == KspSettingType.Preset;

    public bool IsText => SettingType == KspSettingType.Text || SettingType == KspSettingType.Resolution;

    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);

    public bool HasLocKey => !string.IsNullOrWhiteSpace(LocKey);

    public string OriginalValue { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsModified))]
    [NotifyPropertyChangedFor(nameof(BooleanValue))]
    [NotifyPropertyChangedFor(nameof(DoubleValue))]
    [NotifyPropertyChangedFor(nameof(IntegerValue))]
    [NotifyPropertyChangedFor(nameof(TextValue))]
    [NotifyPropertyChangedFor(nameof(SelectedOption))]
    private string _value;

    partial void OnValueChanged(string value)
    {
        _model.Value = value;
        _onChanged?.Invoke();
    }

    public bool IsModified => !string.Equals(Value, OriginalValue, StringComparison.Ordinal);

    /// <summary>
    /// 布尔值绑定属性（适配 CheckBox / ToggleSwitch）。
    /// </summary>
    public bool BooleanValue
    {
        get => bool.TryParse(Value, out var b) && b;
        set
        {
            var strVal = value ? "True" : "False";
            if (!string.Equals(Value, strVal, StringComparison.OrdinalIgnoreCase))
            {
                Value = strVal;
            }
        }
    }

    /// <summary>
    /// 数值绑定属性（适配 Slider / NumericUpDown）。
    /// </summary>
    public double DoubleValue
    {
        get => double.TryParse(Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0;
        set
        {
            var strVal = SettingType == KspSettingType.Integer
                ? ((long)Math.Round(value)).ToString(CultureInfo.InvariantCulture)
                : value.ToString("0.###", CultureInfo.InvariantCulture);

            if (!string.Equals(Value, strVal, StringComparison.Ordinal))
            {
                Value = strVal;
            }
        }
    }

    /// <summary>
    /// 预设选项下拉绑定属性（适配 ComboBox）。
    /// </summary>
    public KspOptionEntry? SelectedOption
    {
        get => Options?.FirstOrDefault(o => string.Equals(o.Value, Value, StringComparison.OrdinalIgnoreCase));
        set
        {
            if (value is not null && !string.Equals(Value, value.Value, StringComparison.OrdinalIgnoreCase))
            {
                Value = value.Value;
            }
        }
    }

    /// <summary>
    /// 整数数值输入属性（适配 TextBox）。
    /// </summary>
    public int IntegerValue
    {
        get => int.TryParse(Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)
            ? i
            : (int)Math.Round(DoubleValue);
        set
        {
            var strVal = value.ToString(CultureInfo.InvariantCulture);
            if (!string.Equals(Value, strVal, StringComparison.Ordinal))
            {
                Value = strVal;
            }
        }
    }

    /// <summary>
    /// 纯文本绑定属性（适配 TextBox）。
    /// </summary>
    public string TextValue
    {
        get => Value;
        set
        {
            var strVal = value ?? string.Empty;
            if (!string.Equals(Value, strVal, StringComparison.Ordinal))
            {
                Value = strVal;
            }
        }
    }

    /// <summary>
    /// 重置该项为读盘时的原始值。
    /// </summary>
    [RelayCommand]
    public void Reset()
    {
        Value = OriginalValue;
    }
}
