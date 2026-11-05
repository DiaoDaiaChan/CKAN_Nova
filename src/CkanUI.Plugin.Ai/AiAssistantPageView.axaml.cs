using System;
using System.Collections.Specialized;
using System.ComponentModel;

using Avalonia.Controls;

namespace CkanUI.Plugin.Ai;

/// <summary>
/// 「AI 助手」页签视图。
/// </summary>
/// <remarks>
/// <para>必须是 <c>XxxPageView</c>、与 <see cref="AiAssistantPageViewModel"/> 同命名空间同程序集、且有无参构造函数：
/// 宿主 ViewLocator 靠这条约定把插件页 VM 映射到视图（docs/design/plugins.md §6.1）。</para>
/// <para>视图代码只做两件事：把「已挂到可视树」告诉 VM（只初始化一次，不轮询），
/// 以及新消息进来时把对话滚到底——否则回答长了用户得自己拖。</para>
/// </remarks>
public partial class AiAssistantPageView : UserControl
{
    private AiAssistantPageViewModel? _observed;

    /// <summary>供宿主 ViewLocator 反射实例化。</summary>
    public AiAssistantPageView()
    {
        InitializeComponent();
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
        DataContextChanged += OnDataContextChanged;

        var input = this.FindControl<TextBox>("InputTextBox");
        if (input is not null)
        {
            input.KeyDown += OnInputKeyDown;
        }
    }

    private void OnInputKeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        if (e.Key == Avalonia.Input.Key.Enter && !e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Shift))
        {
            e.Handled = true;
            if (_observed?.SendCommand.CanExecute(null) == true)
            {
                _observed.SendCommand.Execute(null);
            }
        }
    }

    private void OnAttachedToVisualTree(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        (DataContext as AiAssistantPageViewModel)?.Initialize();
    }

    private void OnDetachedFromVisualTree(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
        => Detach();

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        Attach(DataContext as AiAssistantPageViewModel);
    }

    private void Attach(AiAssistantPageViewModel? viewModel)
    {
        if (ReferenceEquals(_observed, viewModel))
        {
            return;
        }

        Detach();
        _observed = viewModel;
        if (_observed is null)
        {
            return;
        }

        _observed.Messages.CollectionChanged += OnMessagesChanged;
        _observed.PropertyChanged += OnViewModelPropertyChanged;
        ScrollToEnd();
    }

    private void Detach()
    {
        if (_observed is null)
        {
            return;
        }

        _observed.Messages.CollectionChanged -= OnMessagesChanged;
        _observed.PropertyChanged -= OnViewModelPropertyChanged;
        _observed = null;
    }

    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e) => ScrollToEnd();

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AiAssistantPageViewModel.HasToolTraces))
        {
            ScrollToEnd();
        }
    }

    private void ScrollToEnd() => MessageScroll.ScrollToEnd();
}
