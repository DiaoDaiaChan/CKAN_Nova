using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Avalonia.Media.Imaging;
using CkanUI.Localization;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.ViewModels;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CkanUI.Plugin.SpaceDock;

/// <summary>
/// SpaceDock 浏览页：匿名浏览 / 搜索 / 下载 ZIP / 直接解压进当前实例的 GameData。
/// </summary>
/// <remarks>
/// <para>登录口径：SpaceDock 的登录只用于发布或更新模组，浏览、搜索与下载全部匿名；页面因此不引导登录，
/// 只在页头标注一句，并把「打开 SpaceDock」作为可选出口。</para>
/// <para>去重口径：CKAN 仓库的 <c>resources.spacedock</c> 与已安装注册表是判重依据，默认隐藏重复项；
/// 隐藏只影响列表投影，不修改 CKAN 的任何数据，用户可随时取消勾选看全量。</para>
/// <para>安装口径：这条链路不经过 CKAN——直接下载 ZIP、解压 GameData，因此不写注册表、不做依赖解析。
/// 确认框里必须把这件事说清楚，避免用户以为 CKAN 会接管后续更新。</para>
/// </remarks>
public sealed partial class SpaceDockPageViewModel : PageViewModel, IPluginPage, IDisposable, IPageViewEvictionAware
{
    private const int PageSize = 30;
    private const int DescriptionLimit = 12000;
    private const int CardImageConcurrency = 6;
    private const int MaxCardImages = 60;

    private static readonly Regex HtmlTagPattern = new("<[^>]+>", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex BbCodePattern = new(@"\[[^\]]{1,24}\]", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex HorizontalSpacePattern = new(@"[ \t]+", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex BlankLinePattern = new(@"\n{3,}", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly ISpaceDockService _spaceDock;
    private readonly ISpaceDockInstaller _installer;
    private readonly ISpacedockImageService _images;
    private readonly IInstanceService _instances;
    private readonly IRepositoryService _repositories;
    private readonly IRegistryService _registry;
    private readonly IDialogService _dialogs;
    private readonly IErrorReporter _errors;

    private IReadOnlyList<SpaceDockModInfo> _allMods = Array.Empty<SpaceDockModInfo>();
    private SpaceDockDuplicateMatcher _duplicates = SpaceDockDuplicateMatcher.Empty;
    private CancellationTokenSource? _activationCts;
    private CancellationTokenSource? _cardImageCts;
    private int _cardImageGeneration;
    private int _detailRequest;
    private long? _loadedDetailId;
    private int _gameId;
    private bool _hasLoaded;
    private bool _initialized;
    private bool _disposed;

    /// <summary>依赖注入构造：全部依赖都是宿主已注册的抽象。</summary>
    public SpaceDockPageViewModel(
        ISpaceDockService spaceDock,
        ISpaceDockInstaller installer,
        ISpacedockImageService images,
        IInstanceService instances,
        IRepositoryService repositories,
        IRegistryService registry,
        IDialogService dialogs,
        IErrorReporter errors)
    {
        _spaceDock = spaceDock ?? throw new ArgumentNullException(nameof(spaceDock));
        _installer = installer ?? throw new ArgumentNullException(nameof(installer));
        _images = images ?? throw new ArgumentNullException(nameof(images));
        _instances = instances ?? throw new ArgumentNullException(nameof(instances));
        _repositories = repositories ?? throw new ArgumentNullException(nameof(repositories));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _errors = errors ?? throw new ArgumentNullException(nameof(errors));

        SortOptions = new[]
        {
            new SpaceDockSortOption(Text("最近更新", LocalizationKeys.SpaceDockSortUpdated), SpaceDockSortOrder.Updated, false),
            new SpaceDockSortOption(Text("名称 A→Z", LocalizationKeys.SpaceDockSortName), SpaceDockSortOrder.Name, true),
            new SpaceDockSortOption(Text("最新发布", LocalizationKeys.SpaceDockSortCreated), SpaceDockSortOrder.Created, false),
        };
        _selectedSort = SortOptions[0];
        _statusText = Text("就绪", LocalizationKeys.SpaceDockStatusReady);
        _busyText = Text("正在加载 SpaceDock…", LocalizationKeys.SpaceDockLoading);
    }

    /// <inheritdoc />
    public override string Title => Text("SpaceDock 模组", LocalizationKeys.NavRailSpaceDock);

    /// <summary>可用的排序方式。</summary>
    public IReadOnlyList<SpaceDockSortOption> SortOptions { get; }

    private static readonly bool CoverTrace = Environment.GetEnvironmentVariable("CKANUI_SD_TRACE") == "1";
    // 驻留预算摘掉本页视图时是否连封面位图一起放掉：默认关，量到收益再由人决定开不开。
    private static readonly bool ReleaseOnEvict = Environment.GetEnvironmentVariable("CKANUI_SD_RELEASE_ON_EVICT") == "1";
    // 封面显示尺寸只有 232×116，整幅解码是在拿 1920×1080 的像素换 7.9 MB 内存；宽度留 0 = 保持整幅解码。
    private static readonly int CoverDecodeWidth = ReadCoverDecodeWidth();
    private static readonly object CoverTraceLock = new();
    private static long _coverTraceBytes;
    private static int _coverTraceCount;

    /// <summary>列表行（已应用 CKAN 去重开关）。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    private IReadOnlyList<SpaceDockModItem> _mods = Array.Empty<SpaceDockModItem>();

    /// <summary>当前选中行。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyPropertyChangedFor(nameof(ShowDetailHint))]
    private SpaceDockModItem? _selectedMod;

    /// <summary>排序选择。</summary>
    [ObservableProperty]
    private SpaceDockSortOption? _selectedSort;

    /// <summary>搜索框内容。</summary>
    [ObservableProperty]
    private string _searchText = string.Empty;

    /// <summary>是否默认隐藏 CKAN 已有模组。</summary>
    [ObservableProperty]
    private bool _hideCkanDuplicates = true;

    /// <summary>状态栏文案。</summary>
    [ObservableProperty]
    private string _statusText;

    /// <summary>错误条主文案；为空表示无错误。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _errorText;

    /// <summary>错误条技术细节。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasErrorDetail))]
    private string? _errorDetail;

    /// <summary>是否有异步操作进行中。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    private bool _isBusy;

    /// <summary>遮罩文案。</summary>
    [ObservableProperty]
    private string _busyText;

    /// <summary>详情是否正在加载。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowDetailHint))]
    private bool _isDetailLoading;

    /// <summary>当前页码。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageText))]
    private int _pageNumber = 1;

    /// <summary>总页数。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageText))]
    [NotifyPropertyChangedFor(nameof(ShowPager))]
    private int _totalPages = 1;

    /// <summary>当前结果总数（去重前）。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageText))]
    private int _totalCount;

    /// <summary>被去重隐藏的条数。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HiddenBadgeText))]
    [NotifyPropertyChangedFor(nameof(HasHidden))]
    private int _hiddenCount;

    /// <summary>是否处于搜索结果模式（搜索结果不分页）。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPager))]
    private bool _isSearchMode;

    /// <summary>详情标题。</summary>
    [ObservableProperty]
    private string _detailName = string.Empty;

    /// <summary>详情作者行。</summary>
    [ObservableProperty]
    private string _detailAuthorText = string.Empty;

    /// <summary>详情统计行。</summary>
    [ObservableProperty]
    private string _detailStatsText = string.Empty;

    /// <summary>详情许可证行。</summary>
    [ObservableProperty]
    private string _detailLicenseText = string.Empty;

    /// <summary>详情正文（已去掉 HTML / BBCode 标记）。</summary>
    [ObservableProperty]
    private string _detailDescription = string.Empty;

    /// <summary>详情页链接。</summary>
    [ObservableProperty]
    private string _detailUrl = string.Empty;

    /// <summary>可选版本（最新在前）。</summary>
    [ObservableProperty]
    private IReadOnlyList<SpaceDockVersionInfo> _versions = Array.Empty<SpaceDockVersionInfo>();

    /// <summary>当前选中的下载版本。</summary>
    [ObservableProperty]
    private SpaceDockVersionInfo? _selectedVersion;

    /// <summary>详情截图。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasScreenshot))]
    private Bitmap? _screenshot;

    /// <summary>是否有选中项。</summary>
    public bool HasSelection => SelectedMod is not null;

    /// <summary>是否有截图。</summary>
    public bool HasScreenshot => Screenshot is not null;

    /// <summary>是否显示错误条。</summary>
    public bool HasError => !string.IsNullOrEmpty(ErrorText);

    /// <summary>是否显示错误技术细节。</summary>
    public bool HasErrorDetail => !string.IsNullOrEmpty(ErrorDetail);

    /// <summary>是否显示空态。</summary>
    public bool ShowEmptyState => !IsBusy && !IsDetailLoading && Mods.Count == 0 && string.IsNullOrEmpty(ErrorText);

    /// <summary>是否显示「从左侧选择」提示。</summary>
    public bool ShowDetailHint => !HasSelection && !IsDetailLoading;

    /// <summary>是否显示分页。</summary>
    public bool ShowPager => !IsSearchMode && TotalPages > 1;

    /// <summary>分页摘要。</summary>
    public string PageText => Text(
        "第 {0}/{1} 页 · 共 {2} 个",
        LocalizationKeys.SpaceDockPageOf,
        PageNumber,
        TotalPages,
        TotalCount);

    /// <summary>去重徽标文案。</summary>
    public string HiddenBadgeText => Text(
        "已隐藏 {0} 个 CKAN 重复",
        LocalizationKeys.SpaceDockHiddenBadge,
        HiddenCount);

    /// <summary>是否有被隐藏的重复项。</summary>
    public bool HasHidden => HiddenCount > 0;

    /// <summary>当前实例是否可用（无实例时安装按钮禁用并给出提示）。</summary>
    public bool HasInstance => _instances.Current is not null;

    /// <summary>无实例提示。</summary>
    public string NoInstanceText => Text(
        "当前没有选择游戏实例，安装按钮不可用。",
        LocalizationKeys.SpaceDockNoInstance);

    /// <summary>页头登录提示。</summary>
    public string LoginNoteText => Text(
        "SpaceDock 登录只用于发布或更新模组；浏览、下载与安装均不需要账号。",
        LocalizationKeys.SpaceDockLoginNote);

    /// <summary>下载按钮可用性。</summary>
    public bool CanDownloadSelection => !IsBusy && SelectedVersion is not null;

    /// <summary>安装按钮可用性。</summary>
    public bool CanInstallSelection => !IsBusy && SelectedVersion is not null && _instances.Current is not null;

    /// <summary>页头副标题。</summary>
    public string SubtitleText => Text(
        "浏览、搜索并直接安装 SpaceDock 上的模组；无需登录 SpaceDock。",
        LocalizationKeys.SpaceDockPageSubtitle);

    /// <inheritdoc />
    protected override void OnActivated()
    {
        _instances.CurrentChanged += OnCurrentInstanceChanged;
        _activationCts = new CancellationTokenSource();

        if (_hasLoaded)
        {
            RebuildDuplicates();
            ApplyFilter();
            OnPropertyChanged(nameof(HasInstance));
            RaiseCommandStates();
            if (SelectedMod is { } selected)
            {
                _ = LoadDetailAsync(selected.Mod);
            }

            return;
        }

        _hasLoaded = true;
        _ = InitializeAsync(_activationCts.Token);
    }

    /// <inheritdoc />
    protected override void OnDeactivated()
    {
        _instances.CurrentChanged -= OnCurrentInstanceChanged;
        _activationCts?.Cancel();
        _activationCts?.Dispose();
        _activationCts = null;
        if (!_initialized)
        {
            // 初始加载被切页打断：下次进入重来，避免停在半加载状态。
            _hasLoaded = false;
        }

        CancelCardImageLoad();
        _detailRequest++;
        _loadedDetailId = null;
        IsDetailLoading = false;
        DisposeScreenshot();
        RaiseCommandStates();
    }

    /// <summary>视图被驻留预算摘走时放掉只为它缓存的封面位图；开关关闭时保持原样（位图留着，回访秒开）。</summary>
    public void OnViewEvicted()
    {
        if (!ReleaseOnEvict || _disposed)
        {
            return;
        }

        var released = Mods.Count(item => item.Image is not null);
        CancelCardImageLoad();
        ReleaseCardImages();
        TraceEvicted(released);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _instances.CurrentChanged -= OnCurrentInstanceChanged;
        _activationCts?.Cancel();
        _activationCts?.Dispose();
        _activationCts = null;
        CancelCardImageLoad();
        DisposeScreenshot();
        GC.SuppressFinalize(this);
    }

    partial void OnSelectedModChanged(SpaceDockModItem? value)
    {
        RaiseCommandStates();
        _ = LoadDetailAsync(value?.Mod);
    }

    partial void OnSelectedVersionChanged(SpaceDockVersionInfo? value) => RaiseCommandStates();

    partial void OnSelectedSortChanged(SpaceDockSortOption? value)
    {
        if (!IsActive || IsBusy || value is null)
        {
            return;
        }

        _ = RunBusyAsync(
            Text("正在加载 SpaceDock…", LocalizationKeys.SpaceDockLoading),
            async ct =>
            {
                IsSearchMode = false;
                await FetchBrowseAsync(1, ct);
            });
    }

    partial void OnHideCkanDuplicatesChanged(bool value) => ApplyFilter();

    partial void OnIsBusyChanged(bool value) => RaiseCommandStates();

    partial void OnPageNumberChanged(int value) => RaiseCommandStates();

    partial void OnTotalPagesChanged(int value) => RaiseCommandStates();

    partial void OnIsSearchModeChanged(bool value) => RaiseCommandStates();

    private async Task InitializeAsync(CancellationToken ct)
    {
        await RunBusyAsync(
            Text("正在加载 SpaceDock…", LocalizationKeys.SpaceDockLoading),
            async token =>
            {
                await LoadIndexesAsync(token);
                RebuildDuplicates();

                if (_gameId <= 0)
                {
                    await ResolveGameAsync(token);
                }

                if (ct.IsCancellationRequested || _gameId <= 0)
                {
                    return;
                }

                await FetchBrowseAsync(1, token);
            },
            ct);
    }

    private async Task LoadIndexesAsync(CancellationToken ct)
    {
        try
        {
            if (_repositories.Current.Available.Count == 0)
            {
                var cached = await _repositories.LoadCachedAsync(ct);
                if (!cached.IsSuccess)
                {
                    _errors.Report("SpaceDock 读取 CKAN 索引", cached.Message ?? "本地索引不可用", cached.Detail);
                }
            }

            if (_instances.Current is { } instance &&
                !string.Equals(_registry.Current.InstanceName, instance.Name, StringComparison.Ordinal))
            {
                var opened = await _registry.OpenAsync(instance, ct);
                if (!opened.IsSuccess)
                {
                    _errors.Report("SpaceDock 读取 CKAN 注册表", opened.Message ?? "注册表不可用", opened.Detail);
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // 索引只是去重的辅助输入；读不到也要能浏览 SpaceDock。
            _errors.Report(ex, "SpaceDock 加载 CKAN 索引");
        }
    }

    private async Task ResolveGameAsync(CancellationToken ct)
    {
        var games = await _spaceDock.GetGamesAsync(ct);
        if (!games.IsSuccess || games.Value is null)
        {
            SetApiError(
                Text("SpaceDock 游戏列表里没有找到 KSP。", LocalizationKeys.SpaceDockUnsupportedGame),
                games);
            return;
        }

        var ksp = games.Value.FirstOrDefault(game =>
                      string.Equals(game.Short, "kerbal-space-program", StringComparison.OrdinalIgnoreCase)) ??
                  games.Value.FirstOrDefault(game =>
                      game.Name.Contains("Kerbal", StringComparison.OrdinalIgnoreCase));

        if (ksp is null)
        {
            SetApiError(
                Text("SpaceDock 游戏列表里没有找到 KSP。", LocalizationKeys.SpaceDockUnsupportedGame),
                null);
            return;
        }

        // 游戏 ID 由 SpaceDock 分配且会变；这里只缓存解析结果，不把 3102 写死。
        _gameId = ksp.Id;
    }

    private async Task FetchBrowseAsync(int page, CancellationToken ct)
    {
        var sort = SelectedSort ?? SortOptions[0];
        var result = await _spaceDock.BrowseAsync(
            _gameId,
            Math.Max(1, page),
            PageSize,
            sort.Order,
            sort.Ascending,
            ct);

        if (!result.IsSuccess || result.Value is null)
        {
            SetApiError(Text("加载 SpaceDock 失败", LocalizationKeys.SpaceDockLoadFailed), result);
            return;
        }

        var browse = result.Value;
        IsSearchMode = false;
        _allMods = browse.Mods;
        PageNumber = Math.Max(1, browse.Page);
        TotalPages = Math.Max(1, browse.Pages);
        TotalCount = browse.Total;
        _initialized = true;
        ClearError();
        ApplyFilter();
    }

    private async Task FetchSearchAsync(string query, CancellationToken ct)
    {
        var result = await _spaceDock.SearchAsync(query, ct);
        if (!result.IsSuccess || result.Value is null)
        {
            SetApiError(Text("加载 SpaceDock 失败", LocalizationKeys.SpaceDockLoadFailed), result);
            return;
        }

        IsSearchMode = true;
        _allMods = result.Value;
        PageNumber = 1;
        TotalPages = 1;
        TotalCount = result.Value.Count;
        ClearError();
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        if (_allMods.Count == 0)
        {
            CancelCardImageLoad();
            ReleaseCardImages();
            Mods = Array.Empty<SpaceDockModItem>();
            HiddenCount = 0;
            SelectedMod = null;
            StatusText = Text("没有找到模组", LocalizationKeys.SpaceDockStatusEmpty);
            return;
        }

        var visible = HideCkanDuplicates
            ? _allMods.Where(mod => !_duplicates.IsDuplicate(mod)).ToArray()
            : _allMods.ToArray();

        HiddenCount = _allMods.Count - visible.Length;

        var previousId = SelectedMod?.Mod.Id;
        CancelCardImageLoad();
        ReleaseCardImages();
        Mods = visible
            .Select(mod => new SpaceDockModItem(
                mod,
                Text(
                    "下载 {0}",
                    LocalizationKeys.SpaceDockCardDownloads,
                    mod.Downloads.ToString("N0", CultureInfo.CurrentCulture))))
            .ToArray();

        var selected = previousId is { } id
            ? Mods.FirstOrDefault(item => item.Mod.Id == id)
            : null;
        SelectedMod = selected ?? Mods.FirstOrDefault();

        if (Mods.Count == 0)
        {
            StatusText = Text(
                "这些模组都已存在于 CKAN，已全部隐藏",
                LocalizationKeys.SpaceDockStatusAllHidden);
        }
        else if (IsSearchMode)
        {
            StatusText = Text(
                "搜索到 {0} 个模组",
                LocalizationKeys.SpaceDockStatusSearch,
                Mods.Count);
        }
        else
        {
            StatusText = Text(
                "本页 {0} 个模组",
                LocalizationKeys.SpaceDockStatusLoaded,
                Mods.Count);
        }

        StartCardImageLoad(Mods);
    }

    /// <summary>放掉当前列表每张卡片的封面图：整批重建列表前调用，否则解码后的位图只能等 GC 收。</summary>
    private void ReleaseCardImages()
    {
        foreach (var stale in Mods)
        {
            stale.ReleaseImage();
        }
    }

    /// <summary>读封面解码宽度：未设置或非正数时返回 0，表示沿用整幅解码。</summary>
    private static int ReadCoverDecodeWidth()
    {
        var raw = Environment.GetEnvironmentVariable("CKANUI_SD_COVER_WIDTH");
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var width) && width > 0
            ? width
            : 0;
    }

    /// <summary>封面取回失败时的探针：只关心「为什么一张都没解码」，不改变页面行为。</summary>
    private static void TraceCoverSkipped(SpaceDockModItem item, bool success, int bytes)
    {
        if (!CoverTrace)
        {
            return;
        }

        var path = Environment.GetEnvironmentVariable("CKANUI_NAV_TRACE");
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            File.AppendAllText(
                path,
                string.Create(CultureInfo.InvariantCulture, $"[sd] cover-skip id={item.Mod.Id} ok={success} bytes={bytes}\n"));
        }
        catch (IOException)
        {
        }
    }

    /// <summary>摘视图时的探针：记录放掉了几张封面与当时的工作集，给「要不要默认开」攒数据。</summary>
    private static void TraceEvicted(int released)
    {
        if (!CoverTrace)
        {
            return;
        }

        var path = Environment.GetEnvironmentVariable("CKANUI_NAV_TRACE");
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            File.AppendAllText(
                path,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"[sd] evict released={released} ws={Environment.WorkingSet / 1048576}MB\n"));
        }
        catch (IOException)
        {
        }
    }

    /// <summary>封面解码探针：记录源字节与解码后像素，给「按显示宽度解码」的决策攒实测数据。</summary>
    private static void TraceCoverDecoded(SpaceDockModItem item, Bitmap cover, int sourceBytes)
    {
        if (!CoverTrace)
        {
            return;
        }

        var path = Environment.GetEnvironmentVariable("CKANUI_NAV_TRACE");
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var size = cover.PixelSize;
        var decodedBytes = (long)size.Width * size.Height * 4;
        lock (CoverTraceLock)
        {
            _coverTraceCount++;
            _coverTraceBytes += decodedBytes;
            var line = string.Create(
                CultureInfo.InvariantCulture,
                $"[sd] cover n={_coverTraceCount} id={item.Mod.Id} px={size.Width}x{size.Height} decoded={decodedBytes / 1048576.0:F2}MB total={_coverTraceBytes / 1048576.0:F2}MB src={sourceBytes / 1024.0:F0}KB");
            try
            {
                File.AppendAllText(path, line + Environment.NewLine);
            }
            catch (IOException)
            {
                // 探针写不进去不影响页面。
            }
        }
    }

    /// <summary>取消上一批封面下载；旧批次的结果不会再回填到新列表。</summary>
    private void CancelCardImageLoad()
    {
        _cardImageGeneration++;
        _cardImageCts?.Cancel();
        _cardImageCts?.Dispose();
        _cardImageCts = null;
    }

    /// <summary>为当前页卡片异步加载封面，限制并发，避免一次打满 SpaceDock。</summary>
    private void StartCardImageLoad(IReadOnlyList<SpaceDockModItem> items)
    {
        CancelCardImageLoad();
        if (items.Count == 0 || _activationCts is null || _disposed)
        {
            return;
        }

        var generation = _cardImageGeneration;
        var cts = CancellationTokenSource.CreateLinkedTokenSource(_activationCts.Token);
        _cardImageCts = cts;
        _ = LoadCardImagesAsync(items, generation, cts.Token);
    }

    private async Task LoadCardImagesAsync(
        IReadOnlyList<SpaceDockModItem> items,
        int generation,
        CancellationToken ct)
    {
        using var gate = new SemaphoreSlim(CardImageConcurrency);
        var tasks = new List<Task>(Math.Min(items.Count, MaxCardImages));
        foreach (var item in items.Take(MaxCardImages))
        {
            tasks.Add(LoadCardImageAsync(item, generation, gate, ct));
        }

        try
        {
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException)
        {
            // 翻页、搜索或离开页面时取消，属于正常路径。
        }
    }

    private async Task LoadCardImageAsync(
        SpaceDockModItem item,
        int generation,
        SemaphoreSlim gate,
        CancellationToken ct)
    {
        try
        {
            await gate.WaitAsync(ct);
            try
            {
                if (generation != _cardImageGeneration || ct.IsCancellationRequested)
                {
                    return;
                }

                var result = await _images.FetchScreenshotAsync(item.Url, ct);
                if (!result.IsSuccess || result.Value is not { Length: > 0 } bytes)
                {
                    TraceCoverSkipped(item, result.IsSuccess, result.Value?.Length ?? 0);
                    return;
                }

                if (generation != _cardImageGeneration || ct.IsCancellationRequested)
                {
                    return;
                }

                using var stream = new MemoryStream(bytes);
                var cover = CoverDecodeWidth > 0
                    ? Bitmap.DecodeToWidth(stream, CoverDecodeWidth, BitmapInterpolationMode.HighQuality)
                    : new Bitmap(stream);
                item.ReplaceImage(cover);
                TraceCoverDecoded(item, cover, bytes.Length);
            }
            finally
            {
                gate.Release();
            }
        }
        catch (OperationCanceledException)
        {
            // 同上，取消不记错误。
        }
        catch (Exception ex)
        {
            if (generation == _cardImageGeneration)
            {
                _errors.Report(ex, "SpaceDock 加载模组封面");
            }
        }
    }

    private void RebuildDuplicates()
    {
        _duplicates = SpaceDockDuplicateMatcher.Create(_repositories.Current, _registry.Current);
    }

    private async Task LoadDetailAsync(SpaceDockModInfo? mod)
    {
        var request = ++_detailRequest;

        if (mod is null)
        {
            ClearDetail();
            return;
        }

        if (_loadedDetailId == mod.Id)
        {
            return;
        }

        _loadedDetailId = mod.Id;
        IsDetailLoading = true;
        FillDetail(mod);

        var ct = _activationCts?.Token ?? CancellationToken.None;
        try
        {
            var detailResult = await _spaceDock.GetModAsync(mod.Id, ct);
            if (request != _detailRequest)
            {
                return;
            }

            if (detailResult.IsSuccess && detailResult.Value is { } detail)
            {
                FillDetail(detail);
            }
            else if (!detailResult.IsSuccess)
            {
                _errors.Report(
                    "SpaceDock 读取模组详情",
                    detailResult.Message ?? "详情加载失败",
                    detailResult.Detail);
            }

            var imageResult = await _images.FetchScreenshotAsync(DetailUrl, ct);
            if (request != _detailRequest)
            {
                return;
            }

            if (imageResult.IsSuccess && imageResult.Value is { Length: > 0 } bytes)
            {
                using var stream = new MemoryStream(bytes);
                DisposeScreenshot();
                Screenshot = new Bitmap(stream);
            }
        }
        catch (OperationCanceledException)
        {
            // 页面切走或选中项变化，属于正常取消。
        }
        catch (Exception ex)
        {
            if (request == _detailRequest)
            {
                _errors.Report(ex, "SpaceDock 读取模组详情");
            }
        }
        finally
        {
            if (request == _detailRequest)
            {
                IsDetailLoading = false;
            }
        }
    }

    private void FillDetail(SpaceDockModInfo mod)
    {
        DetailName = mod.Name;
        DetailAuthorText = Text("作者：{0}", LocalizationKeys.SpaceDockAuthor, string.IsNullOrWhiteSpace(mod.Author) ? Text("未知", LocalizationKeys.SpaceDockUnknown) : mod.Author);
        DetailStatsText = Text(
            "下载 {0} · 关注 {1}",
            LocalizationKeys.SpaceDockDetailStats,
            mod.Downloads.ToString("N0", CultureInfo.CurrentCulture),
            mod.Followers.ToString("N0", CultureInfo.CurrentCulture));
        DetailLicenseText = Text(
            "许可证：{0}",
            LocalizationKeys.SpaceDockLicense,
            string.IsNullOrWhiteSpace(mod.License) ? Text("未知", LocalizationKeys.SpaceDockUnknown) : mod.License);
        DetailDescription = ToPlainText(mod.Description, mod.ShortDescription);
        DetailUrl = mod.Url;
        Versions = mod.Versions;
        SelectedVersion = mod.LatestVersion;
    }

    private void ClearDetail()
    {
        _detailRequest++;
        _loadedDetailId = null;
        DetailName = string.Empty;
        DetailAuthorText = string.Empty;
        DetailStatsText = string.Empty;
        DetailLicenseText = string.Empty;
        DetailDescription = string.Empty;
        DetailUrl = string.Empty;
        Versions = Array.Empty<SpaceDockVersionInfo>();
        SelectedVersion = null;
        DisposeScreenshot();
        IsDetailLoading = false;
    }

    private void DisposeScreenshot()
    {
        var image = Screenshot;
        Screenshot = null;
        image?.Dispose();
    }

    private async Task RunBusyAsync(string busyText, Func<CancellationToken, Task> action, CancellationToken? externalToken = null)
    {
        if (IsBusy)
        {
            return;
        }

        SetBusy(true, busyText);
        var ct = externalToken ?? _activationCts?.Token ?? CancellationToken.None;
        try
        {
            await action(ct);
        }
        catch (OperationCanceledException)
        {
            // 页面失活：保留上一次成功结果，不弹错误。
        }
        catch (Exception ex)
        {
            SetError(ex, Text("加载 SpaceDock 失败", LocalizationKeys.SpaceDockLoadFailed));
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool value, string? text = null)
    {
        if (text is not null)
        {
            BusyText = text;
        }

        IsBusy = value;
        OnPropertyChanged(nameof(CanDownloadSelection));
        OnPropertyChanged(nameof(CanInstallSelection));
        OnPropertyChanged(nameof(ShowEmptyState));
        RaiseCommandStates();
    }

    private void SetApiError<T>(string message, OperationResult<T> result) =>
        SetApiError(message, OperationResult.From(result));

    private void SetApiError(string message, OperationResult? result)
    {
        ErrorText = message;
        ErrorDetail = string.IsNullOrWhiteSpace(result?.Detail)
            ? result?.Message
            : string.IsNullOrWhiteSpace(result.Message)
                ? result.Detail
                : $"{result.Message}\n{result.Detail}";
        _errors.Report("SpaceDock", message, ErrorDetail);
        RaiseCommandStates();
    }

    private void SetError(Exception ex, string message)
    {
        ErrorText = message;
        ErrorDetail = ex.Message;
        _errors.Report(ex, "SpaceDock", message);
    }

    private void ClearError()
    {
        ErrorText = null;
        ErrorDetail = null;
    }

    private void OnCurrentInstanceChanged(InstanceInfo? instance)
    {
        OnPropertyChanged(nameof(HasInstance));
        OnPropertyChanged(nameof(CanInstallSelection));
        RaiseCommandStates();
    }

    private void RaiseCommandStates()
    {
        RefreshCommand.NotifyCanExecuteChanged();
        SearchCommand.NotifyCanExecuteChanged();
        ClearSearchCommand.NotifyCanExecuteChanged();
        PreviousPageCommand.NotifyCanExecuteChanged();
        NextPageCommand.NotifyCanExecuteChanged();
        DownloadCommand.NotifyCanExecuteChanged();
        InstallCommand.NotifyCanExecuteChanged();
        OpenModPageCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task RefreshAsync()
    {
        await RunBusyAsync(
            Text("正在加载 SpaceDock…", LocalizationKeys.SpaceDockLoading),
            async ct =>
            {
                await LoadIndexesAsync(ct);
                RebuildDuplicates();

                if (_gameId <= 0)
                {
                    await ResolveGameAsync(ct);
                }

                if (_gameId <= 0)
                {
                    return;
                }

                if (IsSearchMode && !string.IsNullOrWhiteSpace(SearchText))
                {
                    await FetchSearchAsync(SearchText.Trim(), ct);
                }
                else
                {
                    await FetchBrowseAsync(PageNumber, ct);
                }
            });
    }

    private bool CanRefresh() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanSearch))]
    private async Task SearchAsync()
    {
        var query = SearchText.Trim();
        if (query.Length == 0)
        {
            await ClearSearchAsync();
            return;
        }

        await RunBusyAsync(
            Text("正在加载 SpaceDock…", LocalizationKeys.SpaceDockLoading),
            async ct =>
            {
                if (_gameId <= 0)
                {
                    await ResolveGameAsync(ct);
                    if (_gameId <= 0)
                    {
                        return;
                    }
                }

                await FetchSearchAsync(query, ct);
            });
    }

    private bool CanSearch() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanClearSearch))]
    private async Task ClearSearchAsync()
    {
        SearchText = string.Empty;
        IsSearchMode = false;
        await RunBusyAsync(
            Text("正在加载 SpaceDock…", LocalizationKeys.SpaceDockLoading),
            ct => FetchBrowseAsync(1, ct));
    }

    private bool CanClearSearch() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanGoPrevious))]
    private async Task PreviousPageAsync()
    {
        await RunBusyAsync(
            Text("正在加载 SpaceDock…", LocalizationKeys.SpaceDockLoading),
            ct => FetchBrowseAsync(PageNumber - 1, ct));
    }

    private bool CanGoPrevious() => !IsBusy && !IsSearchMode && PageNumber > 1;

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private async Task NextPageAsync()
    {
        await RunBusyAsync(
            Text("正在加载 SpaceDock…", LocalizationKeys.SpaceDockLoading),
            ct => FetchBrowseAsync(PageNumber + 1, ct));
    }

    private bool CanGoNext() => !IsBusy && !IsSearchMode && PageNumber < TotalPages;

    [RelayCommand(CanExecute = nameof(CanDownload))]
    private async Task DownloadAsync()
    {
        if (SelectedMod?.Mod is not { } mod || SelectedVersion is not { } version)
        {
            return;
        }

        SetBusy(true, Text("正在下载 {0}…", LocalizationKeys.SpaceDockStatusDownloading, $"{mod.Name} {version.FriendlyVersion}"));
        OperationResult<string> result;
        try
        {
            var directory = ResolveDownloadDirectory();
            result = await _installer.DownloadAsync(
                version,
                directory,
                $"{mod.Name}-{version.FriendlyVersion}",
                _activationCts?.Token ?? CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            SetBusy(false);
            return;
        }
        catch (Exception ex)
        {
            SetBusy(false);
            SetError(ex, Text("下载失败", LocalizationKeys.SpaceDockDownloadFailed));
            return;
        }

        SetBusy(false);
        if (!result.IsSuccess || string.IsNullOrEmpty(result.Value))
        {
            SetApiError(Text("下载失败", LocalizationKeys.SpaceDockDownloadFailed), result);
            return;
        }

        ClearError();
        await _dialogs.ShowMessageAsync(
            Text("下载完成", LocalizationKeys.SpaceDockDownloadTitle),
            Text("已保存到：{0}", LocalizationKeys.SpaceDockDownloadBody, result.Value));
    }

    private bool CanDownload() => !IsBusy && SelectedVersion is not null;

    [RelayCommand(CanExecute = nameof(CanInstall))]
    private async Task InstallAsync()
    {
        if (SelectedMod?.Mod is not { } mod || SelectedVersion is not { } version)
        {
            return;
        }

        var instance = _instances.Current;
        if (instance is null)
        {
            SetApiError(Text("当前没有选择游戏实例，安装按钮不可用。", LocalizationKeys.SpaceDockNoInstance), null);
            return;
        }

        if (_instances.IsGameRunning(instance.Name))
        {
            var proceed = await _dialogs.ConfirmAsync(
                Text("游戏正在运行", LocalizationKeys.SpaceDockInstallRunningTitle),
                Text(
                    "检测到游戏正在运行。现在写入 GameData 可能与游戏争抢文件。仍要继续？",
                    LocalizationKeys.SpaceDockInstallRunningBody));
            if (!proceed)
            {
                return;
            }
        }

        var archive = string.Empty;
        try
        {
            SetBusy(true, Text("正在下载 {0}…", LocalizationKeys.SpaceDockStatusDownloading, mod.Name));
            var tempDirectory = Path.Combine(Path.GetTempPath(), "ckanui-spacedock");
            Directory.CreateDirectory(tempDirectory);
            var downloaded = await _installer.DownloadAsync(
                version,
                tempDirectory,
                $"{mod.Name}-{version.FriendlyVersion}",
                _activationCts?.Token ?? CancellationToken.None);
            if (!downloaded.IsSuccess || string.IsNullOrEmpty(downloaded.Value))
            {
                SetBusy(false);
                SetApiError(Text("下载失败", LocalizationKeys.SpaceDockDownloadFailed), downloaded);
                return;
            }

            archive = downloaded.Value;
            var inspected = await _installer.InspectAsync(
                archive,
                mod.Name,
                _activationCts?.Token ?? CancellationToken.None);
            if (!inspected.IsSuccess || inspected.Value is null)
            {
                SetBusy(false);
                SetApiError(Text("安装失败", LocalizationKeys.SpaceDockInstallFailed), inspected);
                return;
            }

            SetBusy(false);
            var confirmed = await _dialogs.ConfirmAsync(
                Text("安装到实例", LocalizationKeys.SpaceDockInstallTitle),
                Text(
                    "将把「{0}」{1} 安装到实例「{2}」。压缩包布局：{3}。这条链路不经过 CKAN：不会写入 CKAN 注册表，也不会做依赖解析。继续？",
                    LocalizationKeys.SpaceDockInstallBody,
                    mod.Name,
                    version.FriendlyVersion,
                    instance.DisplayName,
                    DescribeInstallPlan(inspected.Value)),
                mod.Url);
            if (!confirmed)
            {
                return;
            }

            SetBusy(true, Text("正在安装 {0}…", LocalizationKeys.SpaceDockStatusInstalling, mod.Name));
            var installed = await _installer.InstallAsync(
                archive,
                instance.GameDir,
                mod.Name,
                _activationCts?.Token ?? CancellationToken.None);
            SetBusy(false);

            if (!installed.IsSuccess || string.IsNullOrEmpty(installed.Value))
            {
                SetApiError(Text("安装失败", LocalizationKeys.SpaceDockInstallFailed), installed);
                return;
            }

            ClearError();
            await _dialogs.ShowMessageAsync(
                Text("安装完成", LocalizationKeys.SpaceDockInstallDoneTitle),
                Text(
                    "已安装到：{0}。提示：该模组不会出现在 CKAN 的已安装列表里。",
                    LocalizationKeys.SpaceDockInstallDoneBody,
                    installed.Value));
        }
        catch (OperationCanceledException)
        {
            SetBusy(false);
        }
        catch (Exception ex)
        {
            SetBusy(false);
            SetError(ex, Text("安装失败", LocalizationKeys.SpaceDockInstallFailed));
        }
        finally
        {
            if (!string.IsNullOrEmpty(archive))
            {
                TryDelete(archive);
            }
        }
    }

    private string DescribeInstallPlan(SpaceDockInstallPlan plan)
    {
        var count = plan.FileCount.ToString("N0", CultureInfo.CurrentCulture);
        return plan.Strategy switch
        {
            SpaceDockInstallStrategy.GameDataTree or SpaceDockInstallStrategy.SingleFolder =>
                Text(
                    "GameData/{0}，共 {1} 个文件",
                    LocalizationKeys.SpaceDockInstallLayoutGameData,
                    plan.InstalledRoot,
                    count),
            SpaceDockInstallStrategy.FlatPayload =>
                Text(
                    "GameData/{0}（压缩包没有统一目录，已自动收拢），共 {1} 个文件",
                    LocalizationKeys.SpaceDockInstallLayoutFlatPayload,
                    plan.InstalledRoot,
                    count),
            SpaceDockInstallStrategy.GameRootTree =>
                Text(
                    "游戏根目录（Ships/Missions 等），共 {0} 个文件",
                    LocalizationKeys.SpaceDockInstallLayoutGameRoot,
                    count),
            SpaceDockInstallStrategy.MixedRoots =>
                Text(
                    "GameData/{0} + 游戏根目录，共 {1} 个文件",
                    LocalizationKeys.SpaceDockInstallLayoutMixed,
                    plan.InstalledRoot,
                    count),
            _ => Text("未知", LocalizationKeys.SpaceDockUnknown),
        };
    }

    private bool CanInstall() => !IsBusy && SelectedVersion is not null && _instances.Current is not null;

    [RelayCommand]
    private void OpenSite()
    {
        OpenUrl("https://spacedock.info/kerbal-space-program");
    }

    [RelayCommand(CanExecute = nameof(CanOpenModPage))]
    private void OpenModPage()
    {
        if (SelectedMod is { } item)
        {
            OpenUrl(item.Url);
        }
    }

    private bool CanOpenModPage() => SelectedMod is not null;

    private void OpenUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            SetApiError(Text("加载 SpaceDock 失败", LocalizationKeys.SpaceDockLoadFailed), null);
            return;
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            _errors.Report(ex, "SpaceDock 打开浏览器");
            SetError(ex, Text("加载 SpaceDock 失败", LocalizationKeys.SpaceDockLoadFailed));
        }
    }

    private static string ResolveDownloadDirectory()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var directory = string.IsNullOrEmpty(profile)
            ? Path.GetTempPath()
            : Path.Combine(profile, "Downloads");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // 临时文件删不掉不影响安装结果。
        }
        catch (UnauthorizedAccessException)
        {
            // 同上。
        }
    }

    private static string ToPlainText(string? description, string? fallback)
    {
        var text = string.IsNullOrWhiteSpace(description) ? fallback : description;
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        text = HtmlTagPattern.Replace(text, " ");
        text = BbCodePattern.Replace(text, " ");
        text = WebUtility.HtmlDecode(text);
        text = HorizontalSpacePattern.Replace(text, " ");
        text = BlankLinePattern.Replace(text, "\n\n");
        text = text.Trim();

        return text.Length <= DescriptionLimit
            ? text
            : string.Concat(text.AsSpan(0, DescriptionLimit), "…");
    }
}