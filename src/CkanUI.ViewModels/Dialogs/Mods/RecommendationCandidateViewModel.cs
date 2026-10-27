using CommunityToolkit.Mvvm.ComponentModel;
using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.ViewModels.Dialogs.Mods;

/// <summary>
/// 推荐对话框里的一行候选（F-34）。DLC 与已知冲突项不可勾选，
/// 原因写在 <see cref="BlockReason"/> 里（对齐上游 ChooseRecommendedMods 的 MarkConflicts）。
/// </summary>
public sealed partial class RecommendationCandidateViewModel : ObservableObject
{
    /// <summary>用 DTO 构造一行；初始勾选状态由 <see cref="RecommendationCandidate.IsSelectedByDefault"/> 决定。</summary>
    public RecommendationCandidateViewModel(RecommendationCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        Candidate = candidate;
        _isSelected = CanSelect && candidate.IsSelectedByDefault;
    }

    /// <summary>原始候选数据。</summary>
    public RecommendationCandidate Candidate { get; }

    /// <summary>模组标识。</summary>
    public string Identifier => Candidate.Identifier;

    /// <summary>模组显示名。</summary>
    public string Name => Candidate.Name;

    /// <summary>分组来源。</summary>
    public RecommendationKind Kind => Candidate.Kind;

    /// <summary>一句话描述。</summary>
    public string? Description => Candidate.Description;

    /// <summary>是否带描述。</summary>
    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);

    /// <summary>关联模组标识（"与哪些模组相关"）。</summary>
    public IReadOnlyList<string> RelatedIdentifiers => Candidate.RelatedIdentifiers ?? Array.Empty<string>();

    /// <summary>是否有关联模组可展示。</summary>
    public bool HasRelated => RelatedIdentifiers.Count > 0;

    /// <summary>关联模组的展示文案。</summary>
    public string RelatedText => HasRelated ? "关联：" + string.Join("、", RelatedIdentifiers) : string.Empty;

    /// <summary>是否为 DLC（DLC 不能在推荐对话框里勾选）。</summary>
    public bool IsDlc => Candidate.IsDlc;

    /// <summary>是否已安装。</summary>
    public bool IsInstalled => Candidate.IsInstalled;

    /// <summary>是否与当前变更集冲突。</summary>
    public bool IsConflicting => Candidate.IsConflicting;

    /// <summary>是否允许勾选：DLC 与冲突项一律禁用。</summary>
    public bool CanSelect => !IsDlc && !IsConflicting;

    /// <summary>显示名（DLC 加后缀便于识别）。</summary>
    public string DisplayName => IsDlc ? Name + "（DLC）" : Name;

    /// <summary>不可勾选的原因；可勾选时为空串。</summary>
    public string BlockReason
    {
        get
        {
            if (IsDlc)
            {
                return "DLC 不在此处勾选。";
            }

            if (IsConflicting)
            {
                return string.IsNullOrWhiteSpace(Candidate.ConflictReason)
                    ? "与当前变更集冲突。"
                    : Candidate.ConflictReason!;
            }

            return string.Empty;
        }
    }

    /// <summary>是否显示不可勾选原因。</summary>
    public bool HasBlockReason => !CanSelect;

    /// <summary>是否已勾选。</summary>
    [ObservableProperty]
    private bool _isSelected;

    partial void OnIsSelectedChanged(bool value)
    {
        // 禁用的行不接受勾选：外部（全选/单选）把它置 true 时立刻回滚。
        if (value && !CanSelect)
        {
            IsSelected = false;
        }
    }
}
