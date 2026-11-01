using System;
using System.Collections.Generic;

namespace CkanUI.App.Controls;

/// <summary>
/// 轨道特征节点类型：Ap (远拱点), Pe (近拱点), An (升交点), Dn (降交点)。
/// </summary>
public enum OrbitalNodeType
{
    Ap,
    Pe,
    An,
    Dn
}

/// <summary>
/// 轨道节点标记运行时数据，包含出生时间与弹出动画进度。
/// </summary>
public sealed class OrbitalNodeMarker
{
    public double X { get; }
    public double Y { get; }
    public OrbitalNodeType Type { get; }

    /// <summary>生成以来的累积时长（秒）。</summary>
    public double AgeSeconds { get; private set; }

    public OrbitalNodeMarker(double x, double y, OrbitalNodeType type)
    {
        X = x;
        Y = y;
        Type = type;
        AgeSeconds = 0d;
    }

    public void Advance(double deltaSeconds)
    {
        AgeSeconds += Math.Max(0d, deltaSeconds);
    }

    /// <summary>
    /// 弹出动画进度 (0..1+)。
    /// 0.22 秒内完成弹性弹出（Overshoot 达到 ~1.15 后回弹稳定到 1.0）。
    /// </summary>
    public double PopScale
    {
        get
        {
            const double duration = 0.22d;
            if (AgeSeconds >= duration)
            {
                return 1.0d;
            }

            var p = AgeSeconds / duration;
            // 弹性回弹曲线: overshoot = 1.2
            var s = 1.70158d;
            p -= 1d;
            return Math.Max(0d, p * p * ((s + 1d) * p + s) + 1d);
        }
    }

    /// <summary>弹出动画的 Alpha (0..1)。</summary>
    public double PopAlpha => Math.Clamp(AgeSeconds / 0.10d, 0d, 1d);
}

/// <summary>
/// 按住左键拖动时留下的细线轨迹。
/// 采样画线轨迹，并在划线时在轨迹上依次随机弹出 4 个 KSP 经典轨道特征节点（Ap, Pe, An, Dn）。
/// 每次按住拖拽交互中，这 4 个节点至多各出现一次。
/// 抬手后整条线及节点在指定时间内平滑淡出。
/// </summary>
public sealed class DragTrailModel
{
    public const int Capacity = 512;
    public const double MinSampleDistance = 1.2d;
    public const double FadeSeconds = 0.45d;
    public const double MaxStepSeconds = 0.25d;

    public const double MinNodeDistance = 90.0d;
    public const double MaxNodeDistance = 160.0d;
    public const int MaxNodes = 4;

    private readonly double[] _x = new double[Capacity];
    private readonly double[] _y = new double[Capacity];
    private int _head;
    private int _count;
    private double _fade = 1d;

    private readonly List<OrbitalNodeMarker> _nodes = new(MaxNodes);
    private readonly List<OrbitalNodeType> _remainingNodePool = new(MaxNodes);

    private double _distanceSinceLastNode;
    private double _nextSpawnThreshold = 100.0d;
    private readonly Random _random;

    public DragTrailModel(int? seed = null)
    {
        _random = seed.HasValue ? new Random(seed.Value) : new Random();
        ResetPoolAndThreshold();
    }

    public bool IsDragging { get; private set; }
    public int Count => _count;
    public double Intensity => IsDragging ? 1d : Math.Clamp(_fade, 0d, 1d);
    public bool IsActive => _count > 0 && Intensity > 0d;
    public IReadOnlyList<OrbitalNodeMarker> Nodes => _nodes;

    /// <summary>是否有正在进行的动效（节点弹性弹出或整线淡出）。</summary>
    public bool HasActiveAnimations
    {
        get
        {
            if (!IsDragging)
            {
                return IsActive;
            }

            for (var i = 0; i < _nodes.Count; i++)
            {
                if (_nodes[i].AgeSeconds < 0.25d)
                {
                    return true;
                }
            }

            return false;
        }
    }

    public void Begin(double x, double y)
    {
        Clear();
        if (!double.IsFinite(x) || !double.IsFinite(y))
        {
            return;
        }

        IsDragging = true;
        _fade = 1d;
        _distanceSinceLastNode = 0d;
        ResetPoolAndThreshold();
        Add(x, y);
    }

    public void Move(double x, double y)
    {
        if (!IsDragging || !double.IsFinite(x) || !double.IsFinite(y) || _count == 0)
        {
            return;
        }

        var lastX = _x[Index(_count - 1)];
        var lastY = _y[Index(_count - 1)];
        var dx = x - lastX;
        var dy = y - lastY;
        var distSq = dx * dx + dy * dy;
        if (distSq < MinSampleDistance * MinSampleDistance)
        {
            return;
        }

        var stepDist = Math.Sqrt(distSq);
        _distanceSinceLastNode += stepDist;

        if (_distanceSinceLastNode >= _nextSpawnThreshold && _remainingNodePool.Count > 0)
        {
            SpawnNode(x, y);
            _distanceSinceLastNode = 0d;
            ResetNextSpawnThreshold();
        }

        Add(x, y);
    }

    public void End()
    {
        if (!IsDragging)
        {
            return;
        }

        IsDragging = false;
        _fade = 1d;
    }

    public void Clear()
    {
        _count = 0;
        _head = 0;
        _fade = 1d;
        _distanceSinceLastNode = 0d;
        _nodes.Clear();
        _remainingNodePool.Clear();
        IsDragging = false;
    }

    public void Advance(double deltaSeconds)
    {
        if (!double.IsFinite(deltaSeconds) || deltaSeconds <= 0d)
        {
            return;
        }

        var clampedDelta = Math.Min(deltaSeconds, MaxStepSeconds);

        // 推进所有已有节点的弹出动画计时
        for (var i = 0; i < _nodes.Count; i++)
        {
            _nodes[i].Advance(clampedDelta);
        }

        if (IsDragging)
        {
            return;
        }

        _fade -= clampedDelta / FadeSeconds;
        if (_fade <= 0d)
        {
            Clear();
        }
    }

    public double PointX(int index) => _x[Index(index)];
    public double PointY(int index) => _y[Index(index)];

    private void Add(double x, double y)
    {
        _head = (_head + 1) % Capacity;
        if (_count < Capacity)
        {
            _count++;
        }

        _x[_head] = x;
        _y[_head] = y;
    }

    private int Index(int index)
    {
        var start = (_head - _count + 1 + Capacity) % Capacity;
        return (start + index) % Capacity;
    }

    private void SpawnNode(double x, double y)
    {
        if (_remainingNodePool.Count == 0)
        {
            return;
        }

        // 随机从剩余节点池中抽取一个（保证每个类型至多出现一次）
        var poolIndex = _random.Next(0, _remainingNodePool.Count);
        var type = _remainingNodePool[poolIndex];
        _remainingNodePool.RemoveAt(poolIndex);

        _nodes.Add(new OrbitalNodeMarker(x, y, type));
    }

    private void ResetPoolAndThreshold()
    {
        _remainingNodePool.Clear();
        _remainingNodePool.Add(OrbitalNodeType.Ap);
        _remainingNodePool.Add(OrbitalNodeType.Pe);
        _remainingNodePool.Add(OrbitalNodeType.An);
        _remainingNodePool.Add(OrbitalNodeType.Dn);

        ResetNextSpawnThreshold();
    }

    private void ResetNextSpawnThreshold()
    {
        _nextSpawnThreshold = MinNodeDistance + _random.NextDouble() * (MaxNodeDistance - MinNodeDistance);
    }
}
