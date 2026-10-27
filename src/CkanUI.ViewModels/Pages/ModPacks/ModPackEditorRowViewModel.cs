using System.Collections.Generic;
using CkanUI.Services.Abstractions.Dtos;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CkanUI.ViewModels.Pages.ModPacks;

/// <summary>编辑器里的一条文本行（作者 / 许可证这类"字符串列表"字段）。</summary>
public sealed partial class ModPackEditorRowViewModel : ObservableObject
{
    /// <summary>用一条文本构造行。</summary>
    /// <param name="value">文本内容。</param>
    /// <param name="fieldLabel">所属字段的中文名（用于界面提示与错误文案）。</param>
    public ModPackEditorRowViewModel(string value, string fieldLabel)
    {
        _value = value ?? string.Empty;
        FieldLabel = fieldLabel ?? string.Empty;
    }

    /// <summary>所属字段的中文名，例如"作者""许可证"。</summary>
    public string FieldLabel { get; }

    /// <summary>文本内容。</summary>
    [ObservableProperty]
    private string _value;

    /// <summary>展示用文本：空值显示为"—"。</summary>
    public string DisplayText => string.IsNullOrWhiteSpace(Value) ? "—" : Value;

    partial void OnValueChanged(string value) => OnPropertyChanged(nameof(DisplayText));

    /// <summary>拷成不可变文本，写盘时用。</summary>
    public string ToValue() => (Value ?? string.Empty).Trim();

    /// <summary>由文本列表批量建行。</summary>
    public static List<ModPackEditorRowViewModel> FromValues(IEnumerable<string>? values, string fieldLabel)
    {
        var rows = new List<ModPackEditorRowViewModel>();
        if (values is null)
        {
            return rows;
        }

        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                rows.Add(new ModPackEditorRowViewModel(value.Trim(), fieldLabel));
            }
        }

        return rows;
    }
}
