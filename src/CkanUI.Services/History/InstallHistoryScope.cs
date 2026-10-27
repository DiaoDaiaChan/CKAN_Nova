namespace CkanUI.Services.History;

/// <summary>
/// "这次 ApplyAsync 由安装历史服务自己记账"的抑制范围（避免同一次重装落两条历史）。
/// </summary>
/// <remarks>
/// 用 <see cref="AsyncLocal{T}"/> 而不是静态 bool：同一进程里多个实例/多个页面并发跑流水线时，
/// 抑制只对当前异步流生效，不会把别人的历史一并吞掉。
/// </remarks>
internal static class InstallHistoryScope
{
    private static readonly AsyncLocal<int> Depth = new();

    /// <summary>当前是否处于抑制范围内。</summary>
    internal static bool IsSuppressed => Depth.Value > 0;

    /// <summary>进入抑制范围；Dispose 退出。</summary>
    internal static IDisposable Suppress()
    {
        Depth.Value++;
        return new Scope();
    }

    private sealed class Scope : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (Depth.Value > 0)
            {
                Depth.Value--;
            }
        }
    }
}
