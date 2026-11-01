using System.Collections.ObjectModel;
using System.Globalization;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using CkanUI.Localization;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.ViewModels.Pages.Instances;

namespace CkanUI.ViewModels.Dialogs;

/// <summary>
/// 首次启动指引（02 篇 §1 Ⅱ-⑪）：找到游戏 → 配下载代理 → 对齐兼容版本 → 拉元数据 → 装推荐模组。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么不用页面而用对话框</b>：向导是「一次性的收尾动作」，不是常驻目的地。走完就该消失，
/// 不该在侧栏留一个「已办完」的入口；重复进入的路径放在「设置 → 首次启动指引」里。
/// </para>
/// <para>
/// <b>文案为什么走 <c>T(键, 中文兜底)</c> 而不是写死中文</b>：与 <c>PageViewModel.Text</c> 同一契约——
/// 宿主（App 层）把本地化器的 <c>Get</c> 挂到 <see cref="Translate"/> 上，译文与兜底必须逐字一致。
/// 译文必须在设置 <see cref="Translate"/> 之后、绑定之前取，所以宿主应当「先 new → 再挂 Translate → 最后设 DataContext」。
/// </para>
/// <para>
/// <b>每一步都能跳过</b>：只有「登记实例」是硬前置（后面三步都要实例名）。代理/版本/索引/模组任何一步失败
/// 都只写错误提示，不把用户扣在原地——首启最怕的就是「卡在某一步出不去」。
/// </para>
/// </remarks>
public sealed partial class FirstRunWizardViewModel : DialogViewModelBase
{
    private static readonly (FirstRunStepKind Kind, string Key, string Fallback)[] StepDefinitions =
    {
        (FirstRunStepKind.Welcome, LocalizationKeys.FirstRunStepWelcome, "欢迎"),
        (FirstRunStepKind.LocateGame, LocalizationKeys.FirstRunStepLocate, "找到游戏"),
        (FirstRunStepKind.Proxy, LocalizationKeys.FirstRunStepProxy, "下载代理"),
        (FirstRunStepKind.GameVersion, LocalizationKeys.FirstRunStepVersion, "兼容版本"),
        (FirstRunStepKind.Metadata, LocalizationKeys.FirstRunStepMetadata, "元数据缓存"),
        (FirstRunStepKind.RecommendedMods, LocalizationKeys.FirstRunStepMods, "装机必备"),
        (FirstRunStepKind.Done, LocalizationKeys.FirstRunStepDone, "完成"),
    };

    private readonly IInstanceService _instances;
    private readonly IKspInstallScanner _scanner;
    private readonly IProxyConfigurator _proxy;
    private readonly ICompatibleVersionsService _versions;
    private readonly IRepositoryService _repositories;
    private readonly IChangeSetService _changeSets;
    private readonly IInstallerService _installer;
    private readonly IUiSettingsStore _uiSettings;

    private Func<string, string>? _translate;
    private InstanceInfo? _instance;
    private bool _indexReady;
    private int _installedMods;

    /// <summary>构造向导。</summary>
    /// <param name="instances">实例服务。</param>
    /// <param name="scanner">安装目录扫描器。</param>
    /// <param name="proxy">下载代理装配器。</param>
    /// <param name="versions">兼容版本服务。</param>
    /// <param name="repositories">元数据索引服务。</param>
    /// <param name="changeSets">变更集构建服务。</param>
    /// <param name="installer">安装执行服务。</param>
    /// <param name="uiSettings">UI 设置存储（首次完成标志与代理配置都落在这里）。</param>
    public FirstRunWizardViewModel(
        IInstanceService instances,
        IKspInstallScanner scanner,
        IProxyConfigurator proxy,
        ICompatibleVersionsService versions,
        IRepositoryService repositories,
        IChangeSetService changeSets,
        IInstallerService installer,
        IUiSettingsStore uiSettings)
        : base("首次启动指引")
    {
        _instances = instances ?? throw new ArgumentNullException(nameof(instances));
        _scanner = scanner ?? throw new ArgumentNullException(nameof(scanner));
        _proxy = proxy ?? throw new ArgumentNullException(nameof(proxy));
        _versions = versions ?? throw new ArgumentNullException(nameof(versions));
        _repositories = repositories ?? throw new ArgumentNullException(nameof(repositories));
        _changeSets = changeSets ?? throw new ArgumentNullException(nameof(changeSets));
        _installer = installer ?? throw new ArgumentNullException(nameof(installer));
        _uiSettings = uiSettings ?? throw new ArgumentNullException(nameof(uiSettings));

        Steps = new ObservableCollection<FirstRunStepItemViewModel>(
            StepDefinitions.Select((definition, index) =>
                new FirstRunStepItemViewModel(definition.Kind, index, Text(definition.Key, definition.Fallback))));

        // 代理控件初始值取磁盘上的配置：用户上次填过就还是那个值，不用重抄。
        var settings = _uiSettings.Current;
        SelectedProxyMode = ParseProxyMode(settings.ProxyMode);
        ProxyServer = settings.ProxyServer;
        ProxyBypass = settings.ProxyBypass;

        RecommendedMods = EssentialModCatalog.CreateItems();
        UpdateStepState();
    }

    /// <summary>宿主注入的译文解析器；为空时全部走中文兜底。</summary>
    public Func<string, string>? Translate
    {
        get => _translate;
        set
        {
            _translate = value;
            RefreshStepTitles();
            OnPropertyChanged(nameof(WindowTitle));
            OnPropertyChanged(nameof(WizardSubtitle));
        }
    }

    /// <summary>宿主注入的目录选择器（与实例页同一个能力）。</summary>
    public IInstanceFolderPicker? FolderPicker { get; set; }

    /// <summary>步骤条。</summary>
    public ObservableCollection<FirstRunStepItemViewModel> Steps { get; }

    /// <summary>第 2 步的扫描结果。</summary>
    public ObservableCollection<KspCandidateItemViewModel> Candidates { get; } = new();

    /// <summary>第 4 步的兼容版本项。</summary>
    public ObservableCollection<FirstRunVersionItemViewModel> VersionItems { get; } = new();

    /// <summary>第 6 步的推荐模组。</summary>
    public ObservableCollection<RecommendedModItemViewModel> RecommendedMods { get; }

    /// <summary>窗口标题（跟随语言切换）。</summary>
    public string WindowTitle => Text(LocalizationKeys.FirstRunTitle, "首次启动指引");

    /// <summary>顶部辅标题。</summary>
    public string WizardSubtitle => Text(LocalizationKeys.FirstRunKicker, "五步把 CKAN 跑起来");

    /// <summary>当前步骤索引。</summary>
    [ObservableProperty]
    private int _currentIndex;

    /// <summary>是否有长操作在跑（按钮禁用依据）。</summary>
    [ObservableProperty]
    private bool _isBusy;

    /// <summary>长操作文案。</summary>
    [ObservableProperty]
    private string? _busyText;

    /// <summary>就地状态提示。</summary>
    [ObservableProperty]
    private string? _statusText;

    /// <summary>错误提示。</summary>
    [ObservableProperty]
    private string? _errorText;

    /// <summary>是否正在扫描安装目录。</summary>
    [ObservableProperty]
    private bool _isScanning;

    /// <summary>是否正在拉取索引。</summary>
    [ObservableProperty]
    private bool _isRefreshingIndex;

    /// <summary>是否正在测试代理。</summary>
    [ObservableProperty]
    private bool _isTestingProxy;

    /// <summary>代理模式。</summary>
    [ObservableProperty]
    private ProxyMode _selectedProxyMode = ProxyMode.System;

    /// <summary>代理地址。</summary>
    [ObservableProperty]
    private string? _proxyServer;

    /// <summary>不分流地址。</summary>
    [ObservableProperty]
    private string? _proxyBypass;

    /// <summary>代理测试结论。</summary>
    [ObservableProperty]
    private string? _proxyTestResult;

    /// <summary>索引刷新结论。</summary>
    [ObservableProperty]
    private string? _indexSummary;

    /// <summary>推荐模组安装结论。</summary>
    [ObservableProperty]
    private string? _modsInstallSummary;

    /// <summary>完成页：实例。</summary>
    [ObservableProperty]
    private string? _summaryInstance;

    /// <summary>完成页：代理。</summary>
    [ObservableProperty]
    private string? _summaryProxy;

    /// <summary>完成页：索引。</summary>
    [ObservableProperty]
    private string? _summaryIndex;

    /// <summary>完成页：模组。</summary>
    [ObservableProperty]
    private string? _summaryMods;

    /// <summary>步骤计数文案。</summary>
    public string StepCounter => string.Format(
        CultureInfo.CurrentCulture,
        Text(LocalizationKeys.FirstRunStepCounter, "第 {0} / {1} 步"),
        CurrentIndex + 1,
        Steps.Count);

    /// <summary>主按钮文案：随步骤变化。</summary>
    public string NextButtonText => Steps[Math.Clamp(CurrentIndex, 0, Steps.Count - 1)].Kind switch
    {
        FirstRunStepKind.Welcome => Text(LocalizationKeys.FirstRunNext, "下一步"),
        FirstRunStepKind.RecommendedMods => Text(LocalizationKeys.FirstRunModsNext, "安装并继续"),
        FirstRunStepKind.Done => Text(LocalizationKeys.FirstRunFinish, "完成并进入"),
        _ => Text(LocalizationKeys.FirstRunNext, "下一步"),
    };

    /// <summary>是否能回退。</summary>
    public bool CanGoBack => CurrentIndex > 0 && CurrentStep != FirstRunStepKind.Done;

    /// <summary>当前步骤种类。</summary>
    public FirstRunStepKind CurrentStep =>
        Steps[Math.Clamp(CurrentIndex, 0, Steps.Count - 1)].Kind;

    /// <summary>是否在欢迎页。</summary>
    public bool IsWelcomeStep => CurrentStep == FirstRunStepKind.Welcome;

    /// <summary>是否在「找到游戏」页。</summary>
    public bool IsLocateStep => CurrentStep == FirstRunStepKind.LocateGame;

    /// <summary>是否在「下载代理」页。</summary>
    public bool IsProxyStep => CurrentStep == FirstRunStepKind.Proxy;

    /// <summary>是否在「兼容版本」页。</summary>
    public bool IsVersionStep => CurrentStep == FirstRunStepKind.GameVersion;

    /// <summary>是否在「元数据缓存」页。</summary>
    public bool IsMetadataStep => CurrentStep == FirstRunStepKind.Metadata;

    /// <summary>是否在「装机必备」页。</summary>
    public bool IsModsStep => CurrentStep == FirstRunStepKind.RecommendedMods;

    /// <summary>是否在「完成」页。</summary>
    public bool IsDoneStep => CurrentStep == FirstRunStepKind.Done;

    /// <summary>是否有错误提示。</summary>
    public bool HasError => !string.IsNullOrEmpty(ErrorText);

    /// <summary>是否有状态提示。</summary>
    public bool HasStatus => !string.IsNullOrEmpty(StatusText);

    /// <summary>是否没有扫描到任何候选（显示空状态）。</summary>
    public bool HasNoCandidates => Candidates.Count == 0 && !IsScanning;

    /// <summary>是否选中了某个候选。</summary>
    public bool HasSelection => Candidates.Any(candidate => candidate.IsSelected);

    /// <summary>已勾选的推荐模组数。</summary>
    public int CheckedModCount => RecommendedMods.Count(mod => mod.IsChecked);

    /// <summary>手动代理时才显示地址输入框。</summary>
    public bool IsManualProxy => SelectedProxyMode == ProxyMode.Manual;

    /// <summary>「跟随系统」单选。</summary>
    public bool IsProxySystem
    {
        get => SelectedProxyMode == ProxyMode.System;
        set
        {
            if (value)
            {
                SelectedProxyMode = ProxyMode.System;
            }
        }
    }

    /// <summary>「直连」单选。</summary>
    public bool IsProxyDirect
    {
        get => SelectedProxyMode == ProxyMode.Direct;
        set
        {
            if (value)
            {
                SelectedProxyMode = ProxyMode.Direct;
            }
        }
    }

    /// <summary>「手动代理」单选。</summary>
    public bool IsProxyManual
    {
        get => SelectedProxyMode == ProxyMode.Manual;
        set
        {
            if (value)
            {
                SelectedProxyMode = ProxyMode.Manual;
            }
        }
    }

    // ---- 文案（每次求值都走 T，保证挂上 Translate 之后立刻生效）----------------

    /// <summary>欢迎页标题。</summary>
    public string WelcomeTitle => Text(LocalizationKeys.FirstRunWelcomeTitle, "欢迎使用 CKAN UI");

    /// <summary>欢迎页正文。</summary>
    public string WelcomeBody => Text(
        LocalizationKeys.FirstRunWelcomeBody,
        "这是一个《坎巴拉太空计划》的模组管理器。接下来几步只做四件事：找到游戏、配好下载网络、对齐游戏版本、拉一份模组索引。每一步都能跳过，之后也能从「设置 → 首次启动指引」重新开始。");

    /// <summary>欢迎页免责说明。</summary>
    public string WelcomeNote => Text(
        LocalizationKeys.FirstRunWelcomeNote,
        "向导不会自动安装模组，也不会收集任何数据。");

    /// <summary>「找到游戏」页正文。</summary>
    public string LocateBody => Text(
        LocalizationKeys.FirstRunLocateBody,
        "已扫描 Steam 库、注册表与常见安装位置。选中正确的目录，或手动指定。");

    /// <summary>扫描中提示。</summary>
    public string LocateScanning => Text(LocalizationKeys.FirstRunLocateScanning, "正在扫描…");

    /// <summary>空状态提示。</summary>
    public string LocateEmpty => Text(
        LocalizationKeys.FirstRunLocateEmpty,
        "没找到 KSP。请手动选择游戏根目录（需包含 GameData 与 KSP.exe）。");

    /// <summary>版本未知占位。</summary>
    public string LocateVersionUnknown => Text(LocalizationKeys.FirstRunLocateVersionUnknown, "版本未知");

    /// <summary>「下载代理」页正文。</summary>
    /// <summary>「跟随系统」单选文案。</summary>
    public string ProxyModeSystemText => Text(LocalizationKeys.FirstRunProxyModeSystem, "跟随系统");

    /// <summary>「直连」单选文案。</summary>
    public string ProxyModeDirectText => Text(LocalizationKeys.FirstRunProxyModeDirect, "直连");

    /// <summary>「手动代理」单选文案。</summary>
    public string ProxyModeManualText => Text(LocalizationKeys.FirstRunProxyModeManual, "手动代理");

    public string ProxyBody => Text(
        LocalizationKeys.FirstRunProxyBody,
        "CKAN 的索引与模组都从 GitHub 拉取。直连不通时填一个代理，之后所有下载都会走它。");

    /// <summary>「兼容版本」页正文。</summary>
    public string VersionBody => Text(
        LocalizationKeys.FirstRunVersionBody,
        "CKAN 按这里的版本挑选模组。默认只认游戏的真实版本；勾上旧版本，才能装到「尚未适配新版」的模组。");

    /// <summary>「元数据缓存」页正文。</summary>
    public string MetadataBody => Text(
        LocalizationKeys.FirstRunMetadataBody,
        "把官方索引（两千多个模组的元数据）拉到本地。这是「能搜索、能装、能算依赖」的前提。");

    /// <summary>拉取中提示。</summary>
    public string MetadataRunning => Text(LocalizationKeys.FirstRunMetadataRunning, "正在拉取索引…");

    /// <summary>离线 skip 提示。</summary>
    public string MetadataSkipHint => Text(
        LocalizationKeys.FirstRunMetadataSkipHint,
        "拉不到也能先跳过，联网后在「仓库」页刷新即可。");

    /// <summary>「装机必备」页正文。</summary>
    public string ModsBody => Text(
        LocalizationKeys.FirstRunModsBody,
        "社区里几乎人人装的基础模组。默认一个都不勾——想装哪个自己点，装完也能随时卸。");

    /// <summary>一个都没勾时的提示。</summary>
    public string ModsNone => Text(LocalizationKeys.FirstRunModsNone, "一个都没勾，直接下一步即可。");

    /// <summary>完成页标题。</summary>
    public string DoneTitle => Text(LocalizationKeys.FirstRunDoneTitle, "好了，可以出发了");

    /// <summary>完成页正文。</summary>
    public string DoneBody => Text(LocalizationKeys.FirstRunDoneBody, "这些已经就位：");

    /// <summary>完成页补充.</summary>
    public string DoneTip => Text(LocalizationKeys.FirstRunDoneTip, "想再走一遍？设置 → 首次启动指引。");

    /// <summary>退出按钮文案。</summary>
    public string ExitText => Text(LocalizationKeys.FirstRunExit, "退出向导");

    /// <summary>回退按钮文案。</summary>
    public string BackText => Text(LocalizationKeys.FirstRunBack, "上一步");

    /// <summary>重新扫描按钮文案。</summary>
    public string RescanText => Text(LocalizationKeys.FirstRunRescan, "重新扫描");

    /// <summary>手动选择目录按钮文案。</summary>
    public string BrowseText => Text(LocalizationKeys.FirstRunBrowse, "手动选择目录");

    /// <summary>代理地址标签。</summary>
    public string ProxyServerLabel => Text(LocalizationKeys.FirstRunProxyServerLabel, "代理地址");

    /// <summary>代理地址占位符。</summary>
    public string ProxyServerPlaceholder => Text(LocalizationKeys.FirstRunProxyServerPlaceholder, "http://127.0.0.1:7890");

    /// <summary>不分流地址标签。</summary>
    public string ProxyBypassLabel => Text(LocalizationKeys.FirstRunProxyBypassLabel, "不分流地址（可选）");

    /// <summary>不分流地址占位符。</summary>
    public string ProxyBypassPlaceholder => Text(LocalizationKeys.FirstRunProxyBypassPlaceholder, "localhost, 127.0.0.1");

    /// <summary>测试连接按钮文案。</summary>
    public string ProxyTestText => IsTestingProxy
        ? Text(LocalizationKeys.FirstRunProxyTesting, "正在测试连接…")
        : Text(LocalizationKeys.FirstRunProxyTest, "测试连接");

    /// <summary>全选推荐按钮文案。</summary>
    public string SelectAllText => Text(LocalizationKeys.FirstRunModsSelectAll, "全选推荐");

    /// <summary>全部取消按钮文案。</summary>
    public string ClearText => Text(LocalizationKeys.FirstRunModsClear, "全部取消");

    /// <summary>真实版本标签。</summary>
    public string VersionRealLabel => Text(LocalizationKeys.FirstRunVersionReal, "真实版本");

    /// <summary>重新拉取按钮文案。</summary>
    public string RetryText => Text(LocalizationKeys.FirstRunRetry, "重新拉取");

    /// <summary>完成页「游戏实例」标签。</summary>
    public string DoneInstanceLabel => Text(LocalizationKeys.FirstRunDoneInstance, "游戏实例");

    /// <summary>完成页「下载代理」标签。</summary>
    public string DoneProxyLabel => Text(LocalizationKeys.FirstRunDoneProxy, "下载代理");

    /// <summary>完成页「模组索引」标签。</summary>
    public string DoneIndexLabel => Text(LocalizationKeys.FirstRunDoneIndex, "模组索引");

    /// <summary>完成页「已安装模组」标签。</summary>
    public string DoneModsLabel => Text(LocalizationKeys.FirstRunDoneMods, "已安装模组");

    // ---- 命令 ----------------------------------------------------------------

    /// <summary>下一步：按当前步骤提交，再前进。</summary>
    [RelayCommand]
    private async Task NextAsync()
    {
        if (IsBusy)
        {
            return;
        }

        ClearError();
        var committed = CurrentStep switch
        {
            FirstRunStepKind.LocateGame => await CommitInstanceAsync().ConfigureAwait(true),
            FirstRunStepKind.Proxy => CommitProxy(),
            FirstRunStepKind.GameVersion => await CommitVersionsAsync().ConfigureAwait(true),
            FirstRunStepKind.RecommendedMods => await CommitModsAsync().ConfigureAwait(true),
            FirstRunStepKind.Done => Complete(),
            _ => true,
        };

        if (!committed)
        {
            return;
        }

        Steps[CurrentIndex].IsCompleted = true;
        if (CurrentStep == FirstRunStepKind.Done)
        {
            return;
        }

        CurrentIndex = Math.Min(CurrentIndex + 1, Steps.Count - 1);
    }

    /// <summary>上一步。</summary>
    [RelayCommand]
    private void Back()
    {
        if (!CanGoBack || IsBusy)
        {
            return;
        }

        ClearError();
        CurrentIndex--;
    }

    /// <summary>退出向导：不写完成标志（下次启动在「还没有实例」时还会出现）。</summary>
    [RelayCommand]
    private void CancelWizard()
    {
        if (IsBusy)
        {
            return;
        }

        Close();
    }

    /// <summary>重新扫描安装目录。</summary>
    [RelayCommand]
    private async Task ScanAsync()
    {
        if (IsScanning || IsBusy)
        {
            return;
        }

        ClearError();
        IsScanning = true;
        StatusText = LocateScanning;
        try
        {
            var result = await _scanner.ScanAsync().ConfigureAwait(true);
            Candidates.Clear();
            if (!result.IsSuccess)
            {
                SetError(result.Message);
                return;
            }

            foreach (var candidate in result.Value ?? Array.Empty<KspCandidate>())
            {
                Candidates.Add(new KspCandidateItemViewModel(candidate, SourceLabel(candidate.Source), LocateVersionUnknown)
                {
                    Selected = EnsureSingleSelection,
                });
            }

            // 只有一个候选就替用户选上：首启最省事的是「按下一步就行」。
            if (Candidates.Count == 1)
            {
                Candidates[0].IsSelected = true;
            }

            StatusText = Candidates.Count == 0 ? LocateEmpty : null;
        }
        catch (Exception ex)
        {
            SetError($"扫描失败：{ex.Message}");
        }
        finally
        {
            IsScanning = false;
            RaiseCandidateState();
        }
    }

    /// <summary>手动选择游戏目录。</summary>
    [RelayCommand]
    private async Task BrowseAsync()
    {
        if (IsBusy)
        {
            return;
        }

        ClearError();
        if (FolderPicker is not { } picker)
        {
            SetError(Text(LocalizationKeys.FirstRunLocateNoPicker, "当前环境没有可用的目录选择器。"));
            return;
        }

        try
        {
            var picked = await picker.PickFolderAsync(Text(LocalizationKeys.FirstRunBrowse, "手动选择目录")).ConfigureAwait(true);
            if (string.IsNullOrWhiteSpace(picked))
            {
                return;
            }

            if (!_scanner.IsKspDirectory(picked))
            {
                SetError(Text(
                    LocalizationKeys.FirstRunLocateNotKsp,
                    "这个目录不像 KSP 根目录：需要同时存在 GameData 与 KSP.exe。"));
                return;
            }

            var version = _scanner.ReadGameVersion(picked);
            Candidates.Insert(0, new KspCandidateItemViewModel(
                new KspCandidate(picked, version, KspCandidateSource.Manual),
                SourceLabel(KspCandidateSource.Manual),
                LocateVersionUnknown)
            {
                Selected = EnsureSingleSelection,
            });
            foreach (var other in Candidates.Skip(1))
            {
                other.IsSelected = false;
            }

            Candidates[0].IsSelected = true;
            StatusText = null;
            RaiseCandidateState();
        }
        catch (Exception ex)
        {
            SetError($"选择目录失败：{ex.Message}");
        }
    }

    /// <summary>测试代理连通性。</summary>
    [RelayCommand]
    private async Task TestProxyAsync()
    {
        if (IsTestingProxy)
        {
            return;
        }

        ClearError();
        ProxyTestResult = null;
        var settings = new ProxySettings(SelectedProxyMode, ProxyServer?.Trim(), ProxyBypass?.Trim());
        if (!settings.IsValid)
        {
            SetError(Text(LocalizationKeys.FirstRunProxyInvalid, "代理地址格式应为 http://主机:端口。"));
            return;
        }

        IsTestingProxy = true;
        try
        {
            var result = await _proxy.TestAsync(settings).ConfigureAwait(true);
            ProxyTestResult = result.IsSuccess ? result.Value : result.Message;
        }
        catch (Exception ex)
        {
            ProxyTestResult = $"测试失败：{ex.Message}";
        }
        finally
        {
            IsTestingProxy = false;
        }
    }

    /// <summary>重新拉取索引。</summary>
    [RelayCommand]
    private Task RefreshIndexAsync() => RunIndexRefreshAsync();

    /// <summary>全选推荐（用户主动点的，不默认勾）。</summary>
    [RelayCommand]
    private void SelectAllMods()
    {
        foreach (var mod in RecommendedMods)
        {
            mod.IsChecked = true;
        }

        RaiseModState();
    }

    /// <summary>全部取消。</summary>
    [RelayCommand]
    private void ClearMods()
    {
        foreach (var mod in RecommendedMods)
        {
            mod.IsChecked = false;
        }

        RaiseModState();
    }

    // ---- 步骤提交 ------------------------------------------------------------

    /// <summary>登记实例：已有同路径实例就切换，不重复创建。</summary>
    private async Task<bool> CommitInstanceAsync()
    {
        var selected = Candidates.FirstOrDefault(candidate => candidate.IsSelected);
        if (selected is null)
        {
            SetError(Text(LocalizationKeys.FirstRunLocateNoSelection, "请先选一个 KSP 目录，或手动指定。"));
            return false;
        }

        IsBusy = true;
        BusyText = Text(LocalizationKeys.FirstRunLocateRegistering, "正在登记游戏实例…");
        try
        {
            var existing = _instances.Instances.FirstOrDefault(
                instance => string.Equals(instance.GameDir, selected.GameDir, StringComparison.OrdinalIgnoreCase));

            if (existing is not null)
            {
                var switched = await _instances.SwitchAsync(existing.Name).ConfigureAwait(true);
                if (!switched.IsSuccess)
                {
                    SetError(switched.Message);
                    return false;
                }

                _instance = switched.Value ?? existing;
                return true;
            }

            var name = Path.GetFileName(selected.GameDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrWhiteSpace(name))
            {
                name = "KSP";
            }

            var added = await _instances.AddAsync(selected.GameDir, name).ConfigureAwait(true);
            if (!added.IsSuccess || added.Value is null)
            {
                SetError(added.Message);
                return false;
            }

            _instance = added.Value;
            return true;
        }
        catch (Exception ex)
        {
            SetError($"登记游戏实例失败：{ex.Message}");
            return false;
        }
        finally
        {
            IsBusy = false;
            BusyText = null;
        }
    }

    /// <summary>保存并应用代理配置。</summary>
    private bool CommitProxy()
    {
        var settings = new ProxySettings(SelectedProxyMode, ProxyServer?.Trim(), ProxyBypass?.Trim());
        if (!settings.IsValid)
        {
            SetError(Text(LocalizationKeys.FirstRunProxyInvalid, "代理地址格式应为 http://主机:端口。"));
            return false;
        }

        try
        {
            _proxy.Apply(settings);
            var saved = _uiSettings.Save(_uiSettings.Current with
            {
                ProxyMode = settings.Mode switch
                {
                    ProxyMode.Direct => "direct",
                    ProxyMode.Manual => "manual",
                    _ => "system",
                },
                ProxyServer = settings.Server,
                ProxyBypass = settings.BypassList,
            });

            if (!saved.IsSuccess)
            {
                // 写盘失败不影响本次会话：代理已经进程级生效，只是下次启动要重填。
                SetError(saved.Message);
            }

            return true;
        }
        catch (Exception ex)
        {
            SetError($"保存代理配置失败：{ex.Message}");
            return false;
        }
    }

    /// <summary>保存兼容版本选择。</summary>
    private async Task<bool> CommitVersionsAsync()
    {
        if (_instance is null || VersionItems.Count == 0)
        {
            return true;
        }

        IsBusy = true;
        BusyText = Text(LocalizationKeys.FirstRunSaving, "正在保存…");
        try
        {
            var selected = VersionItems.Where(item => item.IsSelected)
                                       .Select(item => item.VersionString)
                                       .ToArray();
            await _versions.SaveAsync(selected, _instance.Name, triggerRefresh: false).ConfigureAwait(true);
            return true;
        }
        catch (Exception ex)
        {
            // 版本保存失败不该把人扣在这里：后面还能在实例页改。
            SetError($"保存兼容版本失败：{ex.Message}");
            return true;
        }
        finally
        {
            IsBusy = false;
            BusyText = null;
        }
    }

    /// <summary>安装勾选的推荐模组。</summary>
    private async Task<bool> CommitModsAsync()
    {
        var picked = RecommendedMods.Where(mod => mod.IsChecked)
                                    .Select(mod => mod.Identifier)
                                    .ToArray();
        if (picked.Length == 0)
        {
            return true;
        }

        IsBusy = true;
        BusyText = Text(LocalizationKeys.FirstRunModsInstalling, "正在安装推荐模组…");
        try
        {
            var requests = picked.Select(identifier => new ChangeRequest(identifier, ChangeAction.Install)).ToArray();
            var preview = await _changeSets.BuildAsync(requests).ConfigureAwait(true);
            if (!preview.IsSuccess || preview.Value is null)
            {
                ModsInstallSummary = preview.Message;
                return true;
            }

            var report = await _installer.ApplyAsync(preview.Value).ConfigureAwait(true);
            if (!report.IsSuccess || report.Value is null)
            {
                ModsInstallSummary = report.Message;
                return true;
            }

            var summary = report.Value;
            _installedMods += summary.Installed.Count;
            ModsInstallSummary = string.Format(
                CultureInfo.CurrentCulture,
                Text(LocalizationKeys.FirstRunModsInstallSummary, "已安装 {0} 个，失败 {1} 个。"),
                summary.Installed.Count,
                summary.Failed.Count);

            if (summary.Failed.Count > 0)
            {
                SetError(string.Join("；", summary.Failed));
            }

            return true;
        }
        catch (Exception ex)
        {
            ModsInstallSummary = $"安装失败：{ex.Message}";
            return true;
        }
        finally
        {
            IsBusy = false;
            BusyText = null;
        }
    }

    /// <summary>收尾：写完成标志并关窗。</summary>
    private bool Complete()
    {
        try
        {
            _uiSettings.Save(_uiSettings.Current with { FirstRunCompleted = true });
        }
        catch (Exception ex)
        {
            // 标志写不进去只是「下次还会弹」，不该挡住用户进入主界面。
            StatusText = $"保存首次启动状态失败：{ex.Message}";
        }

        IsConfirmed = true;
        Close();
        return false;
    }

    // ---- 步骤进入 ------------------------------------------------------------

    partial void OnCurrentIndexChanged(int value)
    {
        UpdateStepState();
        ClearError();
        StatusText = null;
        RaiseStepFlags();
        OnStepEntered();
    }

    partial void OnErrorTextChanged(string? value) => OnPropertyChanged(nameof(HasError));

    partial void OnStatusTextChanged(string? value) => OnPropertyChanged(nameof(HasStatus));

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(NextButtonText));
        OnPropertyChanged(nameof(CanGoBack));
    }

    partial void OnSelectedProxyModeChanged(ProxyMode value)
    {
        OnPropertyChanged(nameof(IsProxySystem));
        OnPropertyChanged(nameof(IsProxyDirect));
        OnPropertyChanged(nameof(IsProxyManual));
        OnPropertyChanged(nameof(IsManualProxy));
        ProxyTestResult = null;
    }

    private void OnStepEntered()
    {
        switch (CurrentStep)
        {
            case FirstRunStepKind.LocateGame when Candidates.Count == 0 && !IsScanning:
                _ = ScanAsync();
                break;

            case FirstRunStepKind.GameVersion when VersionItems.Count == 0:
                _ = LoadVersionsAsync();
                break;

            case FirstRunStepKind.Metadata when !_indexReady && !IsRefreshingIndex:
                _ = RunIndexRefreshAsync();
                break;

            case FirstRunStepKind.Done:
                BuildSummary();
                break;
        }
    }

    private async Task LoadVersionsAsync()
    {
        if (_instance is null)
        {
            return;
        }

        try
        {
            var snapshot = await _versions.LoadAsync(_instance.Name).ConfigureAwait(true);
            VersionItems.Clear();
            foreach (var item in snapshot.Items)
            {
                VersionItems.Add(new FirstRunVersionItemViewModel(
                    item.VersionString, item.IsSelected, item.IsRealVersion, item.IsWildcard, VersionRealLabel));
            }

            if (VersionItems.Count == 0)
            {
                StatusText = Text(LocalizationKeys.FirstRunVersionLoadFailed, "读不到兼容版本列表，这一步可以跳过。");
            }
        }
        catch (Exception ex)
        {
            StatusText = $"读取兼容版本失败：{ex.Message}";
        }
    }

    private async Task RunIndexRefreshAsync()
    {
        if (IsRefreshingIndex)
        {
            return;
        }

        IsRefreshingIndex = true;
        IndexSummary = MetadataRunning;
        try
        {
            var result = await _repositories.RefreshAsync().ConfigureAwait(true);
            if (!result.IsSuccess)
            {
                IndexSummary = string.Format(
                    CultureInfo.CurrentCulture,
                    Text(LocalizationKeys.FirstRunMetadataFailed, "拉取失败：{0}"),
                    result.Message);
                return;
            }

            var snapshot = result.Value ?? _repositories.Current;
            _indexReady = snapshot.Available.Count > 0;
            IndexSummary = string.Format(
                CultureInfo.CurrentCulture,
                Text(LocalizationKeys.FirstRunMetadataDone, "索引就绪：{0} 个仓库，{1} 个模组。"),
                snapshot.RepositoryIds.Count,
                snapshot.Available.Count);
        }
        catch (Exception ex)
        {
            IndexSummary = string.Format(
                CultureInfo.CurrentCulture,
                Text(LocalizationKeys.FirstRunMetadataFailed, "拉取失败：{0}"),
                ex.Message);
        }
        finally
        {
            IsRefreshingIndex = false;
        }
    }

    private void BuildSummary()
    {
        SummaryInstance = _instance is null
            ? Text(LocalizationKeys.FirstRunNotSet, "未设置")
            : $"{_instance.Name} · {_instance.GameDir}";

        SummaryProxy = SelectedProxyMode switch
        {
            ProxyMode.Direct => Text(LocalizationKeys.FirstRunProxyModeDirect, "直连"),
            ProxyMode.Manual => $"{Text(LocalizationKeys.FirstRunProxyModeManual, "手动代理")} · {ProxyServer}",
            _ => Text(LocalizationKeys.FirstRunProxyModeSystem, "跟随系统"),
        };

        var snapshot = _repositories.Current;
        SummaryIndex = snapshot.Available.Count == 0
            ? Text(LocalizationKeys.FirstRunMetadataMissing, "未拉取（可在「仓库」页刷新）")
            : $"{snapshot.RepositoryIds.Count} / {snapshot.Available.Count}";

        SummaryMods = _installedMods == 0
            ? Text(LocalizationKeys.FirstRunModsZero, "0 个（随时可在「模组」页添加）")
            : _installedMods.ToString(CultureInfo.CurrentCulture);
    }

    // ---- 内部小工具 ----------------------------------------------------------

    private string Text(string key, string fallback)
    {
        var translated = _translate?.Invoke(key);
        return string.IsNullOrEmpty(translated) || string.Equals(translated, key, StringComparison.Ordinal)
            ? fallback
            : translated;
    }

    private void RefreshStepTitles()
    {
        for (var index = 0; index < Steps.Count; index++)
        {
            var (_, key, fallback) = StepDefinitions[index];
            Steps[index].Title = Text(key, fallback);
        }
    }

    private void UpdateStepState()
    {
        for (var index = 0; index < Steps.Count; index++)
        {
            Steps[index].IsCurrent = index == CurrentIndex;
        }
    }

    private void RaiseStepFlags()
    {
        OnPropertyChanged(nameof(CurrentStep));
        OnPropertyChanged(nameof(StepCounter));
        OnPropertyChanged(nameof(NextButtonText));
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(IsWelcomeStep));
        OnPropertyChanged(nameof(IsLocateStep));
        OnPropertyChanged(nameof(IsProxyStep));
        OnPropertyChanged(nameof(IsVersionStep));
        OnPropertyChanged(nameof(IsMetadataStep));
        OnPropertyChanged(nameof(IsModsStep));
        OnPropertyChanged(nameof(IsDoneStep));
    }

    /// <summary>
    /// 候选是单选语义：某个候选被选中时取消其余候选。
    /// 放在 VM 而不是 XAML 的 GroupName 上 —— ItemsControl 会为每个条目建独立容器，
    /// RadioButton 的隐式分组到不了兄弟条目，点了第二个就会出现「两个都亮」。
    /// </summary>
    private void EnsureSingleSelection(KspCandidateItemViewModel selected)
    {
        foreach (var candidate in Candidates)
        {
            if (!ReferenceEquals(candidate, selected))
            {
                candidate.IsSelected = false;
            }
        }
    }

    private void RaiseCandidateState()
    {
        OnPropertyChanged(nameof(HasNoCandidates));
        OnPropertyChanged(nameof(HasSelection));
    }

    private void RaiseModState()
    {
        OnPropertyChanged(nameof(CheckedModCount));
    }

    private void ClearError() => ErrorText = null;

    private void SetError(string? message) => ErrorText = message;

    private string SourceLabel(KspCandidateSource source) => source switch
    {
        KspCandidateSource.Steam => Text(LocalizationKeys.FirstRunLocateSourceSteam, "Steam 库"),
        KspCandidateSource.Registry => Text(LocalizationKeys.FirstRunLocateSourceRegistry, "注册表"),
        KspCandidateSource.Manual => Text(LocalizationKeys.FirstRunLocateSourceManual, "手动选择"),
        _ => Text(LocalizationKeys.FirstRunLocateSourceCommon, "常见路径"),
    };

    private static ProxyMode ParseProxyMode(string? raw) => raw switch
    {
        "direct" => ProxyMode.Direct,
        "manual" => ProxyMode.Manual,
        _ => ProxyMode.System,
    };
}
