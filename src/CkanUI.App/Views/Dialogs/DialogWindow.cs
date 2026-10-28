using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using CkanUI.ViewModels.Dialogs;

namespace CkanUI.App.Views.Dialogs;

/// <summary>
/// 对话框窗口基类（05 篇 §3.2 / 03 篇 §8）：只负责"长什么样"和"自己关掉自己"。
/// 弹窗时机与 owner 一律由调用方（<c>IDialogService</c>）用 <c>ShowDialog(owner)</c> 决定，
/// 这里绝不调用 <c>Show()</c>，也不自己找 owner —— 后台线程弹框时 owner 必须由调用方在 UI 线程提前捕获。
/// </summary>
public class DialogWindow : Window
{
    /// <summary>页面级动效时长 token（20 篇 P-10 由 MotionHost 写入）。</summary>
    private const string MotionToken = "CkanMotionDurationPage";

    private INotifyPropertyChanged? _viewModel;

    /// <summary>初始化对话框窗口的公共外观（不可缩放、居中于 owner、不进任务栏）。</summary>
    protected DialogWindow()
    {
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        MinWidth = 360;
        MaxWidth = 760;
        MaxHeight = 720;
    }

    /// <summary>
    /// 入场动画（轻量淡入 + 96%→100% 缩放）：对话框是从「点下去的那一刻」开始的，
    /// 直接硬闪出来会让点击与结果之间缺少因果感。时长取 `CkanMotionDurationPage`，
    /// 关闭动效（时长 0）时干脆不挂动画 —— 无障碍设置优先于观感。
    /// </summary>
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        if (!this.TryFindResource(MotionToken, out var raw) || raw is not TimeSpan duration || duration <= TimeSpan.Zero)
        {
            return;
        }

        // 关掉动效还原时不能残留 96% 的缩放：先显式置回 1，动画再从 0.96 开始。
        RenderTransform = new ScaleTransform(1d, 1d);

        var entrance = new Animation
        {
            Duration = duration,
            Easing = new CubicEaseOut(),
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0d),
                    Setters =
                    {
                        new Setter(OpacityProperty, 0d),
                        new Setter(ScaleTransform.ScaleXProperty, 0.96d),
                        new Setter(ScaleTransform.ScaleYProperty, 0.96d),
                    },
                },
                new KeyFrame
                {
                    Cue = new Cue(1d),
                    Setters =
                    {
                        new Setter(OpacityProperty, 1d),
                        new Setter(ScaleTransform.ScaleXProperty, 1d),
                        new Setter(ScaleTransform.ScaleYProperty, 1d),
                    },
                },
            },
        };

        _ = entrance.RunAsync(this);
    }

    /// <inheritdoc />
    protected override void OnDataContextChanged(EventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        base.OnDataContextChanged(e);

        _viewModel = DataContext as INotifyPropertyChanged;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    /// <inheritdoc />
    protected override void OnClosed(EventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel = null;
        }

        base.OnClosed(e);
    }

    /// <summary>VM 置 <see cref="DialogViewModelBase.IsClosed"/> 时关窗；已关闭则忽略，避免重复 Close。</summary>
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(DialogViewModelBase.IsClosed) || !IsVisible)
        {
            return;
        }

        if (DataContext is DialogViewModelBase viewModel && viewModel.IsClosed)
        {
            Close();
        }
    }
}
