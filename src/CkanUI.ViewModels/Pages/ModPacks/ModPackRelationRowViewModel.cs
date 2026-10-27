using System;
using System.Collections.Generic;
using System.Linq;
using CkanUI.Services.Abstractions.Dtos;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CkanUI.ViewModels.Pages.ModPacks;

/// <summary>
/// 关系表的一行。语义模式决定哪一组输入生效：
/// 单选读 <see cref="Name"/> + 版本上下限，多选 / 任意读 <see cref="CandidatesText"/>。
/// </summary>
public sealed partial class ModPackRelationRowViewModel : ObservableObject
{
    /// <summary>用一条关系构造行。</summary>
    /// <param name="relation">服务层 / DTO 里的关系。</param>
    public ModPackRelationRowViewModel(ModPackRelation relation)
    {
        ArgumentNullException.ThrowIfNull(relation);
        _relationType = string.IsNullOrWhiteSpace(relation.RelationType) ? ModPackRelationTypes.Depends : relation.RelationType;
        _mode = string.IsNullOrWhiteSpace(relation.Mode) ? ModPackRelationModes.Single : relation.Mode;
        _name = relation.Name ?? string.Empty;
        _minVersion = relation.MinVersion ?? string.Empty;
        _maxVersion = relation.MaxVersion ?? string.Empty;
        _candidatesText = relation.Candidates is null ? string.Empty : string.Join(", ", relation.Candidates);
    }

    /// <summary>空行构造（"添加关系"用）。</summary>
    public static ModPackRelationRowViewModel CreateEmpty(string relationType = ModPackRelationTypes.Depends)
        => new(new ModPackRelation { RelationType = relationType, Mode = ModPackRelationModes.Single });

    /// <summary>关系类型（<c>depends</c> / <c>recommends</c> / <c>suggests</c> / <c>conflicts</c> / <c>supports</c>）。</summary>
    [ObservableProperty]
    private string _relationType;

    /// <summary>语义模式。<c>single</c> 单选 / <c>multi</c> 多选 / <c>anyOf</c> 任意一个。</summary>
    [ObservableProperty]
    private string _mode;

    /// <summary>单选模式下的模组标识。</summary>
    [ObservableProperty]
    private string _name;

    /// <summary>版本下限（可空）。</summary>
    [ObservableProperty]
    private string _minVersion;

    /// <summary>版本上限（可空）。</summary>
    [ObservableProperty]
    private string _maxVersion;

    /// <summary>多选 / 任意模式下的候选标识，逗号分隔。</summary>
    [ObservableProperty]
    private string _candidatesText;

    /// <summary>是否单选模式（决定界面显示哪一组输入）。</summary>
    public bool IsSingleMode => string.Equals(Mode, ModPackRelationModes.Single, StringComparison.Ordinal);

    /// <summary>是否候选模式（多选或任意一个）。</summary>
    public bool IsCandidateMode => !IsSingleMode;

    /// <summary>关系类型的中文标签。</summary>
    public string RelationTypeLabel => ModPackEditorText.RelationTypeLabel(RelationType);

    /// <summary>语义模式的中文标签。</summary>
    public string ModeLabel => ModPackEditorText.ModeLabel(Mode);

    /// <summary>表格里的"目标"简述。</summary>
    public string TargetText => IsSingleMode
        ? (string.IsNullOrWhiteSpace(Name) ? "（未填写）" : Name.Trim())
        : (string.IsNullOrWhiteSpace(CandidatesText) ? "（未填写）" : CandidatesText.Trim());

    /// <summary>表格里的"版本"简述。</summary>
    public string VersionText
    {
        get
        {
            if (!IsSingleMode)
            {
                return "—";
            }

            var min = (MinVersion ?? string.Empty).Trim();
            var max = (MaxVersion ?? string.Empty).Trim();
            if (min.Length == 0 && max.Length == 0)
            {
                return "任意";
            }

            if (min.Length > 0 && max.Length > 0)
            {
                return min + " ~ " + max;
            }

            return min.Length > 0 ? "≥ " + min : "≤ " + max;
        }
    }

    /// <summary>候选标识（已按逗号 / 分号 / 空白切分并去空）。</summary>
    public IReadOnlyList<string> Candidates => (CandidatesText ?? string.Empty)
        .Split(new[] { ',', '，', ';', '；', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Distinct(StringComparer.Ordinal)
        .ToList();

    /// <summary>投影回 DTO；候选模式只保留候选列表，单选模式只保留名字与版本。</summary>
    public ModPackRelation ToRelation() => new()
    {
        RelationType = (RelationType ?? string.Empty).Trim(),
        Mode = (Mode ?? string.Empty).Trim(),
        Name = IsSingleMode ? (Name ?? string.Empty).Trim() : string.Empty,
        MinVersion = IsSingleMode ? (MinVersion ?? string.Empty).Trim() : string.Empty,
        MaxVersion = IsSingleMode ? (MaxVersion ?? string.Empty).Trim() : string.Empty,
        Candidates = IsSingleMode ? Array.Empty<string>() : Candidates,
    };

    partial void OnModeChanged(string value)
    {
        OnPropertyChanged(nameof(IsSingleMode));
        OnPropertyChanged(nameof(IsCandidateMode));
        OnPropertyChanged(nameof(ModeLabel));
        OnPropertyChanged(nameof(TargetText));
        OnPropertyChanged(nameof(VersionText));
    }

    partial void OnRelationTypeChanged(string value) => OnPropertyChanged(nameof(RelationTypeLabel));

    partial void OnNameChanged(string value) => OnPropertyChanged(nameof(TargetText));

    partial void OnCandidatesTextChanged(string value) => OnPropertyChanged(nameof(TargetText));

    partial void OnMinVersionChanged(string value) => OnPropertyChanged(nameof(VersionText));

    partial void OnMaxVersionChanged(string value) => OnPropertyChanged(nameof(VersionText));
}
