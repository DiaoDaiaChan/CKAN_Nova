using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.ViewModels.Navigation;
using CkanUI.ViewModels.Pages;
using CkanUI.ViewModels.Pages.Settings;
using CkanUI.ViewModels.Relations;

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
    private readonly IChangeSetService? _changeSets;
    private readonly IChangeRequestSink? _changeSink;
    private readonly INavigationService? _navigation;
    private readonly ISpacedockImageService? _spacedockImageService;
    private readonly CkanUI.ModText.IModTextService? _modText;
    private CkanUI.ModText.IModTextProvider? _textProvider;
    private bool _textProviderOverridden;
    private string? _identifier;
    private ModRowData? _row;
    private InstalledModuleInfo? _installed;
    private ModDetailsSnapshot? _details;
    private RelationTreeViewModel? _relationTree;

    /// <summary>创建详情面板 VM。</summary>
    /// <param name="repository">仓库服务（列表行回查）。</param>
    /// <param name="registry">实例注册表服务（已装版本回查）。</param>
    /// <param name="changeSets">
    /// 变更集服务（F-30 指定版本安装）。可为 null：未接线时版本页的安装按钮只给出「没有可用服务」的提示，
    /// 详情浏览本身不受影响（与 <c>DetailsProvider</c> 同一套可选接线思路，保持既有 2 参构造调用零改动）。
    /// </param>
    /// <param name="changeSink">变更集页落点（可选）：算好预览后把请求灌给它，由变更集页确认安装。</param>
    /// <param name="navigation">页面导航服务（可选）：页签四跳转全局 ZIP 内容预览页（F-29）。</param>
    /// <param name="spacedockImageService">SpaceDock 截图抓取服务（可选）：按需懒加载模组截图与背景预览。</param>
    /// <param name="modText">
    /// 模组元数据本地化服务（可选，DI 已注册）：只取它的 <see cref="CkanUI.ModText.IModTextService.Provider"/> 做译文查找，
    /// 缺省时摘要/描述逐字节等于改动前的英文原文行为（headless 测试与未接线宿主零影响）。
    /// </param>
    public ModDetailsViewModel(
        IRepositoryService repository,
        IRegistryService registry,
        IChangeSetService? changeSets = null,
        IChangeRequestSink? changeSink = null,
        INavigationService? navigation = null,
        ISpacedockImageService? spacedockImageService = null,
        CkanUI.ModText.IModTextService? modText = null)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(registry);

        _repository = repository;
        _registry = registry;
        _changeSets = changeSets;
        _changeSink = changeSink;
        _navigation = navigation;
        _spacedockImageService = spacedockImageService;
        _modText = modText;
    }

    private bool _isUpdatingTolerance;
    private StabilityToleranceOption? _selectedStabilityToleranceOverride;

    /// <summary>
    /// 富字段提供者（按 Identifier 返回详情投影）。App 侧用 Core 的 <c>CkanModule</c> 适配；
    /// 为 null 时详情面板只显示快照里已有的字段。用属性而不是构造参数，避免要求容器注册新契约。
    /// </summary>
    public Func<string, ModDetailsSnapshot?>? DetailsProvider { get; set; }

    /// <summary>
    /// 译文查找器（属性注入，App 手动接线与测试用）。getter 返回构造注入的
    /// <see cref="CkanUI.ModText.IModTextService.Provider"/>（服务未注入时为 null）；
    /// setter 覆盖该结果——显式 set null 表示关闭译文查找，不回退到注入的服务——并立刻刷新派生属性，
    /// 否则换绑后 UI 还挂着上一份 provider 的译文。
    /// </summary>
    public CkanUI.ModText.IModTextProvider? TextProvider
    {
        get => _textProviderOverridden ? _textProvider : _modText?.Provider;
        set
        {
            _textProvider = value;
            _textProviderOverridden = true;
            Refresh();
        }
    }

    /// <summary>
    /// 模组详情面板外壳启动器（打开浏览器等）。由视图在 DataContext 变化时注入。
    /// </summary>
    public IModDetailsShellLauncher? ShellLauncher { get; set; }

    /// <summary>
    /// 单模组稳定性容忍度覆盖选项列表（继承全局默认 / stable / testing / development）。
    /// </summary>
    public IReadOnlyList<StabilityToleranceOption> StabilityToleranceOptions { get; } =
    [
        new(null, "继承全局默认"),
        new("stable", "稳定版 (stable)"),
        new("testing", "测试版 (testing)"),
        new("development", "开发版 (development)"),
    ];

    /// <summary>
    /// 稳定性容忍度覆盖区块标题。
    /// </summary>
    public string StabilityToleranceHeaderText => "稳定性容忍度覆盖";

    /// <summary>
    /// 当前选中的单模组稳定性容忍度覆盖选项。
    /// </summary>
    public StabilityToleranceOption? SelectedStabilityToleranceOverride
    {
        get => _selectedStabilityToleranceOverride;
        set
        {
            if (Equals(_selectedStabilityToleranceOverride, value))
            {
                return;
            }

            _selectedStabilityToleranceOverride = value;
            OnPropertyChanged();

            if (_isUpdatingTolerance || _identifier is null)
            {
                return;
            }

            var tolerance = value?.Value;
            if (ToleranceOverrideSetter is not null)
            {
                ToleranceOverrideSetter(_identifier, tolerance);
            }
            else if (!string.IsNullOrEmpty(GameDirectory))
            {
                _ = new StabilityToleranceFile().SetModTolerance(GameDirectory, _identifier, tolerance);
            }

            NotifyModRefreshed?.Invoke(_identifier);
        }
    }

    /// <summary>
    /// 单模组容差覆盖读取提供者（按 Identifier 回查当前模组的容差覆盖；返回 null 表示继承全局）。
    /// </summary>
    public Func<string, string?>? ToleranceOverrideProvider { get; set; }

    /// <summary>
    /// 单模组容差覆盖设置委托（Identifier, tolerance? -> void）。
    /// </summary>
    public Action<string, string?>? ToleranceOverrideSetter { get; set; }

    /// <summary>
    /// 模组状态刷新通知委托（当单模组容差切换后触发，通知外部模组列表重新投影或刷新兼容性状态）。
    /// </summary>
    public Action<string>? NotifyModRefreshed { get; set; }

    /// <summary>
    /// 当前游戏实例根目录（若未配置 ToleranceOverrideProvider/Setter，则直接用 StabilityToleranceFile 读写该目录）。
    /// </summary>
    public string? GameDirectory { get; set; }

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

    /// <summary>摘要字段在翻译包里的键（与 i18n 侧约定一致，别改）。</summary>
    private const string AbstractField = "abstract";

    /// <summary>描述字段在翻译包里的键（与 i18n 侧约定一致，别改）。</summary>
    private const string DescriptionField = "description";

    /// <summary>
    /// 一句话摘要（Core 的 abstract）。
    /// 取值语义（别改回去）：原始英文 → <c>TextProvider.Find(Identifier, "abstract", 原始英文)</c> →
    /// 命中且译文非空白时用 <see cref="CkanUI.ModText.ModText.Text"/>，否则回落原始英文。
    /// <see cref="Identifier"/> 或 <see cref="TextProvider"/> 为 null 时取到的字符串与接入译文前逐字节一致，
    /// 这样 headless 测试与未接线宿主看到的仍是纯英文快照。
    /// </summary>
    public string AbstractText
    {
        get
        {
            var original = _details?.Abstract ?? string.Empty;
            return FindTranslation(original, AbstractField)?.Text ?? original;
        }
    }

    /// <summary>是否有摘要（按最终显示文本判断，与 <see cref="AbstractText"/> 口径一致）。</summary>
    public bool HasAbstract => !string.IsNullOrWhiteSpace(AbstractText);

    /// <summary>
    /// 描述（Markdown 原文，渲染交给视图；缺失时占位）。
    /// 取值语义与 <see cref="AbstractText"/> 同构，只是字段名换成 <c>description</c>：
    /// 命中译文用译文，未命中回落原文，原文为空才显示「暂无描述」。
    /// </summary>
    public string DescriptionText
    {
        get
        {
            var original = _details?.Description;
            var translated = FindTranslation(original, DescriptionField);
            if (translated is not null)
            {
                return translated.Text;
            }

            return string.IsNullOrWhiteSpace(original) ? "暂无描述" : original!;
        }
    }

    /// <summary>摘要是否命中译文（命中才显示「已译」徽标）。</summary>
    public bool IsAbstractTranslated => FindTranslation(_details?.Abstract, AbstractField) is not null;

    /// <summary>描述是否命中译文（命中才显示「已译」徽标）。</summary>
    public bool IsDescriptionTranslated => FindTranslation(_details?.Description, DescriptionField) is not null;

    /// <summary>摘要译文是否已过期（原文更新过，译文对应旧版）。</summary>
    public bool IsAbstractStale => FindTranslation(_details?.Abstract, AbstractField)?.Stale == true;

    /// <summary>描述译文是否已过期（原文更新过，译文对应旧版）。</summary>
    public bool IsDescriptionStale => FindTranslation(_details?.Description, DescriptionField)?.Stale == true;

    /// <summary>是否存在任一带过期标记的译文。</summary>
    public bool HasStaleTranslation => IsAbstractStale || IsDescriptionStale;

    /// <summary>过期译文提示文案（无过期译文时为空串，视图按 <see cref="HasStaleTranslation"/> 显隐）。</summary>
    public string StaleTranslationNotice => HasStaleTranslation ? "译文对应的是旧版原文，英文内容已更新" : string.Empty;

    /// <summary>译文徽标文案（摘要与描述共用，XAML 不写死中文）。</summary>
    public string TranslationBadgeText => "已译";

    /// <summary>
    /// 按当前 Identifier 查译文：Identifier 为空、原文为空、无 provider 都直接返回 null（即回落原文）。
    /// provider 约定为纯函数无 IO，所以每次取值现查，不做缓存——缓存一旦失效就会显示过期译文。
    /// </summary>
    private CkanUI.ModText.ModText? FindTranslation(string? sourceText, string field)
    {
        if (_identifier is null || string.IsNullOrWhiteSpace(sourceText))
        {
            return null;
        }

        var provider = TextProvider;
        if (provider is null)
        {
            return null;
        }

        var hit = provider.Find(_identifier, field, sourceText);

        // 空译文等同未命中：宁可显示英文原文，也不能把有内容的字段翻成空白。
        return string.IsNullOrWhiteSpace(hit?.Text) ? null : hit;
    }

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

    /// <summary>SpaceDock 网页链接（空表示不是 SpaceDock 模组）。</summary>
    public string SpacedockUrl => _details?.SpacedockUrl ?? string.Empty;

    /// <summary>是否有 SpaceDock 链接。</summary>
    public bool HasSpacedockUrl => !string.IsNullOrWhiteSpace(SpacedockUrl);

    private byte[]? _spacedockImageBytes;
    private bool _isLoadingSpacedockImage;
    private string? _spacedockImageError;

    /// <summary>已下载的 SpaceDock 截图原始字节（未加载或加载失败时为 null）。</summary>
    public byte[]? SpacedockImageBytes
    {
        get => _spacedockImageBytes;
        private set
        {
            if (ReferenceEquals(_spacedockImageBytes, value)) return;
            _spacedockImageBytes = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSpacedockImage));
        }
    }

    /// <summary>是否已成功加载截图。</summary>
    public bool HasSpacedockImage => _spacedockImageBytes is not null && _spacedockImageBytes.Length > 0;

    /// <summary>是否正在拉取 SpaceDock 截图。</summary>
    public bool IsLoadingSpacedockImage
    {
        get => _isLoadingSpacedockImage;
        private set
        {
            if (_isLoadingSpacedockImage == value) return;
            _isLoadingSpacedockImage = value;
            OnPropertyChanged();
            LoadSpacedockImageCommand.NotifyCanExecuteChanged();
        }
    }

    /// <summary>SpaceDock 截图加载失败时的错误文案（成功或未加载时为空）。</summary>
    public string SpacedockImageError
    {
        get => _spacedockImageError ?? string.Empty;
        private set
        {
            if (string.Equals(_spacedockImageError, value, StringComparison.Ordinal)) return;
            _spacedockImageError = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSpacedockImageError));
        }
    }

    /// <summary>是否有截图加载错误。</summary>
    public bool HasSpacedockImageError => !string.IsNullOrWhiteSpace(SpacedockImageError);

    /// <summary>SpaceDock 卡片标题。</summary>
    public string SpacedockSectionTitle => "SpaceDock 页面";

    /// <summary>引导前往 SpaceDock 官方界面的按钮文案。</summary>
    public string SpacedockOpenButtonText => "前往 SpaceDock 页面 ↗";

    /// <summary>拉取 SpaceDock 截图的按钮文案（已关闭外部抓取，保留接口兼容）。</summary>
    public string SpacedockLoadButtonText => "加载 SpaceDock 截图";

    /// <summary>SpaceDock 页面引导提示文案（免爬取外链，直达官方）。</summary>
    public string SpacedockLazyHintText => "点击按钮在浏览器中打开该模组在 SpaceDock 的官方主页，浏览完整截图、图文详情与社区发布。";

    /// <summary>正在加载 SpaceDock 截图的提示文案。</summary>
    public string SpacedockLoadingText => "正在从 SpaceDock 加载截图...";

    /// <summary>重试拉取 SpaceDock 截图的按钮文案。</summary>
    public string SpacedockRetryButtonText => "重试加载";

    /// <summary>模组核心分类定义（8 大分类系统）。</summary>
    public CkanUI.ModText.ModCategoryDefinition? Category =>
        _identifier is not null
            ? CkanUI.ModText.ModTaxonomyEngine.Classify(_identifier, Name, Tags, AbstractText, DescriptionText)
            : null;

    /// <summary>核心分类徽章展示文案：🎨 视觉与音效美化</summary>
    public string CategoryBadgeText => Category?.DisplayTitle ?? string.Empty;

    /// <summary>核心分类描述说明</summary>
    public string CategoryDescription => Category?.Description ?? string.Empty;

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


    /// <summary>页签数量（元数据与简介 / 依赖关系 / 全部版本 / 文件与内容）。</summary>
    public const int TabCount = 4;

    /// <summary>全局 ZIP 内容预览页的导航目标键，与 <c>ServiceRegistration</c> 的页面表保持一致。</summary>
    public const string ContentPreviewPageKey = "content";

    private int _selectedTabIndex;

    /// <summary>
    /// 当前页签索引：0=元数据与简介，1=依赖关系，2=全部版本（F-30），3=文件与内容（F-26/F-29）。
    /// 越界值自动收敛，避免宿主或测试塞进非法下标后视图空白。
    /// </summary>
    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set
        {
            var clamped = value < 0 ? 0 : value >= TabCount ? TabCount - 1 : value;
            if (clamped == _selectedTabIndex)
            {
                return;
            }

            _selectedTabIndex = clamped;
            OnPropertyChanged();
        }
    }

    /// <summary>元数据页签标题。</summary>
    public string MetadataTabHeaderText => "元数据与简介";

    /// <summary>依赖关系页签标题（有关系时带计数角标）。</summary>
    public string RelationsTabHeaderText =>
        RelationsTotal > 0 ? "依赖关系 (" + RelationsTotal + ")" : "依赖关系";

    /// <summary>全部版本页签标题（有版本时带计数角标）。</summary>
    public string VersionsTabHeaderText =>
        HasVersions ? "全部版本 (" + Versions.Count + ")" : "全部版本";

    /// <summary>文件与内容页签标题（有已装文件时带计数角标）。</summary>
    public string ContentsTabHeaderText =>
        HasInstalledFiles ? "文件与内容 (" + InstalledFiles.Count + ")" : "文件与内容";

    /// <summary>依赖 + 推荐 + 冲突的关系总数，用于页签计数角标。</summary>
    public int RelationsTotal => Dependencies.Count + Recommendations.Count + Conflicts.Count;

    /// <summary>是否有任意关系（依赖 / 推荐 / 冲突）。</summary>
    public bool HasRelations => RelationsTotal > 0;

    /// <summary>交互式依赖关系树视图模型（F-28 / UI-18）。</summary>
    public RelationTreeViewModel? RelationTree => _relationTree;

    /// <summary>
    /// 全部版本列表（F-30）。数据由 <see cref="ModDetailsSnapshot.Versions"/> 提供；
    /// 快照可能来自缓存而没标 IsInstalled，这里按当前实例的已装版本补齐"当前已安装"标记。
    /// </summary>
    public IReadOnlyList<ModVersionItemDto> Versions
    {
        get
        {
            var source = _details?.Versions;
            if (source is null || source.Count == 0)
            {
                return Array.Empty<ModVersionItemDto>();
            }

            var installedVersion = FirstNonEmpty(_installed?.Version, _row?.InstalledVersion);
            if (string.IsNullOrWhiteSpace(installedVersion))
            {
                return source;
            }

            List<ModVersionItemDto>? patched = null;
            for (var index = 0; index < source.Count; index++)
            {
                var item = source[index];
                if (item.IsInstalled ||
                    !string.Equals(item.Version, installedVersion, StringComparison.Ordinal))
                {
                    continue;
                }

                patched ??= new List<ModVersionItemDto>(source);
                patched[index] = item with { IsInstalled = true };
            }

            return patched ?? source;
        }
    }

    /// <summary>是否有版本记录。</summary>
    public bool HasVersions => Versions.Count > 0;

    /// <summary>版本计数文案。</summary>
    public string VersionsCountText =>
        HasVersions ? "共 " + Versions.Count + " 个版本" : "暂无版本记录";

    /// <summary>版本页空状态文案。</summary>
    public string VersionsEmptyText => "该模组暂无可用版本记录。";

    /// <summary>已装文件清单（相对游戏实例根目录）。</summary>
    public IReadOnlyList<string> InstalledFiles =>
        _details?.InstalledFiles ?? Array.Empty<string>();

    /// <summary>内容页列表行（把路径拆成文件名 + 所在目录，供视图两行显示）。</summary>
    public IReadOnlyList<InstalledFileItem> InstalledFileRows
    {
        get
        {
            var source = _details?.InstalledFiles;
            if (source is null || source.Count == 0)
            {
                return Array.Empty<InstalledFileItem>();
            }

            var rows = new InstalledFileItem[source.Count];
            for (var index = 0; index < source.Count; index++)
            {
                rows[index] = new InstalledFileItem(source[index]);
            }

            return rows;
        }
    }

    /// <summary>是否有已装文件记录。</summary>
    public bool HasInstalledFiles => InstalledFiles.Count > 0;

    /// <summary>已装文件计数文案。</summary>
    public string InstalledFilesCountText =>
        HasInstalledFiles ? "共 " + InstalledFiles.Count + " 个文件" : "暂无文件记录";

    /// <summary>内容页空状态文案。</summary>
    public string ContentsEmptyText => "暂无已装文件记录。";

    /// <summary>关系页空状态文案。</summary>
    public string RelationsEmptyText => "该模组没有声明依赖、推荐或冲突关系。";

    /// <summary>内容预览按钮文案。</summary>
    public string ContentPreviewButtonText => "打开内容预览";

    /// <summary>内容页引导文案（按是否已装给不同说明）。</summary>
    public string ContentPreviewHintText => IsInstalled
        ? "下面是本实例里该模组的已装文件；也可打开全局内容预览页查看压缩包内的完整结构。"
        : "该模组尚未安装，可打开全局内容预览页查看压缩包内的文件结构。";

    /// <summary>
    /// 外部跳转委托（模组标识 → void）。未接入 <see cref="INavigationService"/> 的宿主用它接管
    /// 内容预览跳转（例如主窗口自己切页或弹窗），保证 VM 不依赖 Avalonia 与具体页面。
    /// </summary>
    public Action<string>? ExternalNavigateAction { get; set; }

    private string? _installStatusText;

    /// <summary>版本安装的状态反馈文案（未发起过安装时为空）。</summary>
    public string InstallStatusText
    {
        get => _installStatusText ?? string.Empty;
        private set
        {
            if (string.Equals(_installStatusText, value, StringComparison.Ordinal))
            {
                return;
            }

            _installStatusText = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasInstallStatusText));
        }
    }

    /// <summary>是否有安装状态反馈文案。</summary>
    public bool HasInstallStatusText => !string.IsNullOrWhiteSpace(InstallStatusText);

    private bool _installStatusIsError;

    /// <summary>安装状态反馈是否为失败（视图据此把提示横幅换成危险色）。</summary>
    public bool InstallStatusIsError
    {
        get => _installStatusIsError;
        private set
        {
            if (_installStatusIsError == value)
            {
                return;
            }

            _installStatusIsError = value;
            OnPropertyChanged();
        }
    }

    private ChangeSetPreview? _lastChangeSet;

    /// <summary>
    /// 最近一次"安装指定版本"生成的变更集预览（供变更集页确认安装）。
    /// 生成失败或换选中模组时清空，避免显示上一轮的陈旧结果。
    /// </summary>
    public ChangeSetPreview? LastChangeSet
    {
        get => _lastChangeSet;
        private set
        {
            if (ReferenceEquals(_lastChangeSet, value))
            {
                return;
            }

            _lastChangeSet = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasLastChangeSet));
        }
    }

    /// <summary>是否有最近一次生成的变更集预览。</summary>
    public bool HasLastChangeSet => _lastChangeSet is not null;

    private bool _isInstalling;

    /// <summary>是否正在生成变更集（安装按钮据此禁用，防连点重复计算）。</summary>
    public bool IsInstalling
    {
        get => _isInstalling;
        private set
        {
            if (_isInstalling == value)
            {
                return;
            }

            _isInstalling = value;
            OnPropertyChanged();
            InstallVersionCommand.NotifyCanExecuteChanged();
        }
    }

    private string? _contentNavigationStatusText;

    /// <summary>内容预览跳转的状态反馈文案（未触发过跳转时为空）。</summary>
    public string ContentNavigationStatusText
    {
        get => _contentNavigationStatusText ?? string.Empty;
        private set
        {
            if (string.Equals(_contentNavigationStatusText, value, StringComparison.Ordinal))
            {
                return;
            }

            _contentNavigationStatusText = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasContentNavigationStatusText));
        }
    }

    /// <summary>是否有内容预览跳转的状态反馈文案。</summary>
    public bool HasContentNavigationStatusText =>
        !string.IsNullOrWhiteSpace(ContentNavigationStatusText);

    /// <summary>选中一个模组（按 Identifier 回查快照；null 表示清空）。</summary>
    public void Select(string? identifier)
    {
        var previous = _identifier;
        _identifier = string.IsNullOrWhiteSpace(identifier) ? null : identifier;
        ResetTransientState(previous);
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

        var previous = _identifier;
        _identifier = string.IsNullOrWhiteSpace(details.Identifier) ? null : details.Identifier;
        _details = details;
        ResetTransientState(previous);
        Reload();
    }

    /// <summary>
    /// 回填「全部版本」页签的数据（F-30）。
    /// 详情快照由列表行同步拼出，版本集合却要额外查一次仓库索引，所以由页面异步取到后再回填：
    /// <paramref name="versions"/> 为 <c>null</c> 或空集合表示该模组没有版本记录（页签走空态）。
    /// </summary>
    public void ApplyVersions(IReadOnlyList<ModVersionItemDto>? versions)
    {
        if (_details is null)
        {
            return;
        }

        _details = _details with { Versions = versions is { Count: > 0 } ? versions : null };
        Refresh();
    }

    /// <summary>清空选中（切实例/刷新时用，避免显示上一实例的旧数据）。</summary>
    public void Clear()
    {
        _identifier = null;
        _row = null;
        _installed = null;
        _details = null;
        _selectedStabilityToleranceOverride = null;
        _relationTree = null;
        SelectedTabIndex = 0;
        SetInstallStatus(string.Empty, isError: false);
        LastChangeSet = null;
        ContentNavigationStatusText = string.Empty;
        ResetSpacedockImageState();
        Refresh();
    }


    /// <summary>
    /// 跳转到全局 ZIP 内容预览页（页签四 / F-26、F-29）。
    /// 优先走 <see cref="INavigationService"/> 并带上当前模组标识作为参数；宿主未接入导航服务时回落到
    /// <see cref="ExternalNavigateAction"/>，两者都不可用才给出提示文案，保证 VM 不引用 Avalonia 与具体页面类型。
    /// </summary>
    [RelayCommand]
    private async Task NavigateToContentPreviewAsync()
    {
        var identifier = _identifier;
        if (identifier is null)
        {
            ContentNavigationStatusText = "未选中模组，无法打开内容预览。";
            return;
        }

        if (_navigation is not null)
        {
            try
            {
                if (await _navigation.NavigateAsync(ContentPreviewPageKey, identifier).ConfigureAwait(true))
                {
                    ContentNavigationStatusText = string.Empty;
                    return;
                }
            }
            catch (Exception ex)
            {
                ContentNavigationStatusText = "打开内容预览页失败：" + ex.Message;
                return;
            }
        }

        if (ExternalNavigateAction is not null)
        {
            ExternalNavigateAction(identifier);
            ContentNavigationStatusText = string.Empty;
            return;
        }

        ContentNavigationStatusText = "当前没有可用的导航服务，无法打开内容预览。";
    }

    /// <summary>
    /// 安装指定版本（F-30）：把目标版本号放进 <see cref="ChangeRequest.TargetVersion"/> 交给变更集服务算预览。
    /// 精确版本解析由 Core 侧 <c>CoreChangeSetPlanner</c> 负责（VM 不接触 Core 类型）；
    /// 生成的预览同时经 <see cref="IChangeRequestSink"/> 交给变更集页确认安装。
    /// </summary>
    /// <param name="version">用户在版本页点选的版本行。</param>
    [RelayCommand(CanExecute = nameof(CanInstallVersion))]
    private async Task InstallVersionAsync(ModVersionItemDto? version)
    {
        if (version is null)
        {
            return;
        }

        var identifier = _identifier;
        if (identifier is null)
        {
            SetInstallStatus("未选中模组，无法发起安装。", isError: true);
            return;
        }

        if (_changeSets is null)
        {
            SetInstallStatus("当前没有可用的变更集服务，无法发起安装。", isError: true);
            return;
        }

        var requests = new[] { new ChangeRequest(identifier, ChangeAction.Install, version.Version) };
        IsInstalling = true;
        try
        {
            var result = await _changeSets.BuildAsync(requests).ConfigureAwait(true);
            if (!result.IsSuccess)
            {
                LastChangeSet = null;
                _changeSink?.SetRequests(null);
                SetInstallStatus("生成变更集失败：" + result.Message, isError: true);
                return;
            }

            var preview = result.Value;
            LastChangeSet = preview;
            _changeSink?.SetRequests(requests);
            SetInstallStatus(
                preview is null || preview.IsEmpty
                    ? "目标版本 " + version.VersionText + " 已在实例中或不可用，没有需要安装的内容。"
                    : "已生成变更集：安装 " + version.VersionText + "，共 " + preview.Items.Count +
                      " 项变更，预计下载 " + ModDetailsFormatter.FormatSize(preview.EstimatedDownloadBytes) + "。",
                isError: false);
        }
        catch (Exception ex)
        {
            LastChangeSet = null;
            SetInstallStatus("生成变更集失败：" + ex.Message, isError: true);
        }
        finally
        {
            IsInstalling = false;
        }
    }

    private bool CanInstallVersion(ModVersionItemDto? version) => !_isInstalling;

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

        _isUpdatingTolerance = true;
        try
        {
            string? currentOverride = null;
            if (ToleranceOverrideProvider is not null)
            {
                currentOverride = ToleranceOverrideProvider(_identifier);
            }
            else if (!string.IsNullOrEmpty(GameDirectory))
            {
                currentOverride = StabilityToleranceFile.GetModToleranceSync(GameDirectory, _identifier);
            }

            _selectedStabilityToleranceOverride = StabilityToleranceOptions.FirstOrDefault(opt =>
                string.Equals(opt.Value, currentOverride, StringComparison.OrdinalIgnoreCase))
                ?? StabilityToleranceOptions[0];
            OnPropertyChanged(nameof(SelectedStabilityToleranceOverride));
        }
        finally
        {
            _isUpdatingTolerance = false;
        }

        if (_identifier is not null && HasRelations)
        {
            var edges = new List<RelationEdge>();
            foreach (var d in Dependencies)
            {
                edges.Add(new RelationEdge(RelationKind.Depends, d.Identifier));
            }
            foreach (var r in Recommendations)
            {
                edges.Add(new RelationEdge(RelationKind.Recommends, r.Identifier));
            }
            foreach (var c in Conflicts)
            {
                edges.Add(new RelationEdge(RelationKind.Conflicts, c.Identifier));
            }

            var graph = new Dictionary<string, IReadOnlyList<RelationEdge>>(StringComparer.OrdinalIgnoreCase)
            {
                [_identifier] = edges,
            };

            var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [_identifier] = Name,
            };
            foreach (var d in Dependencies) names[d.Identifier] = d.DisplayText ?? d.Identifier;
            foreach (var r in Recommendations) names[r.Identifier] = r.DisplayText ?? r.Identifier;
            foreach (var c in Conflicts) names[c.Identifier] = c.DisplayText ?? c.Identifier;

            _relationTree = new RelationTreeViewModel(
                _identifier,
                graph,
                names,
                knownIdentifiers: names.Keys,
                navigate: id => Select(id));
        }
        else
        {
            _relationTree = null;
        }

        Refresh();
    }

    private void Refresh() => OnPropertyChanged(string.Empty);

    /// <summary>设置安装状态反馈文案与其成败标记（两者必须同步，否则横幅颜色会和文案矛盾）。</summary>
    /// <param name="text">反馈文案，空串表示清空。</param>
    /// <param name="isError">是否为失败反馈。</param>
    private void SetInstallStatus(string text, bool isError)
    {
        InstallStatusIsError = isError && !string.IsNullOrWhiteSpace(text);
        InstallStatusText = text;
    }

    /// <summary>
    /// 换选中模组时重置一次性状态：回到第一个页签、清掉上一轮的安装/跳转反馈与变更集预览，
    /// 避免同名面板里残留上一个模组的结果。
    /// </summary>
    /// <param name="previousIdentifier">切换前的标识符。</param>
    private void ResetTransientState(string? previousIdentifier)
    {
        if (string.Equals(previousIdentifier, _identifier, StringComparison.Ordinal))
        {
            return;
        }

        SelectedTabIndex = 0;
        SetInstallStatus(string.Empty, isError: false);
        LastChangeSet = null;
        ContentNavigationStatusText = string.Empty;
        ResetSpacedockImageState();
    }

    /// <summary>
    /// 重置 SpaceDock 截图状态（清空下载字节、错误提示与加载中状态）。
    /// </summary>
    private void ResetSpacedockImageState()
    {
        SpacedockImageBytes = null;
        IsLoadingSpacedockImage = false;
        SpacedockImageError = string.Empty;
    }

    /// <summary>
    /// 按需加载当前选中模组的 SpaceDock 截图。
    /// 默认不加载，减轻网络压力；用户点击加载按钮后再触发。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanLoadSpacedockImage))]
    private async Task LoadSpacedockImageAsync()
    {
        if (string.IsNullOrWhiteSpace(SpacedockUrl) || _spacedockImageService is null)
        {
            SpacedockImageError = "当前环境没有可用的 SpaceDock 图片抓取服务。";
            return;
        }

        IsLoadingSpacedockImage = true;
        SpacedockImageError = string.Empty;

        try
        {
            var result = await _spacedockImageService.FetchScreenshotAsync(SpacedockUrl).ConfigureAwait(true);
            if (result.IsSuccess && result.Value is { Length: > 0 } bytes)
            {
                SpacedockImageBytes = bytes;
            }
            else
            {
                SpacedockImageError = string.IsNullOrWhiteSpace(result.Message)
                    ? "未能从 SpaceDock 获取到预览截图"
                    : result.Message;
            }
        }
        catch (Exception ex)
        {
            SpacedockImageError = "拉取截图失败: " + ex.Message;
        }
        finally
        {
            IsLoadingSpacedockImage = false;
        }
    }

    private bool CanLoadSpacedockImage() => HasSpacedockUrl && !IsLoadingSpacedockImage;

    /// <summary>
    /// 引导用户前往 SpaceDock 官方网页页面。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanOpenSpacedockUrl))]
    private async Task OpenSpacedockUrlAsync()
    {
        if (string.IsNullOrWhiteSpace(SpacedockUrl))
        {
            return;
        }

        if (ShellLauncher is not null)
        {
            await ShellLauncher.OpenBrowserAsync(SpacedockUrl).ConfigureAwait(true);
        }
        else
        {
            try
            {
                using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(SpacedockUrl)
                {
                    UseShellExecute = true
                });
            }
            catch
            {
                // 忽略浏览器拉起失败
            }
        }
    }

    private bool CanOpenSpacedockUrl() => HasSpacedockUrl;

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
