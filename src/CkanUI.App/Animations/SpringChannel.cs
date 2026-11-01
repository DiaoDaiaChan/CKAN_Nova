using System;

namespace CkanUI.App.Animations;

/// <summary>
/// 单自由度弹簧的数值积分：半隐式欧拉 + 固定子步长，保证 60Hz 与 240Hz 落在同一物理位置。
/// </summary>
public sealed class SpringChannel
{
    private const double MaxSubstep = 1.0 / 960.0;
    private const double MaxFrameSlice = 0.05;
    private const int MaxSubstepsPerAdvance = 96;

    private readonly double _mass;
    private readonly double _stiffness;
    private readonly double _damping;
    private readonly double _restPosition;
    private readonly double _restVelocity;

    private double _value;
    private double _velocity;
    private double _target;
    private double _hold;
    private double _remainder;

    public SpringChannel(double initial, SpringSpec spec, double restPosition = 0.25, double restVelocity = 4)
    {
        _mass = spec.Mass;
        _stiffness = spec.Stiffness;
        _damping = spec.Damping;
        _restPosition = restPosition;
        _restVelocity = restVelocity;
        _value = initial;
        _target = initial;
    }

    public double Value => _value;

    public double Target => _target;

    public double Velocity => _velocity;

    public bool IsHeld => _hold > 0;

    public bool IsMoving => _hold > 0 || !AtRest();

    /// <summary>换目标：只换平衡点，位置与速度原样保留。</summary>
    public void Retarget(double target, double holdSeconds = 0)
    {
        _target = target;
        _hold = holdSeconds > 0 ? holdSeconds : 0;
    }

    /// <summary>叠加一个冲量，使单次自由振荡峰值等于 <paramref name="amplitude"/>。</summary>
    public void Kick(double amplitude)
    {
        if (amplitude != 0)
        {
            _velocity += VelocityForAmplitude(amplitude);
        }
    }

    /// <summary>立刻落位并清空速度与排队。</summary>
    public void SnapTo(double value)
    {
        _value = value;
        _target = value;
        _velocity = 0;
        _hold = 0;
        _remainder = 0;
    }

    /// <summary>推进一帧；返回是否仍需继续。</summary>
    public bool Advance(double seconds)
    {
        if (seconds <= 0)
        {
            return IsMoving;
        }

        var slice = Math.Min(seconds, MaxFrameSlice);

        if (_hold > 0)
        {
            _hold -= slice;
            return true;
        }

        _remainder += slice;
        var substeps = 0;
        while (_remainder >= MaxSubstep && substeps < MaxSubstepsPerAdvance)
        {
            Integrate(MaxSubstep);
            _remainder -= MaxSubstep;
            substeps++;
        }

        if (substeps >= MaxSubstepsPerAdvance)
        {
            _remainder = 0;
        }

        if (!AtRest())
        {
            return true;
        }

        _value = _target;
        _velocity = 0;
        _remainder = 0;
        return false;
    }

    private void Integrate(double dt)
    {
        var acceleration = ((-_stiffness * (_value - _target)) - (_damping * _velocity)) / _mass;
        _velocity += acceleration * dt;
        _value += _velocity * dt;
    }

    private bool AtRest() => Math.Abs(_value - _target) <= _restPosition && Math.Abs(_velocity) <= _restVelocity;

    private double VelocityForAmplitude(double amplitude)
    {
        var omega = Math.Sqrt(_stiffness / _mass);
        var zeta = _damping / (2 * Math.Sqrt(_stiffness * _mass));

        if (zeta >= 1)
        {
            return amplitude * omega * Math.E;
        }

        var damped = omega * Math.Sqrt(1 - (zeta * zeta));
        var peakTime = Math.Atan(damped / (zeta * omega)) / damped;
        var envelope = Math.Exp(-zeta * omega * peakTime) * Math.Sin(damped * peakTime);
        return amplitude * damped / envelope;
    }
}
