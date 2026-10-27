using System.Collections.Generic;
using CkanUI.Services.Abstractions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CkanUI.ViewModels.Shell;

/// <summary>
/// 系统托盘右键菜单（12 篇 F-51 / UI-26）：七项固定顺序 + 每项命令 + 最小化/关闭到托盘的开关口径。
/// </summary>
/// <remarks>
/// <para>
/// 分层：本类与 <see cref="TrayMenuItemViewModel"/> 只描述「菜单长什么样、点了做什么」，
/// 真正的窗口/目录/网络动作由 App 层的 <see cref="ITrayHostActions"/> 实现，
/// 图标与 <c>NativeMenu</c> 由 App 层的 <c>TrayHost</c> 装配 —— 因此本类可以脱离 GUI 单测。
/// </para>
/// <para>
/// 降级口径：没有宿主（<see cref="Host"/> 为空）或系统托盘不可用（<see cref="IsTrayAvailable"/> 为 false）时，
/// 菜单命令一律不可执行，<see cref="HideOnMinimize"/> / <see cref="HideOnClose"/> 一律为 false，
/// 窗口按「最小化留在任务栏、关闭即退出」的老行为走，不会把用户困在没有窗口也没有图标的进程里。
/// </para>
/// </remarks>
public sealed partial class TrayMenuViewModel : ObservableObject
{
    /// <summary>托盘提示文字的产品名。</summary>
    public const string ProductTitle = "CKAN";

    private readonly TrayMenuItemViewModel _showWindowItem;
    private readonly TrayMenuItemViewModel _hideWindowItem;
    private readonly TrayMenuItemViewModel _openLogFolderItem;
    private readonly TrayMenuItemViewModel _openCacheFolderItem;
    private readonly TrayMenuItemViewModel _openInstanceFolderItem;
    private readonly TrayMenuItemViewModel _refreshRepositoriesItem;
    private readonly TrayMenuItemViewModel _exitItem;

    private ITrayHostActions? _host;
    private bool _isTrayAvailable = true;
    private bool _minimizeToTray;
    private bool _closeToTray;

    [ObservableProperty]
    private string _statusText = "尚未执行托盘动作";

    /// <summary>构造菜单；不传宿主时全部命令不可执行（设计时预览、无托盘会话）。</summary>
    /// <param name="host">宿主动作实现；可稍后用 <see cref="Host"/> 接上。</param>
    public TrayMenuViewModel(ITrayHostActions? host = null)
    {
        _host = host;

        _showWindowItem = new TrayMenuItemViewModel(
            TrayMenuItemId.ShowWindow,
            "显示主窗口",
            ShowWindowAsync,
            () => HasHost);
        _hideWindowItem = new TrayMenuItemViewModel(
            TrayMenuItemId.HideWindow,
            "隐藏到托盘",
            HideWindowAsync,
            () => HasHost);
        _openLogFolderItem = new TrayMenuItemViewModel(
            TrayMenuItemId.OpenLogFolder,
            "打开日志目录",
            () => OpenFolderAsync(a => a.OpenLogFolder(), "日志目录"),
            () => HasHost,
            startsGroup: true);
        _openCacheFolderItem = new TrayMenuItemViewModel(
            TrayMenuItemId.OpenCacheFolder,
            "打开缓存目录",
            () => OpenFolderAsync(a => a.OpenCacheFolder(), "缓存目录"),
            () => HasHost);
        _openInstanceFolderItem = new TrayMenuItemViewModel(
            TrayMenuItemId.OpenInstanceFolder,
            "打开当前实例目录",
            () => OpenFolderAsync(a => a.OpenInstanceFolder(), "实例目录"),
            () => HasHost);
        _refreshRepositoriesItem = new TrayMenuItemViewModel(
            TrayMenuItemId.RefreshRepositories,
            "刷新仓库",
            RefreshRepositoriesAsync,
            () => HasHost,
            startsGroup: true);
        _exitItem = new TrayMenuItemViewModel(
            TrayMenuItemId.Exit,
            "退出",
            ExitAsync,
            () => HasHost,
            startsGroup: true);

        Items = new[]
        {
            _showWindowItem,
            _hideWindowItem,
            _openLogFolderItem,
            _openCacheFolderItem,
            _openInstanceFolderItem,
            _refreshRepositoriesItem,
            _exitItem,
        };
    }

    /// <summary>七项菜单，顺序即原生菜单顺序。</summary>
    public IReadOnlyList<TrayMenuItemViewModel> Items { get; }

    /// <summary>宿主动作实现；换人（或置空）时同步刷新七项命令的可用性。</summary>
    public ITrayHostActions? Host
    {
        get => _host;
        set
        {
            if (ReferenceEquals(_host, value))
            {
                return;
            }

            _host = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasHost));
            foreach (var item in Items)
            {
                item.NotifyCanExecuteChanged();
            }

            if (!HasHost)
            {
                StatusText = "当前环境没有托盘宿主，菜单动作不可用";
            }
        }
    }

    /// <summary>是否已接上宿主。</summary>
    public bool HasHost => _host is not null;

    /// <summary>
    /// 系统托盘是否可用。由 App 层的 <c>TrayHost</c> 在创建图标失败（无托盘环境）时置 false；
    /// 默认 true 让单测与设计时预览不必先造一个托盘。
    /// </summary>
    public bool IsTrayAvailable
    {
        get => _isTrayAvailable;
        private set
        {
            if (SetProperty(ref _isTrayAvailable, value))
            {
                OnPropertyChanged(nameof(HideOnMinimize));
                OnPropertyChanged(nameof(HideOnClose));
                OnPropertyChanged(nameof(ToolTipText));
            }
        }
    }

    /// <summary>设置项「最小化到托盘」（<see cref="UiSettings.MinimizeToTray"/> 的投影）。</summary>
    public bool MinimizeToTray
    {
        get => _minimizeToTray;
        private set
        {
            if (SetProperty(ref _minimizeToTray, value))
            {
                OnPropertyChanged(nameof(HideOnMinimize));
            }
        }
    }

    /// <summary>设置项「关闭到托盘」（<see cref="UiSettings.CloseToTray"/> 的投影）。</summary>
    public bool CloseToTray
    {
        get => _closeToTray;
        private set
        {
            if (SetProperty(ref _closeToTray, value))
            {
                OnPropertyChanged(nameof(HideOnClose));
            }
        }
    }

    /// <summary>最小化时是否只留托盘图标：开关打开 **且** 托盘可用。</summary>
    public bool HideOnMinimize => IsTrayAvailable && MinimizeToTray;

    /// <summary>点关闭时是否隐藏到托盘：开关打开 **且** 托盘可用。</summary>
    public bool HideOnClose => IsTrayAvailable && CloseToTray;

    /// <summary>托盘图标的提示文字。</summary>
    public string ToolTipText => IsTrayAvailable ? ProductTitle : ProductTitle + "（托盘不可用）";

    /// <summary>「显示主窗口」命令。</summary>
    public IAsyncRelayCommand ShowWindowCommand => _showWindowItem.Command;

    /// <summary>「隐藏到托盘」命令。</summary>
    public IAsyncRelayCommand HideWindowCommand => _hideWindowItem.Command;

    /// <summary>「打开日志目录」命令。</summary>
    public IAsyncRelayCommand OpenLogFolderCommand => _openLogFolderItem.Command;

    /// <summary>「打开缓存目录」命令。</summary>
    public IAsyncRelayCommand OpenCacheFolderCommand => _openCacheFolderItem.Command;

    /// <summary>「打开当前实例目录」命令。</summary>
    public IAsyncRelayCommand OpenInstanceFolderCommand => _openInstanceFolderItem.Command;

    /// <summary>「刷新仓库」命令。</summary>
    public IAsyncRelayCommand RefreshRepositoriesCommand => _refreshRepositoriesItem.Command;

    /// <summary>「退出」命令。</summary>
    public IAsyncRelayCommand ExitCommand => _exitItem.Command;

    /// <summary>按标识取菜单项；不存在时返回 null。</summary>
    /// <param name="id">菜单项标识。</param>
    public TrayMenuItemViewModel? FindItem(TrayMenuItemId id)
    {
        foreach (var item in Items)
        {
            if (item.Id == id)
            {
                return item;
            }
        }

        return null;
    }

    /// <summary>把「托盘是否可用」的判定写进来（<c>TrayHost</c> 创建图标失败时调用）。</summary>
    /// <param name="available">托盘是否可用。</param>
    public void SetTrayAvailable(bool available) => IsTrayAvailable = available;

    /// <summary>
    /// 应用一份 UI 设置：只取两个托盘开关，其它字段不关心（设置页保存后由 <c>TrayHost</c> 转发）。
    /// </summary>
    /// <param name="settings">最新的 UI 设置；为空时按「两个开关都关」处理。</param>
    public void ApplySettings(UiSettings? settings)
    {
        MinimizeToTray = settings?.MinimizeToTray ?? false;
        CloseToTray = settings?.CloseToTray ?? false;
    }

    private async Task ShowWindowAsync()
    {
        if (_host is not { } host)
        {
            StatusText = "当前环境没有托盘宿主，无法显示主窗口";
            return;
        }

        await host.ShowWindow().ConfigureAwait(true);
        StatusText = "已显示主窗口";
    }

    private async Task HideWindowAsync()
    {
        if (_host is not { } host)
        {
            StatusText = "当前环境没有托盘宿主，无法隐藏窗口";
            return;
        }

        await host.HideWindow().ConfigureAwait(true);
        StatusText = "已隐藏到托盘";
    }

    private async Task OpenFolderAsync(Func<ITrayHostActions, Task<OperationResult>> action, string what)
    {
        if (_host is not { } host)
        {
            StatusText = "当前环境没有托盘宿主，无法打开" + what;
            return;
        }

        var result = await action(host).ConfigureAwait(true);
        StatusText = result.IsSuccess
            ? "已打开" + what
            : what + "打开失败：" + (result.Message ?? "未知原因");
    }

    private async Task RefreshRepositoriesAsync()
    {
        if (_host is not { } host)
        {
            StatusText = "当前环境没有托盘宿主，无法刷新仓库";
            return;
        }

        var result = await host.RefreshRepositories().ConfigureAwait(true);
        StatusText = result.IsSuccess
            ? "仓库索引已刷新"
            : "刷新仓库失败：" + (result.Message ?? "未知原因");
    }

    private async Task ExitAsync()
    {
        if (_host is not { } host)
        {
            StatusText = "当前环境没有托盘宿主，无法退出";
            return;
        }

        StatusText = "正在退出…";
        await host.ExitAsync().ConfigureAwait(true);
    }
}
