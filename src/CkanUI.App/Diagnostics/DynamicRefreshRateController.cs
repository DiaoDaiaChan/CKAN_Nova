using System;
using System.ComponentModel;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace CkanUI.App.Diagnostics;

/// <summary>
/// 动态刷新率与空闲降频控制器：监听用户操作，在静止/无操作时将渲染与动画下调至低功耗帧率，操作触发时毫秒级拉满恢复。
/// </summary>
public sealed class DynamicRefreshRateController : INotifyPropertyChanged
{
    private static readonly Lazy<DynamicRefreshRateController> LazyInstance =
        new(() => new DynamicRefreshRateController());

    public static DynamicRefreshRateController Instance => LazyInstance.Value;

    private readonly DispatcherTimer _inactivityTimer;
    private readonly Stopwatch _activityStopwatch = new();
    private TopLevel? _attachedTopLevel;
    private Avalonia.Point _lastPointerPosition;
    private bool _hasLastPointerPosition;
    private bool _isIdle;
    private TimeSpan _idleTimeout = TimeSpan.FromSeconds(2.5);
    private int _activeTargetFps = 60;
    private int _idleTargetFps = 10;

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action<bool>? IdleStateChanged;

    public DynamicRefreshRateController()
    {
        _inactivityTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background, OnInactivityCheck);
        _activityStopwatch.Start();
    }

    /// <summary>当前是否处于空闲降频状态。</summary>
    public bool IsIdle
    {
        get => _isIdle;
        private set
        {
            if (_isIdle == value) return;
            _isIdle = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsIdle)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentTargetFps)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusDescription)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusTone)));
            IdleStateChanged?.Invoke(_isIdle);
        }
    }

    /// <summary>从最后一次用户操作到进入空闲状态的超时阈值（默认 2.5 秒）。</summary>
    public TimeSpan IdleTimeout
    {
        get => _idleTimeout;
        set
        {
            if (_idleTimeout == value) return;
            _idleTimeout = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IdleTimeout)));
        }
    }

    /// <summary>活跃状态下的目标基准刷新率（默认 60 FPS）。</summary>
    public int ActiveTargetFps
    {
        get => _activeTargetFps;
        set
        {
            if (_activeTargetFps == value) return;
            _activeTargetFps = Math.Max(15, value);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ActiveTargetFps)));
            if (!IsIdle) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentTargetFps)));
        }
    }

    /// <summary>空闲无操作状态下的降频目标刷新率（默认 10 FPS）。</summary>
    public int IdleTargetFps
    {
        get => _idleTargetFps;
        set
        {
            if (_idleTargetFps == value) return;
            _idleTargetFps = Math.Clamp(value, 1, 30);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IdleTargetFps)));
            if (IsIdle) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentTargetFps)));
        }
    }

    /// <summary>当前生效的目标刷新率。</summary>
    public int CurrentTargetFps => _isIdle ? _idleTargetFps : _activeTargetFps;

    /// <summary>当前状态文字描述。</summary>
    public string StatusDescription => _isIdle ? $"空闲降频 ({_idleTargetFps} FPS)" : $"全速响应 ({_activeTargetFps} FPS)";

    /// <summary>状态主题颜色标识。</summary>
    public string StatusTone => _isIdle ? "Subtle" : "Good";

    /// <summary>挂载到顶层窗口以监听全局输入事件。</summary>
    public void Attach(TopLevel topLevel)
    {
        if (_attachedTopLevel == topLevel) return;
        Detach();

        _attachedTopLevel = topLevel;
        _attachedTopLevel.AddHandler(InputElement.PointerMovedEvent, OnUserInteraction, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        _attachedTopLevel.AddHandler(InputElement.PointerPressedEvent, OnUserInteraction, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        _attachedTopLevel.AddHandler(InputElement.PointerReleasedEvent, OnUserInteraction, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        _attachedTopLevel.AddHandler(InputElement.PointerWheelChangedEvent, OnUserInteraction, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        _attachedTopLevel.AddHandler(InputElement.KeyDownEvent, OnUserInteraction, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        _attachedTopLevel.AddHandler(InputElement.KeyUpEvent, OnUserInteraction, Avalonia.Interactivity.RoutingStrategies.Tunnel);

        if (_attachedTopLevel is Window window)
        {
            window.Activated += OnWindowActivated;
            window.Deactivated += OnWindowDeactivated;
        }

        _hasLastPointerPosition = false;
        _inactivityTimer.Start();
        NotifyUserActivity();
    }

    /// <summary>从当前窗口解绑。</summary>
    public void Detach()
    {
        if (_attachedTopLevel is null) return;

        _attachedTopLevel.RemoveHandler(InputElement.PointerMovedEvent, OnUserInteraction);
        _attachedTopLevel.RemoveHandler(InputElement.PointerPressedEvent, OnUserInteraction);
        _attachedTopLevel.RemoveHandler(InputElement.PointerReleasedEvent, OnUserInteraction);
        _attachedTopLevel.RemoveHandler(InputElement.PointerWheelChangedEvent, OnUserInteraction);
        _attachedTopLevel.RemoveHandler(InputElement.KeyDownEvent, OnUserInteraction);
        _attachedTopLevel.RemoveHandler(InputElement.KeyUpEvent, OnUserInteraction);

        if (_attachedTopLevel is Window window)
        {
            window.Activated -= OnWindowActivated;
            window.Deactivated -= OnWindowDeactivated;
        }

        _inactivityTimer.Stop();
        _attachedTopLevel = null;
    }

    /// <summary>主动通知发生了一次用户交互，立即退出空闲降频状态。</summary>
    public void NotifyUserActivity()
    {
        _activityStopwatch.Restart();
        if (IsIdle)
        {
            IsIdle = false;
        }
    }

    /// <summary>显式强制进入空闲降频状态（如失焦或测试用）。</summary>
    public void ForceIdle()
    {
        if (!IsIdle)
        {
            IsIdle = true;
        }
    }

    private void OnUserInteraction(object? sender, RoutedEventArgs e)
    {
        // 悬停重算（指针位置没变但元素变了）也会发 PointerMoved：
        // 只要光标停着就一路算「用户还在操作」，空闲降频永远进不去，降频等于没做。
        if (e is PointerEventArgs pointer && _attachedTopLevel is { } topLevel)
        {
            var position = pointer.GetPosition(topLevel);
            if (_hasLastPointerPosition
                && Math.Abs(position.X - _lastPointerPosition.X) < 0.5
                && Math.Abs(position.Y - _lastPointerPosition.Y) < 0.5)
            {
                return;
            }

            _lastPointerPosition = position;
            _hasLastPointerPosition = true;
        }

        NotifyUserActivity();
    }

    private void OnWindowActivated(object? sender, EventArgs e)
    {
        NotifyUserActivity();
    }

    private void OnWindowDeactivated(object? sender, EventArgs e)
    {
        // 窗口失焦直接降频
        ForceIdle();
    }

    private void OnInactivityCheck(object? sender, EventArgs e)
    {
        if (_activityStopwatch.Elapsed >= _idleTimeout)
        {
            if (!IsIdle)
            {
                IsIdle = true;
            }
        }
    }
}
