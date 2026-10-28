using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using CkanUI.App.Views.Dialogs;
using CkanUI.ViewModels;
using CkanUI.ViewModels.Dialogs;

namespace CkanUI.App;

/// <summary>主窗口（03 篇 §1 外壳）。导航只做命令转发，状态全部在 ShellViewModel 里。</summary>
public sealed partial class MainWindow : Window
{
    /// <summary>页面切换动画的时长 token（20 篇 P-10 由 MotionHost 写入）。</summary>
    private const string PageMotionToken = "CkanMotionDurationPage";

    public MainWindow()
    {
        InitializeComponent();

        // 动效强度变化会重写这个 token；CrossFade.Duration 是普通属性，只能在代码里跟着重建。
        ResourcesChanged += (_, _) => ApplyPageTransition();
        ApplyPageTransition();

        // 日志抽屉「上拉半屏」（14 篇 §4.1）：按窗口实际高度算 MaxHeight，不写死像素。
        SizeChanged += (_, e) => UpdateLogDrawerHeight(e.NewSize.Height);
        UpdateLogDrawerHeight(Bounds.Height);
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
}
