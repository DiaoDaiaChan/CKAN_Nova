using CommunityToolkit.Mvvm.ComponentModel;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.ViewModels.Dialogs;

/// <summary>
/// 详情面板视图模型（04 篇 §2 / 03 篇 §3.1 / 06 篇 UI-03、VM-04）。
/// 依赖只有 <see cref="IRepositoryService"/> 与 <see cref="IRegistryService"/>：VM 永不持有 Core 的活对象，
/// 只按 <c>Identifier</c> 回查快照；快照整体替换（<c>Updated</c>/<c>Changed</c>）时自动失效重取（04 篇 §6）。
/// 富字段（描述/依赖/推荐/冲突/许可证/主页）由可选的 <see cref="DetailsProvider"/> 提供，
/// 未接线时用快照已有字段 + 空状态文案，保证 headless 可测、不阻塞 M1。
/// </summary>
public sealed partial class ModDetailsViewModel : ObservableObject, IActivatable
{
    private readonly IRepositoryService _repository;
    private readonly IRegistryService _registry;
    private string? _identifier;
    private ModRowData? _row;
    private InstalledModuleInfo? _installed;
    private ModDetailsSnapshot? _details;

    /// <summary>创建详情面板 VM。</summary>
    public ModDetailsViewModel(IRepositoryService repository, IRegistryService registry)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(registry);

        _repository = repository;
        _registry = registry;
    }

    /// <summary>
    /// 富字段提供者（按 Identifier 返回详情投影）。App 侧用 Core 的 <c>CkanModule</c> 适配；
    /// 为 null 时详情面板只显示快照里已有的字段。用属性而不是构造参数，避免要求容器注册新契约。
    /// </summary>
    public Func<string, ModDetailsSnapshot?>? DetailsProvider { get; set; }

    /// <summary>是否已 <see cref="Activate"/>（幂等）。</summary>
    public bool IsActive { get; private set; }

    /// <summary>当前选中的模组标识（未选中为 null）。</summary>
    public string? Identifier => _identifier;

    /// <summary>是否有选中项（详情面板可折叠，03 篇 §3.1）。</summary>
    public bool HasSelection => _identifier is not null;

    /// <summary>是否处于空状态。</summary>
    public bool IsEmpty => !HasSelection;

    /// <summary>空状态文案（03 篇 §10 每页必须有空状态）。</summary>
    public string EmptyText => "未选中模组";

    /// <summary>显示名。</summary>
    public string Name => _details?.Name ?? _row?.Name ?? string.Empty;

    /// <summary>作者（缺失时显示占位）。</summary>
    public string AuthorText => FirstNonEmpty(_details?.Author, _row?.Author) ?? "未知作者";

    /// <summary>一句话摘要（Core 的 abstract）。</summary>
    public string AbstractText => _details?.Abstract ?? string.Empty;

    /// <summary>是否有摘要。</summary>
    public bool HasAbstract => !string.IsNullOrWhiteSpace(AbstractText);

    /// <summary>描述（Markdown 原文，渲染交给视图；缺失时占位）。</summary>
    public string DescriptionText =>
        string.IsNullOrWhiteSpace(_details?.Description) ? "暂无描述" : _details!.Description!;

    /// <summary>已安装版本（未安装时显示"未安装"）。</summary>
    public string InstalledVersionText =>
        FirstNonEmpty(_installed?.Version, _row?.InstalledVersion) ?? "未安装";

    /// <summary>仓库里的最新版本。</summary>
    public string LatestVersionText => ModDetailsFormatter.FormatVersion(_row?.LatestVersion ?? _details?.LatestVersion);

    /// <summary>体积（格式化后的文本）。</summary>
    public string SizeText => ModDetailsFormatter.FormatSize(_details?.SizeBytes ?? _row?.SizeBytes);

    /// <summary>许可证（缺失时占位）。</summary>
    public string LicenseText =>
        string.IsNullOrWhiteSpace(_details?.License) ? "未标注许可证" : _details!.License!;

    /// <summary>主页链接（空表示没有主页）。</summary>
    public string HomepageText => _details?.Homepage ?? string.Empty;

    /// <summary>是否有主页链接。</summary>
    public bool HasHomepage => !string.IsNullOrWhiteSpace(HomepageText);

    /// <summary>标签（详情优先，其次列表行）。</summary>
    public IReadOnlyList<string> Tags => _details?.Tags ?? _row?.Tags ?? Array.Empty<string>();

    /// <summary>是否有标签。</summary>
    public bool HasTags => Tags.Count > 0;

    /// <summary>元数据键值行（作者/版本/体积/许可证/主页/标签），标签与格式都在 VM 内集中，XAML 不写文案。</summary>
    public IReadOnlyList<DiagnosticItem> MetadataItems
    {
        get
        {
            var items = new List<DiagnosticItem>(6)
            {
                new("作者", AuthorText),
                new("最新版本", LatestVersionText),
                new("已安装", InstalledVersionText),
                new("体积", SizeText),
                new("许可证", LicenseText),
            };

            if (HasHomepage)
            {
                items.Add(new DiagnosticItem("主页", HomepageText));
            }

            if (HasTags)
            {
                items.Add(new DiagnosticItem("标签", string.Join(", ", Tags)));
            }

            return items;
        }
    }

    /// <summary>描述区标题文案。</summary>
    public string DescriptionHeaderText => "描述";

    /// <summary>依赖区标题文案。</summary>
    public string DependenciesHeaderText => "依赖";

    /// <summary>推荐区标题文案。</summary>
    public string RecommendationsHeaderText => "推荐";

    /// <summary>冲突区标题文案。</summary>
    public string ConflictsHeaderText => "冲突";

    /// <summary>是否已安装。</summary>
    public bool IsInstalled => _installed is not null || _row?.IsInstalled == true;

    /// <summary>是否有可用更新。</summary>
    public bool HasUpdate => _row?.HasUpdate ?? false;

    /// <summary>是否与当前游戏版本兼容。</summary>
    public bool IsCompatible => _details?.IsCompatible ?? _row?.IsCompatible ?? true;

    /// <summary>兼容性文案（不靠颜色单一通道传达状态，03 篇 §9）。</summary>
    public string CompatibilityText => IsCompatible ? "兼容当前游戏版本" : "不兼容当前游戏版本";

    /// <summary>依赖项（已满足/缺失由服务层判定，视图着色）。</summary>
    public IReadOnlyList<ModRelationItem> Dependencies =>
        _details?.Dependencies ?? Array.Empty<ModRelationItem>();

    /// <summary>推荐项。</summary>
    public IReadOnlyList<ModRelationItem> Recommendations =>
        _details?.Recommendations ?? Array.Empty<ModRelationItem>();

    /// <summary>冲突项。</summary>
    public IReadOnlyList<ModRelationItem> Conflicts =>
        _details?.Conflicts ?? Array.Empty<ModRelationItem>();

    /// <summary>是否有依赖项。</summary>
    public bool HasDependencies => Dependencies.Count > 0;

    /// <summary>是否有推荐项。</summary>
    public bool HasRecommendations => Recommendations.Count > 0;

    /// <summary>是否有冲突项。</summary>
    public bool HasConflicts => Conflicts.Count > 0;

    /// <summary>缺失（未满足）的依赖数量。</summary>
    public int MissingDependencyCount => CountMissing(Dependencies);

    /// <summary>依赖摘要文案，例如"依赖 3 项，其中 1 项缺失"。</summary>
    public string DependencySummaryText
    {
        get
        {
            if (!HasDependencies)
            {
                return "无依赖";
            }

            var missing = MissingDependencyCount;
            return missing == 0
                ? "依赖 " + Dependencies.Count + " 项，均已满足"
                : "依赖 " + Dependencies.Count + " 项，其中 " + missing + " 项缺失";
        }
    }

    /// <summary>选中一个模组（按 Identifier 回查快照；null 表示清空）。</summary>
    public void Select(string? identifier)
    {
        _identifier = string.IsNullOrWhiteSpace(identifier) ? null : identifier;
        Reload();
    }

    /// <summary>
    /// 直接注入详情投影（调用方已有富字段时用，例如从 Core 适配器拿到后先塞进来）。
    /// <paramref name="details"/> 为 null 时清空富字段并保留 Identifier 对应状态。
    /// </summary>
    public void ShowDetails(ModDetailsSnapshot? details)
    {
        if (details is null)
        {
            _details = null;
            Refresh();
            return;
        }

        _identifier = string.IsNullOrWhiteSpace(details.Identifier) ? null : details.Identifier;
        _details = details;
        Reload();
    }

    /// <summary>清空选中（切实例/刷新时用，避免显示上一实例的旧数据）。</summary>
    public void Clear()
    {
        _identifier = null;
        _row = null;
        _installed = null;
        _details = null;
        Refresh();
    }

    /// <inheritdoc />
    public void Activate()
    {
        if (IsActive)
        {
            return;
        }

        IsActive = true;
        _repository.Updated += OnRepositoryUpdated;
        _registry.Changed += OnRegistryChanged;
        Reload();
    }

    /// <inheritdoc />
    public void Deactivate()
    {
        if (!IsActive)
        {
            return;
        }

        IsActive = false;
        _repository.Updated -= OnRepositoryUpdated;
        _registry.Changed -= OnRegistryChanged;
    }

    private void OnRepositoryUpdated(RepositorySnapshot snapshot)
    {
        // 快照整体替换：不持有旧行对象，按 Identifier 重新回查（04 篇 §6）。
        _ = snapshot;
        Reload();
    }

    private void OnRegistryChanged(RegistrySnapshot snapshot)
    {
        _ = snapshot;
        Reload();
    }

    private void Reload()
    {
        if (_identifier is null)
        {
            _row = null;
            _installed = null;
            _details = null;
            Refresh();
            return;
        }

        _row = FindRow(_identifier);
        _installed = FindInstalled(_identifier);

        var provided = DetailsProvider?.Invoke(_identifier);
        if (provided is not null)
        {
            _details = provided;
        }
        else if (_details is not null && !string.Equals(_details.Identifier, _identifier, StringComparison.Ordinal))
        {
            // 换选中项且没有提供者时，旧的富字段必须作废，否则会串台。
            _details = null;
        }

        Refresh();
    }

    private void Refresh() => OnPropertyChanged(string.Empty);

    private ModRowData? FindRow(string identifier)
    {
        foreach (var row in _repository.Current.Available)
        {
            if (string.Equals(row.Identifier, identifier, StringComparison.Ordinal))
            {
                return row;
            }
        }

        return null;
    }

    private InstalledModuleInfo? FindInstalled(string identifier) =>
        _registry.Current.Installed.TryGetValue(identifier, out var info) ? info : null;

    private static string? FirstNonEmpty(string? first, string? second)
    {
        if (!string.IsNullOrWhiteSpace(first))
        {
            return first;
        }

        return string.IsNullOrWhiteSpace(second) ? null : second;
    }

    private static int CountMissing(IReadOnlyList<ModRelationItem> items)
    {
        var missing = 0;
        foreach (var item in items)
        {
            if (!item.IsSatisfied)
            {
                missing++;
            }
        }

        return missing;
    }
}
