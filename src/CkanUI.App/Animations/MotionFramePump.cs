using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

using Avalonia.Controls;

namespace CkanUI.App.Animations;

/// <summary>
/// 按显示器节拍推进弹簧的帧泵：每个 <see cref="TopLevel"/> 一台，空闲时自动停。
/// </summary>
/// <remarks>
/// <para>为什么不用定时器：60 / 120 / 144Hz 的帧间隔差一倍以上，固定 16ms 的定时器在 144Hz 上
/// 每两帧才动一次（看见的是"顿"），在 60Hz 上又和 VSync 错半帧（看见的是"抖"）。
/// <see cref="TopLevel.RequestAnimationFrame"/> 挂在合成器的 VSync 上，帧间隔由回调时间戳实测给出。</para>
/// <para>泵只负责"什么时候算"，算多少由 <see cref="TransformSpring.Advance"/> 自己决定；
/// 没有活跃弹簧就停止排帧，空闲期不占 CPU。</para>
/// </remarks>
internal sealed class MotionFramePump
{
    private static readonly ConditionalWeakTable<TopLevel, MotionFramePump> Pumps = new();

    private readonly TopLevel _topLevel;
    private readonly List<IMotionTicker> _tracks = [];
    private bool _frameRequested;
    private TimeSpan _lastTimestamp;

    private MotionFramePump(TopLevel topLevel) => _topLevel = topLevel;

    /// <summary>把一根弹簧挂到帧泵上（重复挂同一根是幂等的）。</summary>
    /// <param name="topLevel">所属窗口。</param>
    /// <param name="track">要推进的弹簧。</param>
    internal static void Subscribe(TopLevel topLevel, IMotionTicker track)
    {
        var pump = Pumps.GetValue(topLevel, static top => new MotionFramePump(top));
        if (!pump._tracks.Contains(track))
        {
            pump._tracks.Add(track);
        }

        pump.RequestFrame();
    }

    /// <summary>把一根弹簧从帧泵上摘掉（弹簧收敛后由它自己调用）。</summary>
    /// <param name="topLevel">所属窗口。</param>
    /// <param name="track">要摘掉的弹簧。</param>
    internal static void Unsubscribe(TopLevel topLevel, IMotionTicker track)
    {
        if (Pumps.TryGetValue(topLevel, out var pump))
        {
            pump._tracks.Remove(track);
        }
    }

    private void RequestFrame()
    {
        if (_frameRequested || _tracks.Count == 0)
        {
            return;
        }

        _frameRequested = true;
        _lastTimestamp = TimeSpan.Zero;
        _topLevel.RequestAnimationFrame(OnFrame);
    }

    private void OnFrame(TimeSpan timestamp)
    {
        _frameRequested = false;
        if (_tracks.Count == 0)
        {
            return;
        }

        // 首次回调没有上一帧可减，按 60Hz 起步；掉帧时封顶 1/30s，
        // 否则一次 200ms 的停顿会把弹簧按真实时间推过去，看起来像瞬移。
        var delta = _lastTimestamp == TimeSpan.Zero ? 1.0 / 60.0 : (timestamp - _lastTimestamp).TotalSeconds;
        _lastTimestamp = timestamp;
        if (!(delta > 0))
        {
            delta = 1.0 / 60.0;
        }

        delta = Math.Min(delta, 1.0 / 30.0);

        for (var i = _tracks.Count - 1; i >= 0; i--)
        {
            try
            {
                if (!_tracks[i].Advance(delta))
                {
                    _tracks.RemoveAt(i);
                }
            }
            catch (Exception)
            {
                // 一根弹簧炸了不能连累同一窗口的其它动画：摘掉它，继续排帧。
                _tracks.RemoveAt(i);
            }
        }

        RequestFrame();
    }
}
