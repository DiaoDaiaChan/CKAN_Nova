using CommunityToolkit.Mvvm.ComponentModel;
using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.ViewModels.Dialogs.Mods;

/// <summary>
/// 推荐审计对话框中的候选条目（F-36）。
/// 包含推荐来源模组（Recommended by ...）、模组标识、名称、简介、版本与推荐类别。
/// </summary>
public sealed partial class AuditRecommendationItemViewModel : ObservableObject
{
    public AuditRecommendationItemViewModel(
        string identifier,
        string name,
        string version,
        string? description,
        RecommendationKind kind,
        IReadOnlyList<string> sourceModules,
        bool isInstalled = false,
        bool isPendingInstall = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
        ArgumentNullException.ThrowIfNull(sourceModules);

        Identifier = identifier;
        Name = string.IsNullOrWhiteSpace(name) ? identifier : name;
        Version = version ?? string.Empty;
        Description = description;
        Kind = kind;
        SourceModules = sourceModules;
        IsInstalled = isInstalled;
        IsPendingInstall = isPendingInstall;

        // 默认勾选：只要可勾选（未装且不在待安装列表中）就默认勾选
        _isSelected = CanSelect;
    }

    /// <summary>模组唯一标识。</summary>
    public string Identifier { get; }

    /// <summary>模组显示名称。</summary>
    public string Name { get; }

    /// <summary>模组可用版本。</summary>
    public string Version { get; }

    /// <summary>模组一句话描述/简介。</summary>
    public string? Description { get; }

    /// <summary>是否有简介。</summary>
    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);

    /// <summary>推荐关系类型（推荐 / 建议 / 支持）。</summary>
    public RecommendationKind Kind { get; }

    /// <summary>推荐来源模组标识列表（哪些已安装模组推荐了它）。</summary>
    public IReadOnlyList<string> SourceModules { get; }

    /// <summary>推荐来源文案（"由 ModA, ModB 推荐"）。</summary>
    public string SourceModulesText => SourceModules.Count > 0
        ? (Kind == RecommendationKind.Suggested ? "建议来源：" : "推荐来源：") + string.Join("、", SourceModules)
        : string.Empty;

    /// <summary>是否有关联来源可展示。</summary>
    public bool HasSourceModules => SourceModules.Count > 0;

    /// <summary>是否已安装。</summary>
    public bool IsInstalled { get; }

    /// <summary>是否已经在待安装列表中。</summary>
    public bool IsPendingInstall { get; }

    /// <summary>是否可勾选（未安装且未在待安装列表中）。</summary>
    public bool CanSelect => !IsInstalled && !IsPendingInstall;

    /// <summary>不可选原因。</summary>
    public string BlockReason
    {
        get
        {
            if (IsInstalled)
            {
                return "已安装";
            }

            if (IsPendingInstall)
            {
                return "已在待安装列表中";
            }

            return string.Empty;
        }
    }

    /// <summary>是否有不可选原因。</summary>
    public bool HasBlockReason => !CanSelect;

    /// <summary>是否已勾选。</summary>
    [ObservableProperty]
    private bool _isSelected;

    partial void OnIsSelectedChanged(bool value)
    {
        if (value && !CanSelect)
        {
            IsSelected = false;
        }
    }
}
