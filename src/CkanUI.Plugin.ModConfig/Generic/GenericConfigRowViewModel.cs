using CommunityToolkit.Mvvm.ComponentModel;
using CkanUI.Services.Abstractions.ModConfig;

namespace CkanUI.Plugin.ModConfig;

/// <summary>
/// 通用配置面板里的一行：一行一个键值，直接改值（bool 给勾选，其余给文本框）。
/// </summary>
/// <remarks>
/// 行本身不写盘、也不知道文件在哪：脏值交给 <see cref="GenericConfigTabViewModel"/> 汇总，
/// 由它按行号把改动一次性交回服务层（页面的职责止于"收集草稿"）。
/// </remarks>
public sealed partial class GenericConfigRowViewModel : ObservableObject
{
    private readonly string _originalValue;

    public GenericConfigRowViewModel(GenericConfigRow row, bool canEdit)
    {
        LineNumber = row.LineNumber;
        Depth = row.Depth;
        NodePath = row.NodePath;
        Key = row.Key;
        Comment = row.Comment;
        _originalValue = row.Value;
        CanEdit = canEdit && row.Value.IndexOfAny(['\n', '\r']) < 0;

        var trimmed = row.Value.Trim();
        IsBoolean = string.Equals(trimmed, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, "false", StringComparison.OrdinalIgnoreCase);
        _isChecked = string.Equals(trimmed, "true", StringComparison.OrdinalIgnoreCase);
        _valueText = row.Value;
    }

    /// <summary>行号（写回时按它定位）。</summary>
    public int LineNumber { get; }

    /// <summary>节点深度（用于左侧缩进）。</summary>
    public int Depth { get; }

    /// <summary>所在节点路径（根层为空串）。</summary>
    public string NodePath { get; }

    /// <summary>键名。</summary>
    public string Key { get; }

    /// <summary>键上方最近的注释块。</summary>
    public string? Comment { get; }

    /// <summary>是否有注释要显示。</summary>
    public bool HasComment => !string.IsNullOrWhiteSpace(Comment);

    /// <summary>这一行能不能改（文件只读/有解析错误/值里有换行时为 false）。</summary>
    public bool CanEdit { get; }

    /// <summary>true/false 用勾选框，其他用文本框。</summary>
    public bool IsBoolean { get; }

    /// <summary>缩进（像素）：depth * 14。绑定到 Margin 不好写，这里给一个 Thickness 字符串不划算，用 double 给视图换算。</summary>
    public double Indent => Depth * 14d;

    /// <summary>节点换了的时候在行上方显示节点名（视图里用 IsBoundary 判断）。</summary>
    public bool IsBoundary { get; init; }

    /// <summary>边界行的标题（节点路径最后一段；根层给「文件根层」）。</summary>
    public string NodeHeaderText => NodePath.Length == 0
        ? "文件根层"
        : NodePath[(NodePath.LastIndexOf('/') + 1)..];

    /// <summary>文本框里的值。</summary>
    [ObservableProperty]
    private string _valueText;

    /// <summary>勾选框状态（仅 bool 行有意义）。</summary>
    [ObservableProperty]
    private bool _isChecked;

    /// <summary>这一行改过没有。</summary>
    public bool IsModified => IsBoolean
        ? !string.Equals(_originalValue.Trim(), IsChecked ? "true" : "false", StringComparison.OrdinalIgnoreCase)
        : !string.Equals(_originalValue, ValueText, StringComparison.Ordinal);

    /// <summary>当前要写回的值（bool 行按勾选状态序列化）。</summary>
    public string CurrentValue => IsBoolean ? (IsChecked ? "true" : "false") : ValueText;

    /// <summary>勾选变化时把值同步到文本框，让「改了没有」只有一处判据。</summary>
    partial void OnIsCheckedChanged(bool value)
    {
        if (!IsBoolean)
        {
            return;
        }

        ValueText = value ? "true" : "false";
    }

    partial void OnValueTextChanged(string value)
    {
        if (IsBoolean && !_isChecked && string.Equals(value.Trim(), "true", StringComparison.OrdinalIgnoreCase))
        {
            _isChecked = true;
            OnPropertyChanged(nameof(IsChecked));
        }
        else if (IsBoolean && _isChecked && string.Equals(value.Trim(), "false", StringComparison.OrdinalIgnoreCase))
        {
            _isChecked = false;
            OnPropertyChanged(nameof(IsChecked));
        }

        OnPropertyChanged(nameof(IsModified));
        OnPropertyChanged(nameof(CurrentValue));
    }

    /// <summary>把值还原成文件里的原文（撤销改动时用）。</summary>
    public void Revert()
    {
        ValueText = _originalValue;
        IsChecked = string.Equals(_originalValue.Trim(), "true", StringComparison.OrdinalIgnoreCase);
    }
}
