using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace CkanUI.App.Diagnostics;

/// <summary>
/// GUI 运行时性能与帧率监控器服务。
/// 负责采集 Avalonia 渲染帧时间、主线程调度延迟、GC 状态并向 UI 与 HUD 提供响应式数据流。
/// </summary>
public sealed class PerformanceMonitor : INotifyPropertyChanged
{
    private static readonly Lazy<PerformanceMonitor> _instance = new(() => new PerformanceMonitor());
    public static PerformanceMonitor Instance => _instance.Value;

    private readonly PerformanceTelemetryBuffer _telemetry = new(120);
    public PerformanceTelemetryBuffer Telemetry => _telemetry;
    private readonly Stopwatch _frameStopwatch = new();
    private readonly Stopwatch _heartbeatStopwatch = new();
    private DispatcherTimer? _heartbeatTimer;
    private DispatcherTimer? _uiRefreshTimer;
    private TopLevel? _attachedTopLevel;

    private bool _isOpen;
    private bool _isSampling;
    private long _lastFrameTicks;
    private long _lastHeartbeatTicks;
    private double _currentLagMs;

    private PerformanceSnapshot _currentSnapshot;

    public PerformanceMonitor()
    {
        _currentSnapshot = _telemetry.TakeSnapshot();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>动态刷新率与空闲降频控制器。</summary>
    public DynamicRefreshRateController DynamicRate => DynamicRefreshRateController.Instance;

    /// <summary>HUD 浮层是否展开（快捷键 F12 或点击状态栏切换）。</summary>
    public bool IsOpen
    {
        get => _isOpen;
        set
        {
            if (_isOpen != value)
            {
                _isOpen = value;
                OnPropertyChanged();
                if (_isOpen && !_isSampling)
                {
                    StartSampling();
                }
            }
        }
    }

    /// <summary>是否正在实时采样渲染帧率与调度状态。</summary>
    public bool IsSampling
    {
        get => _isSampling;
        private set
        {
            if (_isSampling != value)
            {
                _isSampling = value;
                OnPropertyChanged();
            }
        }
    }

    public PerformanceSnapshot Snapshot => _currentSnapshot;

    public double CurrentFps => _currentSnapshot.CurrentFps;
    public double AvgFps => _currentSnapshot.AvgFps;
    public double OnePercentLowFps => _currentSnapshot.OnePercentLowFps;
    public double CurrentFrameMs => _currentSnapshot.CurrentFrameMs;
    public double P50FrameMs => _currentSnapshot.P50FrameMs;
    public double P90FrameMs => _currentSnapshot.P90FrameMs;
    public double P95FrameMs => _currentSnapshot.P95FrameMs;
    public double P99FrameMs => _currentSnapshot.P99FrameMs;
    public double MaxFrameMs => _currentSnapshot.MaxFrameMs;
    public double DispatcherLagMs => _currentSnapshot.DispatcherLagMs;
    public double MaxDispatcherLagMs => _currentSnapshot.MaxDispatcherLagMs;
    public int TotalFrames => _currentSnapshot.TotalFrames;
    public int Hitches16Count => _currentSnapshot.Hitches16Count;
    public int Hitches33Count => _currentSnapshot.Hitches33Count;
    public double MemoryMb => Math.Round(_currentSnapshot.MemoryBytes / (1024.0 * 1024.0), 1);
    public int Gen0Count => _currentSnapshot.Gen0Count;
    public int Gen1Count => _currentSnapshot.Gen1Count;
    public int Gen2Count => _currentSnapshot.Gen2Count;

    public double[] RecentDurations => _telemetry.GetRecentFrameDurations();

    public string FpsSummaryText => _currentSnapshot.TotalFrames == 0 ? "FPS --" : $"{_currentSnapshot.CurrentFps:0.0} FPS";
    public string FrameTimeSummaryText => $"p50: {_currentSnapshot.P50FrameMs:0.0}ms | p99: {_currentSnapshot.P99FrameMs:0.0}ms";
    public string ResponsivenessText => $"延迟: {_currentSnapshot.DispatcherLagMs:0.0}ms | 卡顿: {_currentSnapshot.Hitches16Count}";
    public string MemorySummaryText => $"{MemoryMb:0.0} MB (GC: {Gen0Count}/{Gen1Count}/{Gen2Count})";

    /// <summary>按当前 FPS 返回健康状态色彩标识 (Green / Amber / Red)。</summary>
    public string FpsStatusTone => _currentSnapshot.CurrentFps >= 55.0 ? "Good" : _currentSnapshot.CurrentFps >= 30.0 ? "Warning" : "Critical";

    /// <summary>绑定 TopLevel 窗口并开始监听渲染帧回调。</summary>
    public void AttachToTopLevel(TopLevel topLevel)
    {
        if (ReferenceEquals(_attachedTopLevel, topLevel)) return;

        Detach();
        _attachedTopLevel = topLevel;
        StartSampling();
    }

    /// <summary>解绑当前 TopLevel。</summary>
    public void Detach()
    {
        _attachedTopLevel = null;
        StopSampling();
    }

    /// <summary>启动性能采样（若已启动则空操作）。</summary>
    public void StartSampling()
    {
        if (_isSampling) return;
        _isSampling = true;

        _frameStopwatch.Restart();
        _lastFrameTicks = _frameStopwatch.ElapsedTicks;

        _heartbeatStopwatch.Restart();
        _lastHeartbeatTicks = _heartbeatStopwatch.ElapsedTicks;

        // 调度器心跳：活跃期 50ms 检查，空闲期 250ms 检查
        var heartbeatInterval = DynamicRefreshRateController.Instance.IsIdle ? TimeSpan.FromMilliseconds(250) : TimeSpan.FromMilliseconds(50);
        _heartbeatTimer = new DispatcherTimer(heartbeatInterval, DispatcherPriority.Input, OnHeartbeatTick);
        _heartbeatTimer.Start();

        // UI 刷新定时器：活跃期 100ms 刷新，空闲期 500ms 刷新，降低无操作时的 CPU 唤醒
        var uiInterval = DynamicRefreshRateController.Instance.IsIdle ? TimeSpan.FromMilliseconds(500) : TimeSpan.FromMilliseconds(100);
        _uiRefreshTimer = new DispatcherTimer(uiInterval, DispatcherPriority.Background, OnUiRefreshTick);
        _uiRefreshTimer.Start();

        DynamicRefreshRateController.Instance.IdleStateChanged += OnIdleStateChanged;

        // 驱动动画帧循环（确保静止状态下若有动画需求或手动滚动能捕获到帧）
        RequestNextFrame();
        OnPropertyChanged(nameof(IsSampling));
    }

    /// <summary>停止性能采样并释放定时器。</summary>
    public void StopSampling()
    {
        if (!_isSampling) return;
        _isSampling = false;

        _heartbeatTimer?.Stop();
        _heartbeatTimer = null;

        _uiRefreshTimer?.Stop();
        _uiRefreshTimer = null;

        DynamicRefreshRateController.Instance.IdleStateChanged -= OnIdleStateChanged;

        _frameStopwatch.Stop();
        _heartbeatStopwatch.Stop();
        OnPropertyChanged(nameof(IsSampling));
    }

    /// <summary>开关 HUD 界面与采样。</summary>
    public void Toggle()
    {
        IsOpen = !IsOpen;
    }

    /// <summary>重置遥测统计与环形历史。</summary>
    public void Reset()
    {
        _telemetry.Reset();
        _currentLagMs = 0;
        RefreshSnapshot();
    }

    /// <summary>强制触发一次完整垃圾回收并立即更新度量。</summary>
    public void ForceGc()
    {
        GC.Collect(2, GCCollectionMode.Forced, true, true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, true, true);
        RefreshSnapshot();
    }

    /// <summary>记录单帧耗时与调度延迟（支持无头探针或外部手动注入）。</summary>
    public void RecordFrameManual(double durationMs, double dispatcherLagMs)
    {
        _telemetry.RecordFrame(durationMs, dispatcherLagMs);
    }

    private void RequestNextFrame()
    {
        if (!_isSampling || _attachedTopLevel is null) return;

        var dynamicRate = DynamicRefreshRateController.Instance;
        if (dynamicRate.IsIdle)
        {
            // 空闲状态下按目标降频帧率调度，避免高频占用
            var minIntervalMs = 1000.0 / Math.Max(1, dynamicRate.IdleTargetFps);
            var elapsedMs = (_frameStopwatch.ElapsedTicks - _lastFrameTicks) * 1000.0 / Stopwatch.Frequency;
            if (elapsedMs < minIntervalMs)
            {
                var delay = TimeSpan.FromMilliseconds(Math.Max(1, minIntervalMs - elapsedMs));
                DispatcherTimer.RunOnce(() =>
                {
                    if (_isSampling && _attachedTopLevel is not null)
                    {
                        _attachedTopLevel.RequestAnimationFrame(OnAnimationFrame);
                    }
                }, delay, DispatcherPriority.Background);
                return;
            }
        }

        _attachedTopLevel.RequestAnimationFrame(OnAnimationFrame);
    }

    private void OnAnimationFrame(TimeSpan timestamp)
    {
        if (!_isSampling) return;

        var currentTicks = _frameStopwatch.ElapsedTicks;
        var elapsedTicks = currentTicks - _lastFrameTicks;
        _lastFrameTicks = currentTicks;

        var durationMs = (elapsedTicks * 1000.0) / Stopwatch.Frequency;
        _telemetry.RecordFrame(durationMs, _currentLagMs);

        RequestNextFrame();
    }

    private void OnRenderTimerTick(TimeSpan timestamp)
    {
        // 若没有 AttachedTopLevel（例如部分纯 Headless 或全局渲染管线），也可以由 RenderTimer 驱动
        if (!_isSampling || _attachedTopLevel is not null) return;

        var currentTicks = _frameStopwatch.ElapsedTicks;
        var elapsedTicks = currentTicks - _lastFrameTicks;
        _lastFrameTicks = currentTicks;

        var durationMs = (elapsedTicks * 1000.0) / Stopwatch.Frequency;
        _telemetry.RecordFrame(durationMs, _currentLagMs);
    }

    private void OnIdleStateChanged(bool isIdle)
    {
        if (_heartbeatTimer is not null)
        {
            _heartbeatTimer.Interval = isIdle ? TimeSpan.FromMilliseconds(250) : TimeSpan.FromMilliseconds(50);
        }

        if (_uiRefreshTimer is not null)
        {
            _uiRefreshTimer.Interval = isIdle ? TimeSpan.FromMilliseconds(500) : TimeSpan.FromMilliseconds(100);
        }
    }

    private void OnHeartbeatTick(object? sender, EventArgs e)
    {
        if (!_isSampling) return;

        var currentTicks = _heartbeatStopwatch.ElapsedTicks;
        var elapsedTicks = currentTicks - _lastHeartbeatTicks;
        _lastHeartbeatTicks = currentTicks;

        var elapsedMs = (elapsedTicks * 1000.0) / Stopwatch.Frequency;
        var expectedIntervalMs = DynamicRefreshRateController.Instance.IsIdle ? 250.0 : 50.0;
        // 超出期望间隔的部分为主线程调度滞后
        var lag = Math.Max(0, elapsedMs - expectedIntervalMs);
        _currentLagMs = lag;
    }

    private void OnUiRefreshTick(object? sender, EventArgs e)
    {
        if (!_isSampling) return;
        RefreshSnapshot();
    }

    public void RefreshSnapshot()
    {
        _currentSnapshot = _telemetry.TakeSnapshot();

        OnPropertyChanged(nameof(Snapshot));
        OnPropertyChanged(nameof(CurrentFps));
        OnPropertyChanged(nameof(AvgFps));
        OnPropertyChanged(nameof(OnePercentLowFps));
        OnPropertyChanged(nameof(CurrentFrameMs));
        OnPropertyChanged(nameof(P50FrameMs));
        OnPropertyChanged(nameof(P90FrameMs));
        OnPropertyChanged(nameof(P95FrameMs));
        OnPropertyChanged(nameof(P99FrameMs));
        OnPropertyChanged(nameof(MaxFrameMs));
        OnPropertyChanged(nameof(DispatcherLagMs));
        OnPropertyChanged(nameof(MaxDispatcherLagMs));
        OnPropertyChanged(nameof(TotalFrames));
        OnPropertyChanged(nameof(Hitches16Count));
        OnPropertyChanged(nameof(Hitches33Count));
        OnPropertyChanged(nameof(MemoryMb));
        OnPropertyChanged(nameof(Gen0Count));
        OnPropertyChanged(nameof(Gen1Count));
        OnPropertyChanged(nameof(Gen2Count));
        OnPropertyChanged(nameof(RecentDurations));
        OnPropertyChanged(nameof(FpsSummaryText));
        OnPropertyChanged(nameof(FrameTimeSummaryText));
        OnPropertyChanged(nameof(ResponsivenessText));
        OnPropertyChanged(nameof(MemorySummaryText));
        OnPropertyChanged(nameof(FpsStatusTone));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
