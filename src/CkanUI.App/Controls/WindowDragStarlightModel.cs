using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;

namespace CkanUI.App.Controls;

/// <summary>
/// 拖动窗口轨迹上的单个星光粒子。
/// </summary>
public sealed class DragStarlightParticle
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Vx { get; set; }
    public double Vy { get; set; }
    public double Radius { get; set; }
    public double Age { get; set; }
    public double MaxLife { get; set; }
    public double Warmth { get; set; } // 0: 冷白, 1: 暖星
    public double TwinklePhase { get; set; }
    public double TwinkleSpeed { get; set; }
    public bool IsSparkleCross { get; set; } // 是否为十字星光芒粒子

    public bool IsAlive => Age < MaxLife;
}

/// <summary>
/// 窗口拖拽星光轨迹模型：管理拖拽过程中的粒子发射、物理飘散、闪烁与衰减。
/// 纯数学模型，不依赖渲染对象，100% 可在无头环境下单测。
/// </summary>
public sealed class WindowDragStarlightModel
{
    public const int MaxParticles = 120;
    private readonly List<DragStarlightParticle> _particles = new(MaxParticles);
    private readonly Random _random;

    private double _lastEmitX;
    private double _lastEmitY;
    private bool _hasLastEmit;
    private bool _isDragging;

    public WindowDragStarlightModel(int? seed = null)
    {
        _random = seed.HasValue ? new Random(seed.Value) : new Random();
    }

    public IReadOnlyList<DragStarlightParticle> Particles => _particles;
    public int ActiveCount => _particles.Count;
    public bool IsDragging => _isDragging;
    public bool IsIdle => !_isDragging && _particles.Count == 0;
    public (double X, double Y) LastPosition => (_lastEmitX, _lastEmitY);

    public void StartDrag(double x, double y)
    {
        _isDragging = true;
        _lastEmitX = x;
        _lastEmitY = y;
        _hasLastEmit = true;
        // 起始点喷射少量微光
        EmitPoint(x, y, count: 3);
    }

    public void UpdateDrag(double x, double y)
    {
        if (!_isDragging)
        {
            StartDrag(x, y);
            return;
        }

        if (!_hasLastEmit)
        {
            _lastEmitX = x;
            _lastEmitY = y;
            _hasLastEmit = true;
            return;
        }

        double dx = x - _lastEmitX;
        double dy = y - _lastEmitY;
        double dist = Math.Sqrt(dx * dx + dy * dy);

        // 沿移动轨迹插值撒星（每 14 像素至少发射 1~2 颗粒子）
        if (dist >= 14.0)
        {
            int steps = Math.Clamp((int)(dist / 14.0), 1, 8);
            for (int i = 1; i <= steps; i++)
            {
                double t = (double)i / steps;
                double px = _lastEmitX + dx * t;
                double py = _lastEmitY + dy * t;
                EmitPoint(px, py, count: 1 + (_random.NextDouble() < 0.35 ? 1 : 0));
            }

            _lastEmitX = x;
            _lastEmitY = y;
        }
    }

    /// <summary>
    /// 按屏幕位移的反向平移所有粒子与最近发射点，使已经生成的星光在屏幕上保持原位。
    /// 窗口向右移动时传入负的 dx，已有粒子留在原屏幕位置，新粒子继续从光标处喷出。
    /// </summary>
    public void Shift(double dx, double dy)
    {
        if (!double.IsFinite(dx) || !double.IsFinite(dy) || (dx == 0.0 && dy == 0.0))
        {
            return;
        }

        for (int i = 0; i < _particles.Count; i++)
        {
            _particles[i].X += dx;
            _particles[i].Y += dy;
        }

        if (_hasLastEmit)
        {
            _lastEmitX += dx;
            _lastEmitY += dy;
        }
    }

    public void EndDrag()
    {
        _isDragging = false;
        _hasLastEmit = false;
    }

    private void EmitPoint(double cx, double cy, int count)
    {
        for (int i = 0; i < count; i++)
        {
            if (_particles.Count >= MaxParticles)
            {
                // 移除最老的一颗
                _particles.RemoveAt(0);
            }

            // 轻微随机发散
            double angle = _random.NextDouble() * Math.PI * 2.0;
            double speed = 10.0 + _random.NextDouble() * 30.0;
            double vx = Math.Cos(angle) * speed;
            double vy = Math.Sin(angle) * speed;

            double radius = 1.2 + _random.NextDouble() * 2.2;
            double life = 0.45 + _random.NextDouble() * 0.45; // 0.45s ~ 0.90s

            _particles.Add(new DragStarlightParticle
            {
                X = cx + (_random.NextDouble() - 0.5) * 8.0,
                Y = cy + (_random.NextDouble() - 0.5) * 8.0,
                Vx = vx,
                Vy = vy,
                Radius = radius,
                Age = 0.0,
                MaxLife = life,
                Warmth = _random.NextDouble(),
                TwinklePhase = _random.NextDouble() * Math.PI * 2.0,
                TwinkleSpeed = 6.0 + _random.NextDouble() * 10.0,
                IsSparkleCross = _random.NextDouble() < 0.28 // 28% 为十字芒星
            });
        }
    }

    public void Advance(double dt)
    {
        if (dt <= 0.0) return;

        const double step = 0.05;
        while (dt > 0.0)
        {
            double curStep = Math.Min(dt, step);
            AdvanceStep(curStep);
            dt -= curStep;
        }
    }

    private void AdvanceStep(double dt)
    {
        for (int i = _particles.Count - 1; i >= 0; i--)
        {
            var p = _particles[i];
            p.Age += dt;
            if (!p.IsAlive)
            {
                _particles.RemoveAt(i);
                continue;
            }

            // 速度微阻尼
            double damping = Math.Max(0.0, 1.0 - dt * 2.5);
            p.Vx *= damping;
            p.Vy *= damping;

            p.X += p.Vx * dt;
            p.Y += p.Vy * dt;
        }
    }

    public void Clear()
    {
        _particles.Clear();
        _isDragging = false;
        _hasLastEmit = false;
    }
}
