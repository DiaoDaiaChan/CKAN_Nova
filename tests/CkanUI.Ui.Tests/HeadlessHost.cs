using Avalonia;
using Avalonia.Headless;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;

namespace CkanUI.Ui.Tests;

/// <summary>
/// 无头 Avalonia 宿主：图标的路径数据要靠渲染平台解析（<c>Geometry.Parse</c>），
/// 单纯 new 控件是过不去的，所以这里起一个最小应用壳。
/// </summary>
internal static class HeadlessHost
{
    private static readonly object Gate = new();
    private static bool _initialized;

    /// <summary>当前线程能不能建控件/窗口（无头壳必须建在本线程上）。</summary>
    public static bool CanCreateControls { get; private set; }

    /// <summary>按需初始化（重复调用安全）。</summary>
    public static void EnsureInitialized()
    {
        lock (Gate)
        {
            if (_initialized)
            {
                return;
            }

            // TestRunner 一个进程里依次跑全部测试程序集：前面的程序集可能已经建过壳，
            // 再调 SetupWithoutStarting 会抛「Setup was already called on one of AppBuilder instances」。
            var created = false;
            if (Application.Current is null)
            {
                AppBuilder.Configure<HeadlessApplication>()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions())
                    .SetupWithoutStarting();
                created = true;
            }

            // 只给自己建的壳补主题：复用的宿主应用已有全套样式，往它的 Styles 里加会撞 UI 线程断言。
            if (created && !Application.Current!.Styles.OfType<FluentTheme>().Any())
            {
                Application.Current.Styles.Add(new FluentTheme());
            }

            CanCreateControls = created || Dispatcher.UIThread.CheckAccess();
            _initialized = true;
        }
    }

    private sealed class HeadlessApplication : Application
    {
    }
}
