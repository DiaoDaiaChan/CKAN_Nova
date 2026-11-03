using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace CkanUI.App.Controls;

/// <summary>
/// 光标覆盖窗口：一个置顶、点击穿透、不抢焦点的透明窗口，专门用来画自绘光标。
///
/// 为什么不能在主窗口里画：Win32 上 <c>Popup</c>（ComboBox 下拉、MenuFlyout、ContextMenu）
/// 会被挂到独立的 <c>PopupRoot</c> 子窗口上，主窗口内容再高的 ZIndex 也只能在主窗口内部排序，
/// 永远排在子窗口下面。唯一能压住它们的，是另一个**顶层**窗口。
/// </summary>
public sealed class CursorOverlayWindow : Window
{
    private const int GwlExStyle = -20;
    private const long WsExTransparent = 0x00000020L;
    private const long WsExNoActivate = 0x08000000L;
    private const long WsExToolWindow = 0x00000080L;

    /// <summary>叠在主窗口之上的光标图层。</summary>
    public CursorPointerLayer CursorLayer { get; } = new();

    public CursorOverlayWindow()
    {
        WindowDecorations = Avalonia.Controls.WindowDecorations.None;
        CanResize = false;
        CanMinimize = false;
        CanMaximize = false;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Focusable = false;
        IsHitTestVisible = false;
        Background = Brushes.Transparent;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        Cursor = new Cursor(StandardCursorType.None);
        Content = CursorLayer;
    }

    /// <summary>跟随宿主窗口的位置与客户区尺寸；两者坐标系原点一致，指针坐标可直接复用。</summary>
    public void SyncTo(Window owner)
    {
        if (!owner.IsVisible)
        {
            return;
        }

        Position = owner.PointToScreen(new Point(0, 0));
        Width = Math.Max(1d, owner.ClientSize.Width);
        Height = Math.Max(1d, owner.ClientSize.Height);
    }

    /// <inheritdoc />
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        MakeClickThrough();
    }

    /// <summary>点击穿透 + 不抢焦点 + 不进 Alt-Tab：不这么做，覆盖窗口会把主窗口的鼠标事件全吃掉。</summary>
    private void MakeClickThrough()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var style = GetWindowLongPtr(handle, GwlExStyle).ToInt64();
        SetWindowLongPtr(handle, GwlExStyle, new IntPtr(style | WsExTransparent | WsExNoActivate | WsExToolWindow));
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
}
