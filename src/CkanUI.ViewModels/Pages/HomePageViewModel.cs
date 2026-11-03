using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CkanUI.Localization;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.ViewModels.Navigation;
using CkanUI.ViewModels.Pages.Home;
using CkanUI.ViewModels.Pages.Instances;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CkanUI.ViewModels.Pages;

/// <summary>
/// 主菜单（应用启动后的落地页）：当前实例概览 + 常用功能入口 + 当前实例的游戏截图。
/// </summary>
/// <remarks>
/// <para>单页纵向布局：实例概览回答「我现在在哪个实例上」；快捷操作按主次分三层——两张大卡放核心工作流、
/// 三张普通卡放常用页面、两个胶囊放工具页，避免七张同规格卡平铺成一片；最下面是自动滚动的截图胶片带。</para>
/// <para>截图只做「枚举 + 排序 + 排版文案」：解码（缩略图）在 App 层，VM 只暴露路径，
/// 于是这一页不需要任何 Avalonia 依赖。</para>
/// <para>宿主能力（<see cref="ShellLauncher"/>、<see cref="CommandStore"/>）走可注入属性，
/// 由视图的 <c>??=</c> 注入——容器里没有「起进程」这类平台实现。</para>
/// </remarks>
public sealed partial class HomePageViewModel : PageViewModel
{
    /// <summary>截图页签一次取多少张：首屏一屏能看完，又不至于把大目录整个读进来。</summary>
    private const int ScreenshotLimit = 12;

    private readonly IInstanceService? _instances;
    private readonly IGameScreenshotService? _screenshots;
    private readonly INavigationService? _navigation;

    private bool _subscribed;

    /// <summary>设计时（XAML 预览）用的空实现。</summary>
    public HomePageViewModel()
        : this(null, null, null)
    {
    }

    /// <summary>运行时构造：实例清单 + 截图枚举 + 页面导航。</summary>
    /// <param name="instances">实例清单（提供当前实例与切换通知）。</param>
    /// <param name="screenshots">截图枚举服务。</param>
    /// <param name="navigation">页面导航（快捷操作卡片与「切换实例」用）。</param>
    public HomePageViewModel(
        IInstanceService? instances,
        IGameScreenshotService? screenshots,
        INavigationService? navigation = null)
    {
        _instances = instances;
        _screenshots = screenshots;
        _navigation = navigation;
        Instance = instances?.Current;
        BuildQuickActions();
    }

    /// <inheritdoc />
    public override string Title => Text("主页", LocalizationKeys.NavRailHome);

    /// <summary>外壳动作：打开目录 / 启动游戏。由视图注入。</summary>
    public IInstanceShellLauncher? ShellLauncher { get; set; }

    /// <summary>实例启动命令的持久化（<c>GUIConfig.json</c>）。由视图注入。</summary>
    public IInstanceCommandStore? CommandStore { get; set; }

    /// <summary>核心工作流：模组库、变更集（页面上排成两张大卡）。</summary>
    public ObservableCollection<HomeQuickActionViewModel> PrimaryActions { get; } = new();

    /// <summary>常用页面：实例管理、存档、模组仓库（三张普通卡）。</summary>
    public ObservableCollection<HomeQuickActionViewModel> PageActions { get; } = new();

    /// <summary>工具页：日志、设置（两个紧凑胶囊，不与上面的卡抢视觉重量）。</summary>
    public ObservableCollection<HomeQuickActionViewModel> ToolActions { get; } = new();

    /// <summary>截图列表（按拍摄时间倒序，最多 <see cref="ScreenshotLimit"/> 张）。</summary>
    public ObservableCollection<HomeScreenshotRowViewModel> Screenshots { get; } = new();

    /// <summary>当前实例；没有实例时为 null。</summary>
    [ObservableProperty]
    private InstanceInfo? _instance;

    /// <summary>错误条文案（空串表示不显示）。</summary>
    [ObservableProperty]
    private string _errorText = string.Empty;

    /// <summary>状态条文案（启动成功等一次性反馈）。</summary>
    [ObservableProperty]
    private string _statusText = string.Empty;

    /// <summary>是否正在枚举截图。</summary>
    [ObservableProperty]
    private bool _isBusy;

    /// <summary>是否有可用实例。</summary>
    public bool HasInstance => Instance is not null;

    /// <summary>没有实例时显示空态引导。</summary>
    public bool ShowNoInstance => Instance is null;

    /// <summary>是否显示截图胶片带：有实例且确实取到了图才占位，空的时候不白留一条横带。</summary>
    public bool ShowScreenshotRail => HasInstance && Screenshots.Count > 0;

    /// <summary>是否显示状态条。</summary>
    public bool HasStatus => !string.IsNullOrWhiteSpace(StatusText);

    /// <summary>是否显示错误条。</summary>
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorText);

    /// <summary>实例被占用（游戏可能正在运行）时不允许再启动一次。</summary>
    public bool CanLaunch => Instance is { IsLocked: false };

    /// <summary>显示「实例被占用」的说明。</summary>
    public bool ShowLockedHint => Instance is { IsLocked: true };

    /// <summary>是否给实例名旁边挂「默认实例」徽标。</summary>
    public bool ShowDefaultBadge => Instance is { IsDefault: true };

    /// <summary>实例显示名（没有实例时给空态标题）。</summary>
    public string InstanceName => Instance?.DisplayName ?? Text("还没有可用实例", LocalizationKeys.HomeNoInstanceTitle);

    /// <summary>游戏版本（缺失时给「版本未知」）。</summary>
    public string InstanceVersionText => string.IsNullOrWhiteSpace(Instance?.GameVersion)
        ? Text("版本未知", LocalizationKeys.HomeVersionUnknown)
        : Instance!.GameVersion!;

    /// <summary>模组数文案。</summary>
    public string ModuleCountText => Instance?.ModuleCount is int count
        ? Text("{0} 个模组", LocalizationKeys.HomeModuleCount, count)
        : Text("模组数未知", LocalizationKeys.HomeModuleCountUnknown);

    /// <summary>游戏目录（没有实例时为空串）。</summary>
    public string GameDirText => Instance?.GameDir ?? string.Empty;

    /// <summary>截图目录（没有实例或没有截图服务时为空串）。</summary>
    public string ScreenshotFolderText => Instance?.GameDir is { Length: > 0 } gameDir && _screenshots is not null
        ? _screenshots.GetScreenshotsDirectory(gameDir)
        : string.Empty;

    /// <summary>截图张数文案。</summary>
    public string ScreenshotCountText => Text("{0} 张截图", LocalizationKeys.HomeScreenshotCount, Screenshots.Count);

    /// <summary>没有截图（且不在读取中）时显示空态。</summary>
    public bool ShowEmptyScreenshots => Screenshots.Count == 0 && !IsBusy;

    /// <inheritdoc />
    protected override void OnActivated()
    {
        if (_instances is not null && !_subscribed)
        {
            _instances.CurrentChanged += OnCurrentChanged;
            _subscribed = true;
        }

        SyncInstance(_instances?.Current);
        BuildQuickActions();
        RefreshScreenshotsCommand.Execute(null);
    }

    /// <inheritdoc />
    protected override void OnDeactivated()
    {
        if (_instances is not null && _subscribed)
        {
            _instances.CurrentChanged -= OnCurrentChanged;
            _subscribed = false;
        }
    }

    /// <summary>切换实例后：概览区与截图一起换成本实例的。</summary>
    private void OnCurrentChanged(InstanceInfo? current)
    {
        SyncInstance(current);
        RefreshScreenshotsCommand.Execute(null);
    }

    /// <summary>把当前实例写进可绑定字段（派生属性由 <see cref="OnInstanceChanged"/> 统一通知）。</summary>
    private void SyncInstance(InstanceInfo? current) => Instance = current;

    /// <summary>实例变化后统一通知派生属性（源生成器只通知 <see cref="Instance"/> 本身）。</summary>
    partial void OnInstanceChanged(InstanceInfo? value)
    {
        OnPropertyChanged(nameof(HasInstance));
        OnPropertyChanged(nameof(ShowNoInstance));
        OnPropertyChanged(nameof(CanLaunch));
        OnPropertyChanged(nameof(ShowLockedHint));
        OnPropertyChanged(nameof(ShowDefaultBadge));
        OnPropertyChanged(nameof(InstanceName));
        OnPropertyChanged(nameof(InstanceVersionText));
        OnPropertyChanged(nameof(ModuleCountText));
        OnPropertyChanged(nameof(GameDirText));
        OnPropertyChanged(nameof(ScreenshotFolderText));
        OnPropertyChanged(nameof(ShowScreenshotRail));
        LaunchGameCommand.NotifyCanExecuteChanged();
        OpenGameFolderCommand.NotifyCanExecuteChanged();
        OpenScreenshotsFolderCommand.NotifyCanExecuteChanged();
    }

    partial void OnStatusTextChanged(string value) => OnPropertyChanged(nameof(HasStatus));

    partial void OnErrorTextChanged(string value) => OnPropertyChanged(nameof(HasError));

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(ShowEmptyScreenshots));

    /// <summary>按实例的启动命令启动游戏（优先用实例里保存的，其次用本机推断的默认值）。</summary>
    [RelayCommand(CanExecute = nameof(CanLaunch))]
    private async Task LaunchGameAsync()
    {
        SetError(null);
        StatusText = string.Empty;
        if (Instance is not { } instance)
        {
            return;
        }

        if (ShellLauncher is null)
        {
            SetError(Text("启动通道尚未接入（未注入外壳启动器），可先用「打开游戏目录」自行启动。", LocalizationKeys.HomeErrorLaunchMissing));
            return;
        }

        var command = await ResolveLaunchCommandAsync(instance.GameDir).ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(command))
        {
            SetError(Text("该实例没有可用的启动命令，请到「实例管理」里编辑启动命令。", LocalizationKeys.HomeErrorNoLaunchCommand));
            return;
        }

        var result = await ShellLauncher.LaunchGameAsync(instance.GameDir, command!).ConfigureAwait(true);
        if (result.IsSuccess)
        {
            StatusText = Text("已启动：{0}", LocalizationKeys.HomeStatusLaunched, command);
            return;
        }

        SetError(
            InstancesErrorText.Compose(
                result.Message,
                Text("启动游戏失败", LocalizationKeys.HomeErrorLaunchFailed),
                result.Retryable),
            result.Detail);
    }

    /// <summary>
    /// 取第一条可用启动命令；没有就返回 null。
    /// 回落链只有一份实现（顶栏常驻按钮也调它），否则同一个实例会出现两种答案。
    /// </summary>
    private Task<string?> ResolveLaunchCommandAsync(string gameDir)
        => InstanceLaunchCommandResolver.ResolveAsync(CommandStore, gameDir);

    /// <summary>在文件管理器里打开游戏目录。</summary>
    [RelayCommand(CanExecute = nameof(HasInstance))]
    private Task OpenGameFolderAsync()
        => OpenFolderAsync(GameDirText, "当前实例没有可打开的游戏目录。");

    /// <summary>在文件管理器里打开截图目录。</summary>
    [RelayCommand(CanExecute = nameof(HasInstance))]
    private Task OpenScreenshotsFolderAsync()
        => OpenFolderAsync(ScreenshotFolderText, "当前实例没有可打开的游戏目录。");

    private async Task OpenFolderAsync(string? path, string emptyMessage)
    {
        SetError(null);
        if (string.IsNullOrWhiteSpace(path))
        {
            SetError(emptyMessage);
            return;
        }

        if (ShellLauncher is null)
        {
            SetError(Text("打开目录的能力尚未接入（未注入外壳启动器）。", LocalizationKeys.HomeErrorLauncherMissing));
            return;
        }

        var result = await ShellLauncher.OpenFolderAsync(path!).ConfigureAwait(true);
        if (!result.IsSuccess)
        {
            SetError(
                InstancesErrorText.Compose(
                    result.Message,
                    Text("打开目录失败", LocalizationKeys.HomeErrorOpenFolderFailed),
                    result.Retryable),
                result.Detail);
        }
    }

    /// <summary>重新枚举当前实例的截图目录。</summary>
    [RelayCommand]
    private async Task RefreshScreenshotsAsync()
    {
        Screenshots.Clear();
        if (_screenshots is null || Instance?.GameDir is not { Length: > 0 } gameDir)
        {
            NotifyScreenshotState();
            return;
        }

        IsBusy = true;
        try
        {
            var result = await _screenshots.ListAsync(gameDir, ScreenshotLimit).ConfigureAwait(true);
            // 枚举期间实例被切走：这批结果属于上一个实例，直接丢弃。
            if (!string.Equals(Instance?.GameDir, gameDir, StringComparison.Ordinal))
            {
                return;
            }

            if (!result.IsSuccess)
            {
                SetError(
                    InstancesErrorText.Compose(
                        result.Message,
                        Text("读取截图失败", LocalizationKeys.HomeErrorLoadScreenshots),
                        result.Retryable),
                    result.Detail);
                return;
            }

            foreach (var item in result.Value ?? Array.Empty<GameScreenshotInfo>())
            {
                Screenshots.Add(CreateRow(item));
            }
        }
        catch (Exception exception)
        {
            SetError(InstancesErrorText.FromException(
                exception,
                Text("读取截图失败", LocalizationKeys.HomeErrorLoadScreenshots)));
        }
        finally
        {
            IsBusy = false;
            NotifyScreenshotState();
        }
    }

    private HomeScreenshotRowViewModel CreateRow(GameScreenshotInfo item)
        => new(
            item.FilePath,
            item.FileName,
            Text("拍摄于 {0}", LocalizationKeys.HomeScreenshotCapturedAt, FormatCapturedAt(item.CapturedAt)),
            Text("{0} MB", LocalizationKeys.HomeScreenshotSizeMb, FormatMegabytes(item.Bytes)));

    private static string FormatCapturedAt(DateTimeOffset stamp)
        => stamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture);

    private static string FormatMegabytes(long bytes)
        => (bytes / (1024d * 1024d)).ToString("0.#", CultureInfo.CurrentCulture);

    /// <summary>跳到另一页（快捷操作卡片与「切换实例」共用）。</summary>
    [RelayCommand]
    private async Task NavigateAsync(string? pageKey)
    {
        if (_navigation is null || string.IsNullOrWhiteSpace(pageKey))
        {
            return;
        }

        await _navigation.NavigateAsync(pageKey!).ConfigureAwait(true);
    }

    /// <summary>把七张常用功能卡建好（卡片内容是静态的，建一次就够）。</summary>
    private void BuildQuickActions()
    {
        if (PrimaryActions.Count > 0)
        {
            return;
        }

        PrimaryActions.Add(CreateAction(
            "模组库", LocalizationKeys.HomeActionMods,
            "搜索、安装与更新模组", LocalizationKeys.HomeActionModsDesc,
            "Apps", "mods"));
        PrimaryActions.Add(CreateAction(
            "变更集", LocalizationKeys.HomeActionChanges,
            "查看待应用的安装计划", LocalizationKeys.HomeActionChangesDesc,
            "Sync", "changes"));
        PageActions.Add(CreateAction(
            "实例管理", LocalizationKeys.HomeActionInstances,
            "切换、克隆或添加实例", LocalizationKeys.HomeActionInstancesDesc,
            "Folder", "instances"));
        PageActions.Add(CreateAction(
            "存档", LocalizationKeys.HomeActionSaves,
            "浏览与备份游戏存档", LocalizationKeys.HomeActionSavesDesc,
            "Archive", "saves"));
        PageActions.Add(CreateAction(
            "模组仓库", LocalizationKeys.HomeActionRepos,
            "管理 CKAN 仓库源", LocalizationKeys.HomeActionReposDesc,
            "Globe", "repos"));
        ToolActions.Add(CreateAction(
            "日志", LocalizationKeys.HomeActionLogs,
            "查看运行日志与错误", LocalizationKeys.HomeActionLogsDesc,
            "Bug", "logs"));
        ToolActions.Add(CreateAction(
            "设置", LocalizationKeys.HomeActionSettings,
            "语言、背景与兼容性选项", LocalizationKeys.HomeActionSettingsDesc,
            "Settings", "settings"));
    }

    private HomeQuickActionViewModel CreateAction(
        string titleFallback,
        string titleKey,
        string descriptionFallback,
        string descriptionKey,
        string glyph,
        string pageKey)
        => new(
            Text(titleFallback, titleKey),
            Text(descriptionFallback, descriptionKey),
            glyph,
            new AsyncRelayCommand(() => NavigateAsync(pageKey)));

    private void NotifyScreenshotState()
    {
        OnPropertyChanged(nameof(ScreenshotCountText));
        OnPropertyChanged(nameof(ShowEmptyScreenshots));
        OnPropertyChanged(nameof(ShowScreenshotRail));
    }

    /// <summary>写错误条（<paramref name="message"/> 为空表示清空）；技术细节跟在第二行。</summary>
    private void SetError(string? message, string? detail = null)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            ErrorText = string.Empty;
            return;
        }

        ErrorText = string.IsNullOrWhiteSpace(detail) ? message!.Trim() : message!.Trim() + Environment.NewLine + detail!.Trim();
    }
}
