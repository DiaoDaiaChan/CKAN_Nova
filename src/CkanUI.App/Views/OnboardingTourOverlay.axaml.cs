using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CkanUI.App.Controls;
using CkanUI.ViewModels.Onboarding;

namespace CkanUI.App.Views;

public sealed partial class OnboardingTourOverlay : UserControl
{
    private OnboardingTourViewModel? _viewModel;

    public OnboardingTourOverlay()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += (_, _) => UpdatePositionAndHighlight();
        SizeChanged += (_, _) => UpdatePositionAndHighlight();
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = DataContext as OnboardingTourViewModel;

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            if (_viewModel.IsActive)
            {
                UpdatePositionAndHighlight();
            }
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(OnboardingTourViewModel.CurrentStepIndex) or nameof(OnboardingTourViewModel.IsActive))
        {
            Dispatcher.UIThread.Post(UpdatePositionAndHighlight, DispatcherPriority.Render);
        }
    }

    public void UpdatePositionAndHighlight()
    {
        if (_viewModel is null || !_viewModel.IsActive || _viewModel.CurrentStep is null)
        {
            if (Spotlight is not null)
            {
                Spotlight.HasHighlight = false;
            }
            return;
        }

        var canvas = OverlayCanvas;
        var card = CardBorder;
        var spotlight = Spotlight;

        if (canvas is null || card is null || spotlight is null)
        {
            return;
        }

        double canvasWidth = canvas.Bounds.Width;
        double canvasHeight = canvas.Bounds.Height;

        if (canvasWidth <= 0 || canvasHeight <= 0)
        {
            return;
        }

        double cardWidth = card.Bounds.Width > 0 ? card.Bounds.Width : 380;
        double cardHeight = card.Bounds.Height > 0 ? card.Bounds.Height : 220;

        var step = _viewModel.CurrentStep;

        if (step.TargetType == OnboardingTargetType.Center || string.IsNullOrEmpty(step.TargetElementName))
        {
            spotlight.HasHighlight = false;

            // 居中显示卡片
            double left = Math.Max(16, (canvasWidth - cardWidth) / 2);
            double top = Math.Max(16, (canvasHeight - cardHeight) / 2);

            Canvas.SetLeft(card, left);
            Canvas.SetTop(card, top);
            return;
        }

        // 查找目标控件
        var root = VisualRoot as Visual;
        var targetControl = FindNamedControl(root, step.TargetElementName);

        if (targetControl is null || !targetControl.IsVisible || targetControl.Bounds.Width <= 0)
        {
            spotlight.HasHighlight = false;
            double left = Math.Max(16, (canvasWidth - cardWidth) / 2);
            double top = Math.Max(16, (canvasHeight - cardHeight) / 2);
            Canvas.SetLeft(card, left);
            Canvas.SetTop(card, top);
            return;
        }

        // 计算目标控件在当前 Canvas 下的相对坐标
        var targetMatrix = targetControl.TransformToVisual(canvas);
        if (targetMatrix is null)
        {
            spotlight.HasHighlight = false;
            return;
        }

        var targetTopLeft = targetMatrix.Value.Transform(new Point(0, 0));
        var targetRect = new Rect(targetTopLeft, targetControl.Bounds.Size);

        // 外扩一点内边距（Padding 4px），让高亮洞口更美观
        const double padding = 4;
        var highlightRect = new Rect(
            Math.Max(0, targetRect.X - padding),
            Math.Max(0, targetRect.Y - padding),
            targetRect.Width + padding * 2,
            targetRect.Height + padding * 2);

        spotlight.HighlightRect = highlightRect;
        spotlight.HasHighlight = true;

        // 根据步骤类型智能摆放卡片
        double cardLeft;
        double cardTop;

        switch (step.TargetType)
        {
            case OnboardingTargetType.NavigationSidebar:
                // 侧边栏在左侧，卡片摆在侧边栏右侧偏上
                cardLeft = highlightRect.Right + 16;
                cardTop = Math.Clamp(highlightRect.Top + 60, 20, canvasHeight - cardHeight - 20);
                break;

            case OnboardingTargetType.TitleBarControls:
                // 顶栏控制按钮下方偏右
                cardLeft = Math.Clamp(highlightRect.Left, 20, canvasWidth - cardWidth - 20);
                cardTop = highlightRect.Bottom + 16;
                break;

            case OnboardingTargetType.SectionTabs:
                // 页签下方
                cardLeft = Math.Clamp(highlightRect.Left, 20, canvasWidth - cardWidth - 20);
                cardTop = highlightRect.Bottom + 16;
                break;

            case OnboardingTargetType.StatusBar:
                // 底部状态栏上方
                cardLeft = Math.Clamp(highlightRect.Right - cardWidth, 20, canvasWidth - cardWidth - 20);
                cardTop = Math.Max(20, highlightRect.Top - cardHeight - 16);
                break;

            default:
                cardLeft = (canvasWidth - cardWidth) / 2;
                cardTop = (canvasHeight - cardHeight) / 2;
                break;
        }

        // 确保不会溢出屏幕边缘
        cardLeft = Math.Clamp(cardLeft, 16, Math.Max(16, canvasWidth - cardWidth - 16));
        cardTop = Math.Clamp(cardTop, 16, Math.Max(16, canvasHeight - cardHeight - 16));

        Canvas.SetLeft(card, cardLeft);
        Canvas.SetTop(card, cardTop);
    }

    private static Control? FindNamedControl(Visual? root, string name)
    {
        if (root is null)
        {
            return null;
        }

        if (root is Control ctrl && string.Equals(ctrl.Name, name, StringComparison.Ordinal))
        {
            return ctrl;
        }

        foreach (var child in root.GetVisualChildren())
        {
            var found = FindNamedControl(child, name);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }
}
