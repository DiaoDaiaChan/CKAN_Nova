using Avalonia;
using Avalonia.Headless;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;

namespace CkanUI.Plugin.Rp1.Tests;

/// <summary>
/// 无头 Avalonia 宿主 + 把同步上下文钉到 UI 调度队列的小工具。
/// </summary>
/// <remarks>
/// <para>插件测试不引用宿主 App 程序集，所以这里自带一个最小应用壳，只挂 Fluent 主题（控件模板），
/// 让插件的页面视图能真正装配出可视树。</para>
/// <para>页面读存档走 <c>Task.Run</c> + <c>ConfigureAwait(true)</c>：没有同步上下文时，
/// 续体会落在线程池线程上改 UI 绑定属性。所以建视图模型之前先装一个「一律投递到
/// <see cref="Dispatcher.UIThread"/>」的上下文，再用 <see cref="PumpJobs"/> 在 UI 线程上排空。</para>
/// </remarks>
internal static class UiTestHost
{
    private static readonly object Gate = new();
    private static bool _initialized;

    /// <summary>当前线程能不能建窗口（无头壳必须建在本线程上）。</summary>
    public static bool CanCreateWindows { get; private set; }

    public static void EnsureInitialized()
    {
        lock (Gate)
        {
            if (_initialized)
            {
                return;
            }

            var created = false;
            if (Application.Current is null)
            {
                AppBuilder.Configure<HeadlessApplication>()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions())
                    .SetupWithoutStarting();
                created = true;
            }

            // 只有自己新建的壳才补主题：复用的宿主应用已经带了全套样式，
            // 而在别的线程往它的 Styles 里加东西会触发资源宿主回调，撞上 UI 线程断言。
            if (created && !Application.Current!.Styles.OfType<FluentTheme>().Any())
            {
                Application.Current.Styles.Add(new FluentTheme());
            }

            CanCreateWindows = created || Dispatcher.UIThread.CheckAccess();
            _initialized = true;
        }
    }

    /// <summary>推进并排空当前 UI 调度队列中的所有待处理任务。</summary>
    public static void PumpJobs()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.RunJobs();
        }
    }

    /// <summary>装一个「投递到 UI 调度队列」的同步上下文，用完恢复原样。</summary>
    public static IDisposable UseDispatcherSynchronizationContext() => new DispatcherContextScope();

    /// <summary>一边排空调度队列一边等条件成立；超时返回 false（由用例自己去断言失败信息）。</summary>
    public static bool WaitUntil(Func<bool> condition, int timeoutMs = 5000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            PumpJobs();
            if (condition())
            {
                return true;
            }

            Thread.Sleep(15);
        }

        PumpJobs();
        return condition();
    }

    private sealed class DispatcherContextScope : SynchronizationContext, IDisposable
    {
        private readonly SynchronizationContext? _previous = SynchronizationContext.Current;

        public DispatcherContextScope() => SetSynchronizationContext(this);

        public override void Post(SendOrPostCallback d, object? state)
            => Dispatcher.UIThread.Post(() => d(state));

        public override void Send(SendOrPostCallback d, object? state)
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                d(state);
                return;
            }

            // 不在 UI 线程时退化成投递：Send 同步等待 UI 线程会在「UI 线程正在等本线程」时死锁。
            Dispatcher.UIThread.Post(() => d(state));
        }

        public void Dispose()
        {
            if (ReferenceEquals(SynchronizationContext.Current, this))
            {
                SetSynchronizationContext(_previous);
            }
        }
    }

    private sealed class HeadlessApplication : Application
    {
    }
}
