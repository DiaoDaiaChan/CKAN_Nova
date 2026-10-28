using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.ModConfig;
using CkanUI.ViewModels.Pages.ModConfig;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CkanUI.ViewModels.Pages;

/// <summary>
/// Mod 配置页（M9 / UI-CFG-01 骨架，15 篇 §5、§6）。
/// 左侧是"有适配器的 mod"卡片列表，右侧是选中 mod 的字段行；本骨架只做只读渲染与筛选。
/// </summary>
/// <remarks>
/// 为什么这一页不碰文件：读盘合成模型在 <see cref="IModConfigService"/>，写盘唯一入口是 IAdapterWriter（CFG-05），
/// 页面只负责编排与文案，这样"写坏配置"的风险被限制在服务层的校验/快照/自校验里（工程约定 §7）。
/// 译文走构造注入的委托，本层不引用本地化实现（铁律 3）。
/// </remarks>
public sealed partial class ModConfigPageViewModel : PageViewModel
{
    private readonly IModConfigService _config;
    private readonly IAdapterCatalog? _catalog;
    private readonly Func<string, string>? _translate;
    private readonly ModConfigFieldRendererRegistry _renderers;
    private ModConfigInstanceModel? _model;

    /// <summary>无参构造：供设计时预览与视图定位器反射使用（依赖是空实现）。</summary>
    public ModConfigPageViewModel()
        : this(new EmptyModConfigService())
    {
    }

    /// <summary>带服务注入的构造函数。</summary>
    /// <param name="config">配置读取服务（合成页面模型）。</param>
    /// <param name="catalog">适配器目录；为 null 时页面不展示适配器清单。</param>
    /// <param name="translate">译文委托（键为中文原文，缺省时原样返回）。</param>
    /// <param name="renderers">字段渲染器注册表；缺省用内置五种。</param>
    public ModConfigPageViewModel(
        IModConfigService config,
        IAdapterCatalog? catalog = null,
        Func<string, string>? translate = null,
        ModConfigFieldRendererRegistry? renderers = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _catalog = catalog;
        _translate = translate;
        _renderers = renderers ?? new ModConfigFieldRendererRegistry();
        StatusText = T("选择左侧的 mod 查看可配置项；没有适配器的 mod 只提供原始只读视图。");
    }

    /// <inheritdoc />
    public override string Title => T("Mod 配置");

    // ---- 状态 --------------------------------------------------------------

    /// <summary>有适配器的 mod 卡片。</summary>
    [ObservableProperty]
    private IReadOnlyList<ModConfigModCardViewModel> _mods = [];

    /// <summary>当前选中的卡片。</summary>
    [ObservableProperty]
    private ModConfigModCardViewModel? _selectedMod;

    /// <summary>选中 mod 的字段行（已按搜索/仅看已改过滤，并按分组聚合）。</summary>
    [ObservableProperty]
    private IReadOnlyList<ModConfigFieldGroupViewModel> _fieldGroups = [];

    /// <summary>当前实例加载到的适配器清单。</summary>
    [ObservableProperty]
    private IReadOnlyList<ModConfigAdapterSummaryViewModel> _adapters = [];

    /// <summary>搜索框内容（按键名/显示名/注释三路匹配）。</summary>
    [ObservableProperty]
    private string _searchText = string.Empty;

    /// <summary>只看已改项。</summary>
    [ObservableProperty]
    private bool _onlyModified;

    /// <summary>是否正在读盘。</summary>
    [ObservableProperty]
    private bool _isBusy;

    /// <summary>忙碌时的提示文案。</summary>
    [ObservableProperty]
    private string _busyText = string.Empty;

    /// <summary>状态栏文案。</summary>
    [ObservableProperty]
    private string _statusText = string.Empty;

    /// <summary>错误文案（失败时同时写进状态栏）。</summary>
    [ObservableProperty]
    private string? _errorText;

    /// <summary>当前实例是否正在跑 KSP（运行时写盘要二次确认）。</summary>
    [ObservableProperty]
    private bool _isGameRunning;

    /// <summary>实例摘要（"实例：默认｜游戏目录：…"）。</summary>
    [ObservableProperty]
    private string _instanceSummaryText = string.Empty;

    // ---- 派生文案 ----------------------------------------------------------

    /// <summary>是否有卡片。</summary>
    public bool HasMods => Mods.Count > 0;

    /// <summary>是否显示空状态。</summary>
    public bool ShowEmptyState => Mods.Count == 0 && !IsBusy;

    /// <summary>是否选中了 mod。</summary>
    public bool HasSelection => SelectedMod is not null;

    /// <summary>是否没有选中 mod（视图直接绑定它，避免在 XAML 里做布尔取反）。</summary>
    public bool IsSelectionEmpty => SelectedMod is null;

    /// <summary>是否有错误。</summary>
    public bool HasError => !string.IsNullOrEmpty(ErrorText);

    /// <summary>卡片数量文案。</summary>
    public string ModCountText => Mods.Count == 0 ? "暂无已适配的 mod" : "共 " + Mods.Count + " 个已适配 mod";

    /// <summary>无适配器提示（底部"⊘ N 个 mod 无适配器"）。</summary>
    public string AdapterlessText => _model is { AdapterlessModCount: > 0 } model
        ? "⊘ " + model.AdapterlessModCount + " 个 mod 无适配器"
        : string.Empty;

    /// <summary>是否显示无适配器提示。</summary>
    public bool HasAdapterless => AdapterlessText.Length > 0;

    /// <summary>KSP 运行中的顶部警示文案。</summary>
    public string GameRunningWarningText => IsGameRunning
        ? T("KSP 正在运行：写入配置需要重启 KSP 才生效，写盘前会再确认一次。")
        : string.Empty;

    /// <summary>是否显示运行中警示。</summary>
    public bool ShowGameRunningWarning => IsGameRunning;

    /// <summary>右侧标题："KSP Community Fixes v1.31.0"。</summary>
    public string HeaderTitleText => SelectedMod is { } card ? card.HeaderText : T("未选择 mod");

    /// <summary>右侧副标题（状态 + 支持区间）。</summary>
    public string HeaderStateText => SelectedMod is { } card
        ? string.Join("｜", new[] { card.StateText, card.SupportedRangeText }.Where(part => part.Length > 0))
        : string.Empty;

    /// <summary>搜索框占位文案（含字段总数）。</summary>
    public string SearchPlaceholder => "搜索 " + FieldTotal + " 项…";

    /// <summary>选中 mod 的字段总数。</summary>
    public int FieldTotal => SelectedMod?.Snapshot.FieldCount ?? 0;

    /// <summary>当前筛选后的字段条数。</summary>
    public int VisibleFieldCount => FieldGroups.Sum(group => group.Rows.Count);

    /// <summary>筛选后条数文案。</summary>
    public string VisibleCountText => SelectedMod is null
        ? string.Empty
        : "显示 " + VisibleFieldCount + " / " + FieldTotal + " 项";

    /// <summary>底部 diff 条文案（"改动 N 项"）。</summary>
    public string ModifiedBarText => SelectedMod is { } card && card.HasModified
        ? "改动 " + card.Snapshot.ModifiedCount + " 项"
        : string.Empty;

    /// <summary>是否有改动（决定 diff 条是否可见）。</summary>
    public bool HasChanges => SelectedMod is { HasModified: true };

    /// <summary>选中卡片最需要提示的一条告警。</summary>
    public string SelectedWarningText => SelectedMod?.WarningText ?? string.Empty;

    /// <summary>是否显示选中卡片的告警。</summary>
    public bool HasSelectedWarning => SelectedWarningText.Length > 0;

    /// <summary>未选中时的提示文案。</summary>
    public string NoSelectionHintText => T("左侧选择一个 mod 查看它的可配置项。");

    /// <summary>是否有适配器清单。</summary>
    public bool HasAdapters => Adapters.Count > 0;

    /// <summary>适配器清单标题。</summary>
    public string AdapterListTitle => "已加载 " + Adapters.Count + " 个适配器";

    /// <summary>是否可用筛选（有选中 mod 才需要）。</summary>
    public bool CanFilter => HasSelection;

    // ---- 命令 --------------------------------------------------------------

    /// <summary>读盘并重建页面（页面激活时自动执行一次，"刷新"按钮也走这里）。</summary>
    [RelayCommand(CanExecute = nameof(CanLoad))]
    private async Task LoadAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        BusyText = T("正在读取 mod 配置文件…");
        ErrorText = null;
        try
        {
            LoadAdapters();
            var result = await _config.LoadAsync(string.Empty).ConfigureAwait(true);
            if (!result.IsSuccess || result.Value is not { } model)
            {
                ApplyModel(null);
                SetError(result.Message ?? T("读取 mod 配置失败。"), result.Detail);
                return;
            }

            ApplyModel(model);
        }
        catch (Exception ex)
        {
            ApplyModel(null);
            // 页面是最后一道防线：服务层约定返回 OperationResult，这里兜住意外异常，别让异常穿到 UI 线程外。
            SetError(T("读取 mod 配置失败：") + ex.Message, ex.GetType().Name);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>加载条件：不忙。</summary>
    public bool CanLoad => !IsBusy;

    /// <summary>重读适配器目录（用户往用户目录塞了新 adapter 后不用重启）。</summary>
    [RelayCommand]
    private void ReloadAdapters()
    {
        _catalog?.Reload();
        LoadAdapters();
        SetStatus(_catalog is null
            ? T("没有配置适配器目录，适配器清单为空。")
            : "已重新加载 " + Adapters.Count + " 个适配器。");
    }

    /// <summary>清空搜索。</summary>
    [RelayCommand]
    private void ClearSearch() => SearchText = string.Empty;

    /// <summary>切换"仅看已改"。</summary>
    [RelayCommand]
    private void ToggleOnlyModified() => OnlyModified = !OnlyModified;

    /// <summary>选中某个 mod 卡片（视图绑定用）。</summary>
    /// <param name="card">卡片；null 表示清空选择。</param>
    [RelayCommand]
    public void SelectMod(ModConfigModCardViewModel? card) => SelectedMod = card;

    // ---- 数据装配 ----------------------------------------------------------

    /// <summary>把服务层模型装进页面（null 表示没有模型/加载失败）。</summary>
    /// <param name="model">实例模型。</param>
    public void ApplyModel(ModConfigInstanceModel? model)
    {
        _model = model;
        if (model is null)
        {
            Mods = [];
            SelectedMod = null;
            IsGameRunning = false;
            InstanceSummaryText = string.Empty;
            RebuildGroups();
            NotifyModelState();
            return;
        }

        var cards = model.Mods.Select(snapshot => new ModConfigModCardViewModel(snapshot)).ToArray();
        Mods = cards;
        IsGameRunning = model.IsGameRunning;
        InstanceSummaryText = model.InstanceName.Length > 0
            ? "实例：" + model.InstanceName + "｜游戏目录：" + model.GameDir
            : "游戏目录：" + model.GameDir;

        // 优先停在"可编辑且有改动"的卡片上：用户多半是来确认自己改过的东西。
        SelectedMod = cards.FirstOrDefault(card => card.IsEditable && card.HasModified)
            ?? cards.FirstOrDefault(card => card.IsEditable)
            ?? cards.FirstOrDefault();

        SetStatus(model.Mods.Count == 0
            ? T("这个实例里没有已适配的 mod：可以把 adapter.json 放进用户目录后重试。")
            : "读取完成：" + ModCountText + "。");
        NotifyModelState();
    }

    /// <summary>按当前筛选条件重建字段分组。</summary>
    public void RebuildGroups()
    {
        if (SelectedMod is not { } card)
        {
            FieldGroups = [];
            NotifyFilterState();
            return;
        }

        var rows = new List<ModConfigFieldRowViewModel>();
        foreach (var file in card.Snapshot.Files)
        {
            foreach (var field in file.Fields)
            {
                var row = new ModConfigFieldRowViewModel(field, _renderers.Resolve(field.Type), file.IsWritable);
                if (!row.Matches(SearchText))
                {
                    continue;
                }

                if (OnlyModified && !row.IsModified)
                {
                    continue;
                }

                rows.Add(row);
            }
        }

        FieldGroups = rows
            .GroupBy(row => row.GroupTitle, StringComparer.Ordinal)
            .Select(group => new ModConfigFieldGroupViewModel(group.Key, group.ToArray()))
            .ToArray();
        NotifyFilterState();
    }

    private void LoadAdapters()
    {
        if (_catalog is null)
        {
            Adapters = [];
            return;
        }

        Adapters = _catalog.Adapters
            .Select(adapter => new ModConfigAdapterSummaryViewModel(adapter))
            .ToArray();
    }

    private void SetStatus(string message) => StatusText = message;

    private void SetError(string message, string? detail)
    {
        ErrorText = string.IsNullOrWhiteSpace(detail) ? message : message + "（" + detail + "）";
        StatusText = ErrorText ?? message;
    }

    private string T(string chinese) => _translate is null ? chinese : _translate(chinese);

    private void NotifyModelState()
    {
        OnPropertyChanged(nameof(HasMods));
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(ModCountText));
        OnPropertyChanged(nameof(AdapterlessText));
        OnPropertyChanged(nameof(HasAdapterless));
        OnPropertyChanged(nameof(GameRunningWarningText));
        OnPropertyChanged(nameof(ShowGameRunningWarning));
        OnPropertyChanged(nameof(AdapterListTitle));
        OnPropertyChanged(nameof(HasAdapters));
        OnPropertyChanged(nameof(CanFilter));
        LoadCommand.NotifyCanExecuteChanged();
    }

    private void NotifyFilterState()
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(IsSelectionEmpty));
        OnPropertyChanged(nameof(HeaderTitleText));
        OnPropertyChanged(nameof(HeaderStateText));
        OnPropertyChanged(nameof(SearchPlaceholder));
        OnPropertyChanged(nameof(FieldTotal));
        OnPropertyChanged(nameof(VisibleFieldCount));
        OnPropertyChanged(nameof(VisibleCountText));
        OnPropertyChanged(nameof(ModifiedBarText));
        OnPropertyChanged(nameof(HasChanges));
        OnPropertyChanged(nameof(SelectedWarningText));
        OnPropertyChanged(nameof(HasSelectedWarning));
        OnPropertyChanged(nameof(CanFilter));
        OnPropertyChanged(nameof(HasAdapters));
    }

    /// <inheritdoc />
    protected override void OnActivated()
    {
        base.OnActivated();

        // 每次进页面都重读：别的页面（安装/卸载）可能刚改过 registry 与磁盘。
        LoadCommand.Execute(null);
    }

    partial void OnSelectedModChanged(ModConfigModCardViewModel? value) => RebuildGroups();

    partial void OnSearchTextChanged(string value) => RebuildGroups();

    partial void OnOnlyModifiedChanged(bool value) => RebuildGroups();

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanLoad));
        OnPropertyChanged(nameof(ShowEmptyState));
        LoadCommand.NotifyCanExecuteChanged();
    }

    partial void OnErrorTextChanged(string? value) => OnPropertyChanged(nameof(HasError));

    partial void OnModsChanged(IReadOnlyList<ModConfigModCardViewModel> value)
    {
        OnPropertyChanged(nameof(HasMods));
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(ModCountText));
    }

    partial void OnAdaptersChanged(IReadOnlyList<ModConfigAdapterSummaryViewModel> value)
    {
        OnPropertyChanged(nameof(HasAdapters));
        OnPropertyChanged(nameof(AdapterListTitle));
    }

    partial void OnIsGameRunningChanged(bool value)
    {
        OnPropertyChanged(nameof(GameRunningWarningText));
        OnPropertyChanged(nameof(ShowGameRunningWarning));
    }

    /// <summary>设计时/预览用的空实现：什么都没装，页面显示空状态而不是崩。</summary>
    private sealed class EmptyModConfigService : IModConfigService
    {
        public Task<OperationResult<ModConfigInstanceModel>> LoadAsync(string instanceName, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult<ModConfigInstanceModel>.Fail("设计时预览：没有可用的 mod 配置服务。"));
    }
}
