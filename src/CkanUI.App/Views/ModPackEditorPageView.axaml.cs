using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;


using CkanUI.App.Animations;
using CkanUI.ViewModels;
using CkanUI.ViewModels.Pages;

namespace CkanUI.App.Views;

/// <summary>
/// modpack 编辑器视图（06 篇 UI-24）：只做绑定与生命周期接线，业务全在
/// <see cref="ModPackEditorPageViewModel"/> 里。
/// </summary>
/// <remarks>
/// <para>视图挂到可视树时激活 VM，移出时取消激活；编辑器不订阅服务事件，因此两者都是幂等的空操作。</para>
/// <para>校验反馈（MAT-04）：视图模型只报「哪些字段出错」（<c>InvalidFields</c>），不认识控件；
/// 这里按 <see cref="Control.Tag"/> 上写的字段名找到对应输入框，标红并给一次冲量轻颤。
/// 字段名与 <c>ModPackEditorDocument</c> 的属性名一致，由 validator 的 <c>nameof(document.X)</c> 决定。</para>
/// </remarks>
public partial class ModPackEditorPageView : UserControl
{
    private ModPackEditorPageViewModel? _viewModel;

    /// <summary>构造视图（ViewLocator 无参构造）。</summary>
    public ModPackEditorPageView()
    {
        InitializeComponent();
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
        DataContextChanged += OnDataContextChanged;
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        (DataContext as PageViewModel)?.Activate();
        HookViewModel(DataContext as ModPackEditorPageViewModel);
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        UnhookViewModel();
        (DataContext as PageViewModel)?.Deactivate();
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
        => HookViewModel(DataContext as ModPackEditorPageViewModel);

    private void HookViewModel(ModPackEditorPageViewModel? viewModel)
    {
        if (ReferenceEquals(_viewModel, viewModel))
        {
            return;
        }

        UnhookViewModel();
        _viewModel = viewModel;

        if (_viewModel is null)
        {
            return;
        }

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        // 换绑时（例如加载了另一个文件）先把当前错误态铺上去，但不抖 —— 抖只属于「刚点完校验」。
        ApplyInvalidFields(_viewModel.InvalidFields, animate: false);
    }

    private void UnhookViewModel()
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel = null;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ModPackEditorPageViewModel.InvalidFields) || _viewModel is null)
        {
            return;
        }

        ApplyInvalidFields(_viewModel.InvalidFields, animate: true);
    }

    /// <summary>把「出错字段集合」映射到界面：命中的标红（并按需要轻颤），未命中的复位。</summary>
    /// <param name="fields">出错字段名（<c>ModPackEditorDocument</c> 的属性名）。</param>
    /// <param name="animate">true 表示刚校验完，给命中的控件一次冲量轻颤。</param>
    private void ApplyInvalidFields(IReadOnlyList<string>? fields, bool animate)
    {
        var invalid = fields ?? Array.Empty<string>();

        foreach (var control in this.GetVisualDescendants().OfType<Control>())
        {
            if (control.Tag is not string field || field.Length == 0)
            {
                continue;
            }

            if (!invalid.Contains(field, StringComparer.Ordinal))
            {
                MotionFeedback.ClearInvalid(control);
                continue;
            }

            if (animate)
            {
                _ = MotionFeedback.ShakeAsync(control);
            }
            else
            {
                control.Classes.Add(MotionFeedback.InvalidClass);
            }
        }
    }
}
