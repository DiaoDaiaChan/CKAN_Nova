using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using CkanUI.App.Views.Dialogs;
using CkanUI.Services.Abstractions.Commands;
using CkanUI.Services.Commands;
using CkanUI.ViewModels;
using CkanUI.ViewModels.CommandPalette;
using CkanUI.ViewModels.Dialogs;

namespace CkanUI.App;

/// <summary>主窗口（03 篇 §1 外壳）。导航只做命令转发，状态全部在 ShellViewModel 里。</summary>
public sealed partial class MainWindow : Window
{
    /// <summary>页面切换动画的时长 token（20 篇 P-10 由 MotionHost 写入）。</summary>
    private const string PageMotionToken = "CkanMotionDurationPage";

    private CommandPaletteViewModel? _palette;
    private ICommandCatalog? _commands;
    private Func<string, string>? _translate;

    /// <summary>
    /// 宿主侧动作落点（UI-27 的 <c>action:</c> 命令）。返回 true 表示已处理。
    /// 外壳自己只认「打开面板 / 快捷键目录 / 前进后退」四条，其余动作的含义属于 App 层能力
    /// （托盘动作、诊断包、更新检查、主题与密度），所以由宿主注入。
    /// </summary>
    private Func<string, bool>? _paletteActionHost;

    public MainWindow()
    {
        InitializeComponent();

        // 动效强度变化会重写这个 token；CrossFade.Duration 是普通属性，只能在代码里跟着重建。
        ResourcesChanged += (_, _) => ApplyPageTransition();
        ApplyPageTransition();

        // 日志抽屉「上拉半屏」（14 篇 §4.1）：按窗口实际高度算 MaxHeight，不写死像素。
        SizeChanged += (_, e) => UpdateLogDrawerHeight(e.NewSize.Height);
        UpdateLogDrawerHeight(Bounds.Height);

        ApplyStatusBarFacts();
    }

    /// <summary>把日志抽屉的最大高度钉在窗口一半（下限 160px，保证至少能看到几行）。</summary>
    private void UpdateLogDrawerHeight(double windowHeight)
    {
        if (LogDrawerHost is null || double.IsNaN(windowHeight) || windowHeight <= 0)
        {
            return;
        }

        LogDrawerHost.MaxHeight = Math.Max(160d, windowHeight / 2d);
    }

    /// <summary>按当前动效强度装配页面切换动画；时长归零时干脆不装（关闭档要的是「瞬时」）。</summary>
    private void ApplyPageTransition()
    {
        if (PageHost is null)
        {
            return;
        }

        if (this.TryFindResource(PageMotionToken, out var value) && value is TimeSpan duration && duration > TimeSpan.Zero)
        {
            PageHost.PageTransition = new Avalonia.Animation.CrossFade { Duration = duration };
        }
        else
        {
            PageHost.PageTransition = null;
        }
    }

    /// <summary>
    /// 状态栏右下角常驻版本信息（报问题时截图就带上版本号，不必再问）。
    /// 只在窗口构造时采一次：发布后程序集版本不会变，重复采集反而给每帧加负担。
    /// </summary>
    private void ApplyStatusBarFacts()
    {
        if (StatusVersionText is null)
        {
            return;
        }

        var info = AboutInfoFactory.Create();
        StatusVersionText.Text = info.AppVersion;
        ToolTip.SetTip(StatusVersionText, $"CKAN 核心 {info.CoreVersion} · {info.RuntimeVersion}");
    }

    /// <summary>打开关于框（12 篇 F-55）；外部链接交给 Avalonia 的 Launcher，失败由 VM 显示提示。</summary>
    private async void OnAboutClick(object? sender, RoutedEventArgs e)
    {
        var top = TopLevel.GetTopLevel(this);
        Func<string, Task<bool>> openExternal = async url =>
        {
            if (top is null || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                return false;
            }

            return await top.Launcher.LaunchUriAsync(uri);
        };

        var viewModel = new AboutDialogViewModel(AboutInfoFactory.Create(), openExternal);
        var window = new AboutDialogView { DataContext = viewModel };

        if (IsVisible)
        {
            await window.ShowDialog(this);
        }
        else
        {
            window.Show();
        }
    }

    /// <summary>
    /// 挂命令面板（UI-27）：VM 与命令目录由 DI 给，外壳只管键位与「命令 Id → 动作」的映射。
    /// </summary>
    /// <param name="palette">命令面板 VM（DI 单例，状态下沉在它自己的属性里）。</param>
    /// <param name="commands">命令目录（快捷键目录要读它）。</param>
    /// <param name="translate">取译文委托（视图模型层不引用本地化层）。</param>
    /// <param name="actionHost">宿主侧动作落点，返回 true 表示已处理；未接线的动作在目录里已置为禁用。</param>
    public void AttachCommandPalette(
        CommandPaletteViewModel palette,
        ICommandCatalog commands,
        Func<string, string> translate,
        Func<string, bool>? actionHost = null)
    {
        ArgumentNullException.ThrowIfNull(palette);
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(translate);

        _palette = palette;
        _commands = commands;
        _translate = translate;
        _paletteActionHost = actionHost;
        CommandPaletteHost.DataContext = palette;
        palette.CommandInvoked += OnCommandInvoked;

        // 隧道阶段抢先处理：页面里的文本框会在冒泡阶段先吃掉 Ctrl+K / Ctrl+/，那时外壳已经轮不上了。
        AddHandler(KeyDownEvent, OnGlobalKeyDown, RoutingStrategies.Tunnel);
    }

    /// <summary>全局键位：Ctrl+K 开关命令面板，Ctrl+/ 打开快捷键目录。</summary>
    private void OnGlobalKeyDown(object? sender, KeyEventArgs e)
    {
        if (_palette is null || !e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            return;
        }

        switch (e.Key)
        {
            case Key.K:
                _palette.ToggleCommand.Execute(null);
                e.Handled = true;
                break;

            // 主键盘的 / 在 Avalonia 里叫 OemQuestion；小键盘的 / 是另一个枚举值，两个都收。
            case Key.OemQuestion:
            case Key.Divide:
                _ = ShowShortcutsAsync();
                e.Handled = true;
                break;
        }
    }

    /// <summary>命令 Id → 动作：先试导航键，再试外壳内置四条，最后交给宿主。</summary>
    private void OnCommandInvoked(string id)
    {
        if (DataContext is not ShellViewModel shell)
        {
            return;
        }

        if (DefaultCommands.TryGetNavigationPageKey(id, out var pageKey))
        {
            shell.NavigateCommand.Execute(pageKey);
            return;
        }

        if (id == DefaultCommands.ActionId("show-palette"))
        {
            _palette?.OpenCommand.Execute(null);
            return;
        }

        if (id == DefaultCommands.ActionId("show-shortcuts"))
        {
            _ = ShowShortcutsAsync();
            return;
        }

        if (id == DefaultCommands.ActionId("go-back"))
        {
            shell.BackCommand.Execute(null);
            return;
        }

        if (id == DefaultCommands.ActionId("go-forward"))
        {
            shell.ForwardCommand.Execute(null);
            return;
        }

        _paletteActionHost?.Invoke(id);
    }

    /// <summary>打开快捷键目录：每次新建一个（VM 继承 DialogViewModelBase，复用会带上一次关闭态）。</summary>
    private async Task ShowShortcutsAsync()
    {
        if (_commands is null)
        {
            return;
        }

        var viewModel = new ShortcutsCatalogViewModel(_commands, _translate);
        var window = new ShortcutsDialogView { DataContext = viewModel };
        if (IsVisible)
        {
            await window.ShowDialog(this);
        }
        else
        {
            window.Show();
        }
    }
}
