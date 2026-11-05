using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using CkanUI.App.Diagnostics;
using CkanUI.App.Theming;
using CkanUI.Services.Abstractions;

namespace CkanUI.App.Controls.Backgrounds;

/// <summary>
/// 程序化背景画布：按 <see cref="Services.Abstractions.BackgroundSceneKinds"/> 选择场景，
/// 统一驱动时间、光标缓动和主题色。默认隐藏，是否露面由 <c>BackgroundHost</c> 决定。
/// </summary>
public sealed class BackgroundSceneCanvas : Control
{
    private const double TickIntervalMs = 33;
    private const double IdleTickIntervalMs = 100;
    private const double LightThemeIntensity = 0.55;
    private const double PointerMoveThreshold = 0.5;

    private static readonly Color DefaultTextColor = Color.Parse("#E8ECF2");
    private static readonly Color DefaultAccentColor = Color.Parse("#72A7FF");

    private static readonly IReadOnlyDictionary<string, IBackgroundSceneRenderer> Renderers =
        new Dictionary<string, IBackgroundSceneRenderer>(StringComparer.Ordinal)
        {
            [BackgroundSceneKinds.Orbital] = new OrbitalBlueprintScene(),
            [BackgroundSceneKinds.Lunar] = new LunarTerminatorScene(),
            [BackgroundSceneKinds.Probe] = new DeepSpaceProbeScene(),
            [BackgroundSceneKinds.Lens] = new GravitationalLensScene(),
            [BackgroundSceneKinds.Corona] = new CoronaScene(),
            [BackgroundSceneKinds.RingPlane] = new RingPlaneScene(),
            [BackgroundSceneKinds.Aurora] = new AuroraOvalScene(),
            [BackgroundSceneKinds.Pulsar] = new PulsarScene(),
            [BackgroundSceneKinds.SolarSail] = new SolarSailScene(),
            [BackgroundSceneKinds.SpaceElevator] = new SpaceElevatorScene()
        };

    private readonly DispatcherTimer? _timer;
    private string _scene = BackgroundSceneKinds.Starfield;
    private double _time;
    private Point? _pointer;
    private Point? _pointerTarget;
    private double _pointerStrength;
    private Point? _lastInputPosition;

    public BackgroundSceneCanvas()
    {
        IsHitTestVisible = false;
        ClipToBounds = true;
        IsVisible = false;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(TickIntervalMs) };
        _timer.Tick += OnTick;
    }

    /// <summary>当前场景键；未知值会归一成星空（此时画布本身不该可见）。</summary>
    public string Scene
    {
        get => _scene;
        set
        {
            var normalized = BackgroundSceneKinds.Normalize(value);
            if (string.Equals(_scene, normalized, StringComparison.Ordinal))
            {
                return;
            }

            _scene = normalized;
            InvalidateVisual();
        }
    }

    public bool IsMotionEnabled =>
        !this.TryFindResource(MotionHost.BaseKey, out var value)
        || value is not TimeSpan duration
        || duration > TimeSpan.Zero;

    /// <summary>把窗口坐标交给场景。传 <c>null</c> 表示光标离开，影响会缓动消失。</summary>
    public void TrackPointer(Point? position)
    {
        if (position is { } point)
        {
            if (_lastInputPosition is { } last
                && Math.Abs(last.X - point.X) < PointerMoveThreshold
                && Math.Abs(last.Y - point.Y) < PointerMoveThreshold)
            {
                return;
            }

            _lastInputPosition = point;
            _pointerTarget = point;
            if (!IsMotionEnabled)
            {
                _pointer = point;
                _pointerStrength = 1;
                InvalidateVisual();
                return;
            }
        }
        else
        {
            if (_lastInputPosition is null && _pointerTarget is null)
            {
                return;
            }

            _lastInputPosition = null;
            _pointerTarget = null;
            if (!IsMotionEnabled)
            {
                _pointer = null;
                _pointerStrength = 0;
                InvalidateVisual();
                return;
            }
        }

        if (_timer is not { IsEnabled: true })
        {
            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var size = Bounds.Size;
        if (size.Width < 8 || size.Height < 8)
        {
            return;
        }

        if (!Renderers.TryGetValue(_scene, out var renderer))
        {
            return;
        }

        var intensity = ActualThemeVariant == ThemeVariant.Light ? LightThemeIntensity : 1d;
        var scene = new BackgroundSceneContext(
            size,
            _time,
            intensity,
            _pointer,
            _pointerStrength,
            IsMotionEnabled,
            ResolveColor("CkanTextPrimaryBrush", DefaultTextColor),
            ResolveColor("CkanAccentBrush", DefaultAccentColor));

        using (context.PushClip(new Rect(size)))
        {
            renderer.Render(context, scene);
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        DynamicRefreshRateController.Instance.IdleStateChanged -= OnIdleStateChanged;
        DynamicRefreshRateController.Instance.IdleStateChanged += OnIdleStateChanged;
        UpdateTimerState();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        DynamicRefreshRateController.Instance.IdleStateChanged -= OnIdleStateChanged;
        base.OnDetachedFromVisualTree(e);
        UpdateTimerState();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        ArgumentNullException.ThrowIfNull(change);
        base.OnPropertyChanged(change);

        if (change.Property == IsVisibleProperty)
        {
            UpdateTimerState();
        }
    }

    private void UpdateTimerState()
    {
        if (_timer is null)
        {
            return;
        }

        _timer.IsEnabled = VisualRoot is not null && IsVisible;
        ApplyTickInterval();
    }

    private void ApplyTickInterval()
    {
        if (_timer is null)
        {
            return;
        }

        var interval = DynamicRefreshRateController.Instance.IsIdle ? IdleTickIntervalMs : TickIntervalMs;
        if (Math.Abs(_timer.Interval.TotalMilliseconds - interval) > 0.5)
        {
            _timer.Interval = TimeSpan.FromMilliseconds(interval);
        }
    }

    private void OnIdleStateChanged(bool isIdle) => ApplyTickInterval();

    private void OnTick(object? sender, EventArgs e)
    {
        if (!IsMotionEnabled || TopLevel.GetTopLevel(this) is Window { IsActive: false })
        {
            return;
        }

        var dt = (_timer?.Interval.TotalMilliseconds ?? TickIntervalMs) / 1000d;
        _time += dt;
        AdvancePointer(dt);
        InvalidateVisual();
    }

    private void AdvancePointer(double dt)
    {
        const double follow = 10.0;
        const double fadeIn = 5.0;
        const double fadeOut = 4.0;

        if (_pointerTarget is { } target)
        {
            var ease = 1.0 - Math.Exp(-dt * follow);
            _pointer = _pointer is { } current
                ? current + ((target - current) * ease)
                : target;
            _pointerStrength = Math.Min(1.0, _pointerStrength + (dt * fadeIn));
            return;
        }

        _pointerStrength = Math.Max(0.0, _pointerStrength - (dt * fadeOut));
        if (_pointerStrength <= 0.001)
        {
            _pointer = null;
            _pointerStrength = 0;
        }
    }

    private Color ResolveColor(string key, Color fallback)
    {
        if (!this.TryFindResource(key, out var value))
        {
            return fallback;
        }

        return value switch
        {
            ISolidColorBrush brush => brush.Color,
            Color color => color,
            _ => fallback
        };
    }
}
