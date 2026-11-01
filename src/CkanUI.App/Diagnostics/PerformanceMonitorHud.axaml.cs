using System;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;

namespace CkanUI.App.Diagnostics;

public partial class PerformanceMonitorHud : UserControl
{
    public event EventHandler? CloseRequested;

    public PerformanceMonitorHud()
    {
        InitializeComponent();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnResetClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PerformanceMonitor monitor)
        {
            monitor.Reset();
        }
    }

    private void OnForceGcClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PerformanceMonitor monitor)
        {
            monitor.ForceGc();
        }
    }

    private async void OnCopyDiagnosticsClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not PerformanceMonitor monitor) return;

        var text = $"[CKAN-UI Performance Report]\n" +
                   $"Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n" +
                   $"FPS: {monitor.CurrentFps:F1} (Avg: {monitor.AvgFps:F1}, 1% Low: {monitor.OnePercentLowFps:F1})\n" +
                   $"Frame Time: Current: {monitor.CurrentFrameMs:F2}ms, p50: {monitor.P50FrameMs:F2}ms, p99: {monitor.P99FrameMs:F2}ms\n" +
                   $"Dispatcher Lag: {monitor.DispatcherLagMs:F2}ms\n" +
                   $"Hitches: >16.7ms: {monitor.Hitches16Count}, >33.3ms: {monitor.Hitches33Count}\n" +
                   $"Memory Heap: {monitor.MemoryMb:F1} MB (Gen0: {monitor.Gen0Count}, Gen1: {monitor.Gen1Count}, Gen2: {monitor.Gen2Count})";

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.Clipboard is not null)
        {
            await topLevel.Clipboard.SetTextAsync(text);
        }
    }
}
