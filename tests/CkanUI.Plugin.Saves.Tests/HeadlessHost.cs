using Avalonia;
using Avalonia.Headless;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;

namespace CkanUI.Plugin.Saves.Tests;

/// <summary>
/// 无头 Avalonia 宿主：插件测试不引用宿主 App 程序集，所以这里自带一个最小应用壳，
/// 只挂 Fluent 主题（控件模板），让插件的窗口视图能真正装配出可视树。
/// </summary>
internal static class HeadlessHost
{
    private static readonly object Gate = new();
    private static bool _initialized;

    /// <summary>
    /// 当前线程能不能建窗口（无头壳必须建在本线程上）。
    /// </summary>
    /// <remarks>
    /// TestRunner 在一个进程里依次跑全部测试程序集：排在前面的程序集已经建过无头壳时，
    /// 那个壳属于另一个线程，本线程建窗口会撞 <c>VerifyAccess</c>。
    /// </remarks>
    public static bool CanCreateWindows { get; private set; }

    public static void EnsureInitialized()
    {
        lock (Gate)
        {
            if (_initialized)
            {
                return;
            }

            // TestRunner 一个进程里依次跑全部测试程序集：排在前面的大会已经建过无头壳，
            // 再调一次 SetupWithoutStarting 会抛「Setup was already called on one of AppBuilder instances」。
            // 已存在应用就复用它，只补齐本程序集需要的 Fluent 主题。
            var created = false;
            if (Application.Current is null)
            {
                AppBuilder.Configure<HeadlessApplication>()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions())
                    .SetupWithoutStarting();
                created = true;
            }

            // 只有自己新建的壳才补主题：复用的宿主应用已经带了全套样式，
            // 而在测试线程往它的 Styles 里加东西会触发资源宿主回调，撞上 UI 线程断言
            // （TransitioningContentControl.set_PageTransition →「The calling thread cannot access this object」）。
            if (created && !Application.Current!.Styles.OfType<FluentTheme>().Any())
            {
                Application.Current.Styles.Add(new FluentTheme());
            }

            CanCreateWindows = created || Dispatcher.UIThread.CheckAccess();
            _initialized = true;
        }
    }

    /// <summary>推进并排空当前 UI 调度队列中的所有待处理任务。</summary>
    public static void PumpJobs() => Dispatcher.UIThread.RunJobs();

    private sealed class HeadlessApplication : Application
    {
    }
}
