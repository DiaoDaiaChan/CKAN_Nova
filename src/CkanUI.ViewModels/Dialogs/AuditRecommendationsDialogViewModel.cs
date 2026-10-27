using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.ViewModels.Dialogs.Mods;
using CkanUI.ViewModels.Pages;

namespace CkanUI.ViewModels.Dialogs;

/// <summary>
/// F-36 推荐与建议审计对话框（Audit Recommendations / Suggestions Dialog，UI-17）：
/// 遍历已安装的所有模组，提取其 recommendations / suggests 关系；
/// 筛选出尚未安装且未在当前待变更集中的推荐项与建议项，按原发起模组分组呈现；
/// 允许用户多选后一键批量加入当前待变更集（ChangeRequest）。
/// </summary>
public sealed partial class AuditRecommendationsDialogViewModel : DialogViewModelBase
{
    private readonly IChangeRequestSink? _changeSink;
    private readonly ObservableCollection<AuditRecommendationGroupViewModel> _groups = [];
    private readonly ObservableCollection<AuditRecommendationItemViewModel> _items = [];

    /// <summary>用快照、待安装集合与可选的变更落点构造对话框。</summary>
    /// <param name="registry">当前注册表快照（用于读取已安装模组列表及其元数据事实）。</param>
    /// <param name="repository">当前仓库索引快照（用于查询推荐候选的名称、简介与版本）。</param>
    /// <param name="pendingInstallIdentifiers">当前已处于待安装状态的模组标识列表（过滤掉）。</param>
    /// <param name="changeSink">变更请求落点（点击主按钮时将勾选模组批量加入待变更集）。</param>
    public AuditRecommendationsDialogViewModel(
        RegistrySnapshot registry,
        RepositorySnapshot repository,
        IReadOnlyCollection<string>? pendingInstallIdentifiers = null,
        IChangeRequestSink? changeSink = null)
        : base("推荐与建议审计")
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(repository);

        _changeSink = changeSink;
        PrimaryButtonText = "加入待变更集";
        SecondaryButtonText = "关闭";

        ScanRecommendations(registry, repository, pendingInstallIdentifiers ?? Array.Empty<string>());
        UpdateProperties();
    }

    /// <summary>按原发起模组分组的推荐列表。</summary>
    public ReadOnlyObservableCollection<AuditRecommendationGroupViewModel> Groups => new(_groups);

    /// <summary>扁平候选推荐项列表（去重后的唯一候选模组）。</summary>
    public ReadOnlyObservableCollection<AuditRecommendationItemViewModel> Items => new(_items);

    /// <summary>候选总数（去重后的模组数）。</summary>
    public int TotalCount => _items.Count;

    /// <summary>是否有可供选择的候选。</summary>
    public bool HasCandidates => _items.Count > 0;

    /// <summary>可勾选的候选数（未安装且不在待安装队列中）。</summary>
    public int SelectableCount => _items.Count(i => i.CanSelect);

    /// <summary>当前已勾选的候选数。</summary>
    public int SelectedCount => _items.Count(i => i.IsSelected);

    /// <summary>是否勾选了任何候选。</summary>
    public bool HasSelection => SelectedCount > 0;

    /// <summary>主按钮（加入待变更集）可用性：有勾选时可用。</summary>
    public bool CanApply => HasSelection;

    /// <summary>已勾选的模组标识列表。</summary>
    public IReadOnlyList<string> SelectedIdentifiers =>
        _items.Where(i => i.IsSelected).Select(i => i.Identifier).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    /// <summary>应用所选后生成的变更请求列表（供外部或落点使用）。</summary>
    public IReadOnlyList<ChangeRequest> AppliedRequests { get; private set; } = Array.Empty<ChangeRequest>();

    /// <summary>顶部摘要文案。</summary>
    public string SummaryText => HasCandidates
        ? $"已扫描已安装模组，发现 {_groups.Count} 个模组提供了 {TotalCount} 项未安装的推荐/建议，已选 {SelectedCount} 项。"
        : "未发现任何新的推荐或建议模组。所有推荐与建议模组均已安装或在待安装列表中。";

    /// <summary>全选所有可选项。</summary>
    [RelayCommand]
    public void CheckAll()
    {
        foreach (var item in _items)
        {
            if (item.CanSelect)
            {
                item.IsSelected = true;
            }
        }

        foreach (var group in _groups)
        {
            group.Refresh();
        }

        UpdateProperties();
    }

    /// <summary>全部取消勾选。</summary>
    [RelayCommand]
    public void UncheckAll()
    {
        foreach (var item in _items)
        {
            item.IsSelected = false;
        }

        foreach (var group in _groups)
        {
            group.Refresh();
        }

        UpdateProperties();
    }

    /// <inheritdoc />
    protected override bool Validate()
    {
        if (!HasSelection)
        {
            ValidationError = "请先勾选需要加入待变更集的推荐模组。";
            return false;
        }

        ValidationError = null;
        return true;
    }

    /// <inheritdoc />
    protected override void OnPrimary()
    {
        var selected = _items.Where(i => i.IsSelected).DistinctBy(i => i.Identifier, StringComparer.OrdinalIgnoreCase).ToList();
        var requests = selected.Select(i => new ChangeRequest(
            i.Identifier,
            ChangeAction.Install,
            string.IsNullOrWhiteSpace(i.Version) ? null : i.Version)).ToArray();

        AppliedRequests = requests;
        _changeSink?.SetRequests(requests);
    }

    /// <inheritdoc />
    protected override void OnSecondary()
    {
        AppliedRequests = Array.Empty<ChangeRequest>();
    }

    private void ScanRecommendations(
        RegistrySnapshot registry,
        RepositorySnapshot repository,
        IReadOnlyCollection<string> pendingInstall)
    {
        var installedIds = new HashSet<string>(registry.Installed.Keys, StringComparer.OrdinalIgnoreCase);
        var pendingIds = new HashSet<string>(pendingInstall, StringComparer.OrdinalIgnoreCase);

        // 索引仓库中的可用行
        var repoDict = new Dictionary<string, ModRowData>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in repository.Available)
        {
            if (!string.IsNullOrWhiteSpace(row.Identifier) && !repoDict.ContainsKey(row.Identifier))
            {
                repoDict[row.Identifier] = row;
            }
        }

        // 维护唯一的 TargetIdentifier -> AuditRecommendationItemViewModel 映射，确保多处引用时勾选联动
        var itemMap = new Dictionary<string, AuditRecommendationItemViewModel>(StringComparer.OrdinalIgnoreCase);

        // 按照原发起模组遍历已安装模组
        foreach (var (sourceId, installedInfo) in registry.Installed.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
        {
            var facts = installedInfo.Facts;
            var groupItems = new List<AuditRecommendationItemViewModel>();

            void ProcessTarget(string target, RecommendationKind kind)
            {
                if (string.IsNullOrWhiteSpace(target)) return;

                // 筛选出尚未安装的推荐项与建议项，并且未在待安装列表中
                if (installedIds.Contains(target) || pendingIds.Contains(target))
                {
                    return;
                }

                if (!itemMap.TryGetValue(target, out var item))
                {
                    string name = target;
                    string version = string.Empty;
                    string? description = null;

                    if (repoDict.TryGetValue(target, out var row))
                    {
                        name = string.IsNullOrWhiteSpace(row.Name) ? target : row.Name;
                        version = row.LatestVersion ?? string.Empty;
                        description = row.Facts.Abstract ?? row.Facts.Description;
                    }

                    item = new AuditRecommendationItemViewModel(
                        target,
                        name,
                        version,
                        description,
                        kind,
                        [sourceId],
                        isInstalled: false,
                        isPendingInstall: false);

                    item.PropertyChanged += OnItemPropertyChanged;
                    itemMap[target] = item;
                    _items.Add(item);
                }
                else
                {
                    // 补充来源并提升推荐等级（推荐高于建议）
                    if (!itemMap[target].SourceModules.Contains(sourceId, StringComparer.OrdinalIgnoreCase))
                    {
                        var updatedSources = itemMap[target].SourceModules.Concat([sourceId]).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                        // 重新构建带有完整来源的 item 或利用新来源
                    }
                }

                if (!groupItems.Contains(item))
                {
                    groupItems.Add(item);
                }
            }

            if (facts.Recommends is { Count: > 0 } recs)
            {
                foreach (var target in recs)
                {
                    ProcessTarget(target, RecommendationKind.Recommended);
                }
            }

            if (facts.Suggests is { Count: > 0 } sugs)
            {
                foreach (var target in sugs)
                {
                    ProcessTarget(target, RecommendationKind.Suggested);
                }
            }

            if (groupItems.Count > 0)
            {
                string sourceName = sourceId;
                if (repoDict.TryGetValue(sourceId, out var sourceRow) && !string.IsNullOrWhiteSpace(sourceRow.Name))
                {
                    sourceName = sourceRow.Name;
                }

                var groupVm = new AuditRecommendationGroupViewModel(
                    sourceId,
                    sourceName,
                    groupItems.OrderBy(i => i.Kind).ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase));

                _groups.Add(groupVm);
            }
        }
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AuditRecommendationItemViewModel.IsSelected))
        {
            foreach (var group in _groups)
            {
                group.Refresh();
            }
            UpdateProperties();
        }
    }

    private void UpdateProperties()
    {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(CanApply));
        OnPropertyChanged(nameof(SelectableCount));
        OnPropertyChanged(nameof(SelectedIdentifiers));
        OnPropertyChanged(nameof(SummaryText));
    }
}
