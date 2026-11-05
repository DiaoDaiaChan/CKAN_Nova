using Avalonia.Threading;

using CkanUI.Services.Abstractions.Input;
using CkanUI.Services.Input;

namespace CkanUI.App.Input;

/// <summary>手柄轮询宿主：把 <see cref="IGamepadSource"/> 的原始状态喂给状态机，再把动作派发回 UI 线程。</summary>
/// <remarks>
/// <para>用 <see cref="DispatcherTimer"/> 而不是定时线程：手柄动作最终要动焦点、切页签、开面板，
/// 全都在 UI 线程上做最省事，也避免再往 <c>CkanUI.App</c> 引入一层线程封送。</para>
/// <para>轮询频率 30 Hz（约 33 ms）：人手最快点击约 10–12 次/秒，30 Hz 采样不会丢按下沿；
/// 再高只是白烧 CPU——手柄空闲时也有 30 次/秒的系统调用。</para>
/// </remarks>
public sealed class GamepadInputHost : IDisposable
{
    /// <summary>轮询间隔（毫秒）。</summary>
    private const int PollIntervalMs = 33;

    private readonly IGamepadSource _source;
    private readonly Action<GamepadAction> _dispatch;
    private readonly GamepadNavigator _navigator;
    private readonly DispatcherTimer _timer;
    private readonly Func<long> _clock;

    private bool _disposed;

    /// <summary>构造轮询宿主；<paramref name="clock"/> 只在测试里替换，生产走 <see cref="Environment.TickCount64"/>。</summary>
    public GamepadInputHost(IGamepadSource source, Action<GamepadAction> dispatch, Func<long>? clock = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(dispatch);

        _source = source;
        _dispatch = dispatch;
        _clock = clock ?? (() => Environment.TickCount64);
        _navigator = new GamepadNavigator();
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(PollIntervalMs), DispatcherPriority.Input, OnTick);
    }

    /// <summary>最近一次轮询时手柄是否在线。</summary>
    public bool IsConnected { get; private set; }

    /// <summary>开始轮询；本机没有 XInput 时直接返回，不创建任何后台负担。</summary>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_source.IsAvailable)
        {
            return;
        }

        _timer.Start();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer.Stop();
        (_source as IDisposable)?.Dispose();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        var snapshot = _source.Poll();
        if (!snapshot.IsConnected)
        {
            IsConnected = false;
            // 断开必须清状态：否则手柄拔了再插，按住的那个键会被当成「没按过」，或者方向连发卡在半路。
            _navigator.Reset();
            return;
        }

        IsConnected = true;
        var actions = _navigator.Update(snapshot, _clock());
        for (var i = 0; i < actions.Count; i++)
        {
            _dispatch(actions[i]);
        }
    }
}
