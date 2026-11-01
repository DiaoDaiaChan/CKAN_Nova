using System;
using System.IO;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

using CkanUI.App.Branding;
using CkanUI.Services.Abstractions;
using CkanUI.ViewModels.Shell;

namespace CkanUI.App.Tray;

/// <summary>
/// 托盘宿主（12 篇 F-51 / UI-26）：把 <see cref="TrayMenuViewModel"/> 装成真正的
/// <see cref="TrayIcon"/> + <see cref="NativeMenu"/>，并跟随 <see cref="UiSettings"/> 的两个开关。
/// </summary>
/// <remarks>
/// <para>
/// 结构与 <c>ThemeHost</c> 一致：<see cref="Install"/> 幂等、<see cref="Dispose"/> 反订阅并摘掉图标。
/// 图标本体只在 App 层创建（视图模型层禁止引用 Avalonia），菜单的命令仍由 VM 持有。
/// </para>
/// <para>
/// 无托盘环境（远程会话、精简 Linux 桌面、平台不支持）的降级：创建图标失败时
/// 把 <see cref="TrayMenuViewModel.IsTrayAvailable"/> 置 false，随后「最小化到托盘 / 关闭到托盘」
/// 一律不生效（窗口保持老行为），进程不会既没窗口也没图标。
/// </para>
/// </remarks>
public sealed class TrayHost : IDisposable
{
    private readonly Application _application;
    private readonly TrayMenuViewModel _menu;
    private readonly IUiSettingsStore _settings;
    private readonly IDispatcher _dispatcher;

    private TrayIcons? _icons;
    private TrayIcon? _icon;
    private bool _installed;

    /// <summary>构造托盘宿主。</summary>
    /// <param name="application">当前 Avalonia 应用（托盘图标挂在它的 <c>TrayIcon.Icons</c> 附加属性上）。</param>
    /// <param name="menu">托盘菜单视图模型。</param>
    /// <param name="settings">UI 自有设置存储（两个托盘开关的唯一真源）。</param>
    /// <param name="dispatcher">UI 线程封送器（<c>Changed</c> 可能在后台线程触发）。</param>
    public TrayHost(Application application, TrayMenuViewModel menu, IUiSettingsStore settings, IDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(menu);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(dispatcher);

        _application = application;
        _menu = menu;
        _settings = settings;
        _dispatcher = dispatcher;
    }

    /// <summary>是否已安装。</summary>
    public bool IsInstalled => _installed;

    /// <summary>托盘图标是否真的建起来了（无托盘环境为 false）。</summary>
    public bool HasIcon => _icon is not null;

    /// <summary>建图标、装菜单、订阅设置变更。可重复调用（幂等）。</summary>
    public void Install()
    {
        if (_installed)
        {
            return;
        }

        _installed = true;

        try
        {
            CreateIcon();
        }
        catch (Exception)
        {
            // 无托盘环境：不抛给启动流程，改为「托盘不可用」，窗口回到老的最小化/关闭语义。
            RemoveIcon();
            _menu.SetTrayAvailable(false);
        }

        Apply(SafeLoad());
        _settings.Changed += OnSettingsChanged;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (!_installed)
        {
            return;
        }

        _installed = false;
        _settings.Changed -= OnSettingsChanged;
        RemoveIcon();
    }

    /// <summary>应用一份设置：只取两个托盘开关，其它字段归各自宿主管。</summary>
    /// <param name="settings">最新设置。</param>
    public void Apply(UiSettings? settings)
    {
        _menu.ApplySettings(settings);
        if (_icon is not null)
        {
            _icon.ToolTipText = _menu.ToolTipText;
        }
    }

    private UiSettings? SafeLoad()
    {
        try
        {
            return _settings.Load();
        }
        catch (Exception)
        {
            // 设置文件损坏时按默认值（两个开关都关）走，不阻断启动。
            return null;
        }
    }

    private void CreateIcon()
    {
        // Avalonia 12 把托盘图标挂在 Application 的附加属性上（Avalonia 11 的 Application.TrayIcons 已移除）。
        var icons = TrayIcon.GetIcons(_application);
        if (icons is null)
        {
            icons = new TrayIcons();
            TrayIcon.SetIcons(_application, icons);
        }

        _icons = icons;

        var icon = new TrayIcon
        {
            ToolTipText = _menu.ToolTipText,
            Menu = BuildMenu(),
            // 左键单击 = 显示主窗口（与右键菜单的第一项同一命令）。
            Command = _menu.ShowWindowCommand,
            IsVisible = true,
        };

        if (LoadIconImage() is { } image)
        {
            icon.Icon = image;
        }

        icons.Add(icon);
        _icon = icon;
        _menu.SetTrayAvailable(true);
    }

    private void RemoveIcon()
    {
        if (_icon is { } icon)
        {
            try
            {
                _icons?.Remove(icon);
            }
            catch (Exception)
            {
                // 平台已经回收图标（会话切换、桌面重启）时摘除会抛；忽略即可。
            }

            icon.Dispose();
            _icon = null;
        }
    }

    private NativeMenu BuildMenu()
    {
        var menu = new NativeMenu();
        foreach (var item in _menu.Items)
        {
            if (item.StartsGroup && menu.Items.Count > 0)
            {
                menu.Add(new NativeMenuItemSeparator());
            }

            menu.Add(new NativeMenuItem(item.Header) { Command = item.Command });
        }

        return menu;
    }

    /// <summary>
    /// 托盘图标：优先用随包分发的 <see cref="AppIcon"/>（与窗口、资源管理器同一个 7 档 ICO），
    /// 保证托盘和 exe 图标是同一张脸；图标文件缺失或损坏时才退回自绘方块。
    /// </summary>
    private WindowIcon? LoadIconImage() => AppIcon.TryLoad() ?? DrawFallbackIcon();

    /// <summary>
    /// 兜底图标：用 Avalonia 内置几何现画一个 32×32 的方块 + 三条横杠（与正式图标同一套
    /// 「列表行」语汇），底色取当前强调色，不新增任何二进制资源（12 篇 F-51 的资源口径）。
    /// 画不出来（没有渲染后端）时返回 null，托盘图标退回平台默认外观。
    /// </summary>
    private WindowIcon? DrawFallbackIcon()
    {
        try
        {
            var background = new SolidColorBrush(ResolveAccent());
            // 方块上的横杠用最深的表面色而不是纯白：亮强调色（如高对比度的黄）上也要有对比度。
            var foreground = new SolidColorBrush(Color.FromRgb(0x0F, 0x12, 0x18));
            using var bitmap = new RenderTargetBitmap(new PixelSize(32, 32), new Vector(96, 96));
            using (var context = bitmap.CreateDrawingContext())
            {
                context.DrawRectangle(background, null, new Rect(0, 0, 32, 32), 7, 7);
                context.DrawRectangle(foreground, null, new Rect(7, 9, 18, 4), 2, 2);
                context.DrawRectangle(foreground, null, new Rect(7, 15, 18, 4), 2, 2);
                context.DrawRectangle(foreground, null, new Rect(7, 21, 11, 4), 2, 2);
            }

            using var stream = new MemoryStream();
            bitmap.Save(stream, PngBitmapEncoderOptions.Default);
            stream.Position = 0;
            return new WindowIcon(stream);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>当前强调色（取不到就退回 Tokens 里的暗色默认蓝，保证兜底图标不会是透明方块）。</summary>
    private Color ResolveAccent() =>
        _application.TryFindResource("CkanAccentBrush", out object? accent) && accent is ISolidColorBrush brush
            ? brush.Color
            : Color.FromRgb(0x72, 0xA7, 0xFF);

    private void OnSettingsChanged(UiSettings settings)
    {
        // 设置保存可能发生在后台线程，改 UI 侧状态必须回到 UI 线程。
        _dispatcher.Post(() => Apply(settings));
    }
}
