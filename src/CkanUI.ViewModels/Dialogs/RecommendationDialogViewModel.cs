using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.ViewModels.Dialogs.Mods;

namespace CkanUI.ViewModels.Dialogs;

/// <summary>
/// F-34 推荐模组对话框：安装/变更前展示 recommends/suggests/supports 带来的额外模组，允许逐项勾选或整组全选。
/// 纯 VM，只吃 <see cref="RecommendationDialogRequest"/>；取消时 <see cref="Result"/> 保持 null（与上游 ChooseRecommendedMods 一致）。
/// </summary>
public sealed partial class RecommendationDialogViewModel : DialogViewModelBase
{
    private readonly List<RecommendationCandidateViewModel> _candidates = [];

    /// <summary>用请求数据构造对话框。</summary>
    public RecommendationDialogViewModel(RecommendationDialogRequest request)
        : base("推荐、建议与被支持模组")
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Candidates);

        Message = request.Message;
        PrimaryButtonText = "安装所选";
        SecondaryButtonText = "取消";
        _suppressRecommendations = request.SuppressRecommendations;

        foreach (var candidate in request.Candidates)
        {
            var item = new RecommendationCandidateViewModel(candidate)
            {
                IsSelected = !_suppressRecommendations && candidate.IsSelectedByDefault && !candidate.IsDlc && !candidate.IsConflicting,
            };
            item.PropertyChanged += OnCandidateChanged;
            _candidates.Add(item);
        }

        Groups =
        [
            BuildGroup(RecommendationKind.Recommended, "推荐（recommends）"),
            BuildGroup(RecommendationKind.Suggested, "建议（suggests）"),
            BuildGroup(RecommendationKind.SupportedBy, "支持所选的模组（supports）"),
        ];

        ConflictNotice = BuildConflictNotice();
        foreach (var group in Groups)
        {
            group.Refresh();
        }

        OnPropertyChanged(nameof(SelectedIdentifiers));
        OnPropertyChanged(nameof(SummaryText));
    }

    /// <summary>对话框顶部说明（通常来自上游 RecommendedDialogLabel）。</summary>
    public string Message { get; }

    /// <summary>三个分组（顺序固定：推荐、建议、被支持；空组由视图隐藏）。</summary>
    public ObservableCollection<RecommendationGroupViewModel> Groups { get; }

    /// <summary>候选总数。</summary>
    public int TotalCount => _candidates.Count;

    /// <summary>是否有候选；没有候选时主按钮不可用。</summary>
    public bool HasCandidates => _candidates.Count > 0;

    /// <summary>可勾选的候选数（排除 DLC 与冲突项）。</summary>
    public int SelectableCount => _candidates.Count(c => c.CanSelect);

    /// <summary>已勾选的候选数。</summary>
    public int SelectedCount => _candidates.Count(c => c.IsSelected);

    /// <summary>是否已勾选任何候选。</summary>
    public bool HasSelection => SelectedCount > 0;

    /// <summary>已勾选的模组标识（保持候选原始顺序）。</summary>
    public IReadOnlyList<string> SelectedIdentifiers =>
        _candidates.Where(c => c.IsSelected).Select(c => c.Identifier).ToArray();

    /// <summary>摘要文案，如 "共 5 项可勾选，已选 2 项。"。</summary>
    public string SummaryText => HasCandidates
        ? "共 " + SelectableCount + " 项可勾选，已选 " + SelectedCount + " 项。"
        : "没有可推荐的模组。";

    /// <summary>冲突提示（有冲突项时列出被禁用的行）。</summary>
    public string ConflictNotice { get; }

    /// <summary>是否显示冲突提示。</summary>
    public bool HasConflictNotice => ConflictNotice.Length > 0;

    /// <summary>"总是取消全选"：勾上后清空当前选择，并把该偏好带回给调用方持久化。</summary>
    [ObservableProperty]
    private bool _suppressRecommendations;

    /// <summary>确认结果；取消时为 null。</summary>
    public RecommendationSelection? Result { get; private set; }

    /// <summary>主按钮可用性（无候选时禁用）。</summary>
    public bool CanConfirm => HasCandidates;

    /// <summary>全部可勾选项全选（跳过 DLC 与冲突项）。</summary>
    [RelayCommand]
    private void CheckAll()
    {
        foreach (var item in _candidates)
        {
            item.IsSelected = true;
        }

        RefreshSelection();
    }

    /// <summary>全部取消勾选（仍可确认：等于"这次不要任何推荐"）。</summary>
    [RelayCommand]
    private void UncheckAll()
    {
        foreach (var item in _candidates)
        {
            item.IsSelected = false;
        }

        RefreshSelection();
    }

    /// <inheritdoc />
    protected override bool Validate()
    {
        if (!HasCandidates)
        {
            ValidationError = "没有可推荐的模组，无法继续。";
            return false;
        }

        ValidationError = null;
        return true;
    }

    /// <inheritdoc />
    protected override void OnPrimary() =>
        Result = new RecommendationSelection(SelectedIdentifiers, SuppressRecommendations);

    /// <inheritdoc />
    protected override void OnSecondary() => Result = null;

    partial void OnSuppressRecommendationsChanged(bool value)
    {
        if (value)
        {
            UncheckAll();
        }
    }

    private RecommendationGroupViewModel BuildGroup(RecommendationKind kind, string header) =>
        new(kind, header, _candidates.Where(c => c.Kind == kind));

    private string BuildConflictNotice()
    {
        var blocked = _candidates.Where(c => c.IsConflicting).ToArray();
        if (blocked.Length == 0)
        {
            return string.Empty;
        }

        return "以下模组与当前变更集冲突，已禁选："
            + string.Join("；", blocked.Select(c => c.DisplayName + "（" + c.BlockReason + "）"));
    }

    private void OnCandidateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RecommendationCandidateViewModel.IsSelected))
        {
            RefreshSelection();
        }
    }

    private void RefreshSelection()
    {
        foreach (var group in Groups)
        {
            group.Refresh();
        }

        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectableCount));
        OnPropertyChanged(nameof(SelectedIdentifiers));
        OnPropertyChanged(nameof(SummaryText));
    }
}
