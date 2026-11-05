using System;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace CkanUI.App.Ai;

/// <summary>
/// AI 助手独立弹出窗口：支持在桌面上随意移动、放大缩小，与主界面并行工作，随时一键嵌入回侧边栏。
/// </summary>
public partial class AiAssistantWindow : Window
{
    /// <summary>用户点击「嵌入主窗口」时触发。</summary>
    public event Action? DockBackRequested;

    /// <summary>标记当前关闭是否由嵌入主窗口触发，避免重复 Dispose。</summary>
    public bool IsDockingBack { get; private set; }

    public AiAssistantWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 将 AI 助手视图挂载到当前独立窗口。
    /// </summary>
    public void SetView(Control? view)
    {
        ViewHost.Content = view;
    }

    /// <summary>
    /// 解除 AI 助手视图在当前窗口的挂载并返回，便于迁移回主窗口。
    /// </summary>
    public Control? ReleaseView()
    {
        var view = ViewHost.Content as Control;
        ViewHost.Content = null;
        return view;
    }

    private void OnDockBackClick(object? sender, RoutedEventArgs e)
    {
        IsDockingBack = true;
        DockBackRequested?.Invoke();
    }
}
