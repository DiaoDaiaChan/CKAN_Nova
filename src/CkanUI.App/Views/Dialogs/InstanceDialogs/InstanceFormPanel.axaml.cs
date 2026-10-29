using System;
using System.ComponentModel;

using Avalonia.Controls;

using CkanUI.App.Animations;
using CkanUI.ViewModels.Dialogs;

namespace CkanUI.App.Views.Dialogs.InstanceDialogs;

/// <summary>
/// 实例表单面板（新增 / 克隆 / 重命名共用，03 篇 §8 D-06）。
/// </summary>
/// <remarks>
/// <para>视图只做渲染：字段绑定到 <c>InstanceFormViewModel</c>，提交/取消由该视图模型的命令处理，
/// 真正的服务调用在 <c>InstancesPageViewModel</c> 里。面板既可作为页面内联覆盖层使用，
/// 也可以在通用模态宿主就绪后原样放进对话框窗口，避免两份实现漂移。</para>
/// <para>校验反馈（20 篇 P-10，第三阶段 MAT-04）：<c>HasValidationError</c> 是视图模型的只读派生属性，
/// 红色描边由 XAML 的 <c>Classes.is-invalid</c> 绑定负责；这里补的是「变红的那一下要抖」——
/// 给根卡片挂一帧的冲量类，摘掉的瞬间由 SpringEasing 释放衰减震荡。</para>
/// </remarks>
public partial class InstanceFormPanel : UserControl
{
    private DialogViewModelBase? _viewModel;

    /// <summary>构造面板。</summary>
    public InstanceFormPanel()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel = null;
        }

        _viewModel = DataContext as DialogViewModelBase;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // ValidationError 变了会连带通知 HasValidationError，只认后者：它才是「有没有错」的单一真相。
        if (e.PropertyName != nameof(DialogViewModelBase.HasValidationError) || _viewModel is null)
        {
            return;
        }

        if (_viewModel.HasValidationError)
        {
            _ = MotionFeedback.ShakeAsync(FormCard);
        }
        else
        {
            MotionFeedback.ClearInvalid(FormCard);
        }
    }
}
