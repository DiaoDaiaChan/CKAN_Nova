using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

using Avalonia.Controls;

using CkanUI.Services.Abstractions;
using CkanUI.ViewModels.Pages.Settings;
using CkanUI.ViewModels.Shell;

namespace CkanUI.App.Tray;

/// <summary>
/// <see cref="ITrayHostActions"/> 在 App 层的实现（12 篇 F-51 / UI-26）：窗口显示/隐藏、三个目录、刷新仓库、退出。
/// </summary>
/// <remarks>
/// <para>
/// 目录与仓库一律走注入的取值委托/服务，本类不拼 <c>%LocalAppData%</c> 也不认 CKAN 命名空间
/// （架构铁律：App 层同样禁止 <c>using CKAN</c>）。日志目录由 <c>SettingsPaths.DefaultConfigDirectory</c>
/// 或日志服务的实际落地目录提供，缓存目录由 <c>ICacheStore.CacheDirectory</c> 提供，实例目录由实例服务提供 —— 接线见 docs/design/tray.md。
/// </para>
/// <para>
/// 托盘回调可能来自系统托盘的托管线程，所有窗口操作经 <see cref="IDispatcher"/> 回到 UI 线程。
/// </para>
/// </remarks>
public sealed class TrayHostActions : ITrayHostActions
{
    private readonly Func<Window?> _window;
    private readonly Func<string?> _logDirectory;
    private readonly Func<string?> _cacheDirectory;
    private readonly Func<string?> _instanceDirectory;
    private readonly IRepositoryService? _repositories;
    private readonly ISettingsService? _settings;
    private readonly Action _exit;
    private readonly IDispatcher _dispatcher;

    /// <summary>构造托盘的宿主动作。</summary>
    /// <param name="window">取主窗口（未创建时返回 null）。</param>
    /// <param name="logDirectory">取日志目录（真源在日志服务侧）。</param>
    /// <param name="cacheDirectory">取下载缓存目录（真源在缓存服务侧）。</param>
    /// <param name="instanceDirectory">取当前实例目录（真源在实例服务侧）。</param>
    /// <param name="repositories">仓库服务；为空时「刷新仓库」返回可读失败。</param>
    /// <param name="exit">退出应用（通常是 <c>IClassicDesktopStyleApplicationLifetime.TryShutdown(0)</c>）。</param>
    /// <param name="dispatcher">UI 线程封送器。</param>
    /// <param name="settings">
    /// 核心设置服务；<paramref name="cacheDirectory"/> 取不到值时用它读
    /// <see cref="SettingsKeyNames.DownloadCacheDir"/> 兜底（缓存目录的真源是 CKAN 配置，不在 UI 设置里）。
    /// </param>
    public TrayHostActions(
        Func<Window?> window,
        Func<string?> logDirectory,
        Func<string?> cacheDirectory,
        Func<string?> instanceDirectory,
        IRepositoryService? repositories,
        Action exit,
        IDispatcher dispatcher,
        ISettingsService? settings = null)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(logDirectory);
        ArgumentNullException.ThrowIfNull(cacheDirectory);
        ArgumentNullException.ThrowIfNull(instanceDirectory);
        ArgumentNullException.ThrowIfNull(exit);
        ArgumentNullException.ThrowIfNull(dispatcher);

        _window = window;
        _logDirectory = logDirectory;
        _cacheDirectory = cacheDirectory;
        _instanceDirectory = instanceDirectory;
        _repositories = repositories;
        _settings = settings;
        _exit = exit;
        _dispatcher = dispatcher;
    }

    /// <inheritdoc />
    public async Task ShowWindow()
    {
        await _dispatcher.InvokeAsync(() =>
        {
            if (_window() is not { } window)
            {
                return;
            }

            if (!window.IsVisible)
            {
                window.Show();
            }

            if (window.WindowState == WindowState.Minimized)
            {
                window.WindowState = WindowState.Normal;
            }

            window.Activate();
        }).ConfigureAwait(true);
    }

    /// <inheritdoc />
    public async Task HideWindow()
    {
        // 用 Hide 而不是 Close：ShutdownMode 是 OnMainWindowClose，
        // Close 会直接退进程（见 docs/design/tray.md「关闭到托盘」）。
        await _dispatcher.InvokeAsync(() => _window()?.Hide()).ConfigureAwait(true);
    }

    /// <inheritdoc />
    public Task<OperationResult> OpenLogFolder() => OpenFolder(_logDirectory(), "日志目录");

    /// <inheritdoc />
    public async Task<OperationResult> OpenCacheFolder()
    {
        var directory = await ResolveCacheDirectoryAsync().ConfigureAwait(true);
        return await OpenFolder(directory, "缓存目录").ConfigureAwait(true);
    }

    /// <inheritdoc />
    public Task<OperationResult> OpenInstanceFolder() => OpenFolder(_instanceDirectory(), "实例目录");

    /// <inheritdoc />
    public async Task<OperationResult> RefreshRepositories()
    {
        if (_repositories is null)
        {
            return OperationResult.Fail("尚未接入仓库服务，无法刷新仓库", retryable: false);
        }

        var result = await _repositories.RefreshAsync().ConfigureAwait(true);
        return OperationResult.From(result);
    }

    /// <inheritdoc />
    public async Task ExitAsync()
    {
        await _dispatcher.InvokeAsync(() =>
        {
            _exit();
            return true;
        }).ConfigureAwait(true);
    }

    /// <summary>
    /// 缓存目录：先问注入的取值委托，拿不到再读核心设置 <see cref="SettingsKeyNames.DownloadCacheDir"/>。
    /// 两条都空就如实报「服务层暂时给不出路径」，不猜 <c>%LocalAppData%</c> 之类的默认位置。
    /// </summary>
    private async Task<string?> ResolveCacheDirectoryAsync()
    {
        if (_cacheDirectory() is { Length: > 0 } direct && !string.IsNullOrWhiteSpace(direct))
        {
            return direct;
        }

        if (_settings is null)
        {
            return null;
        }

        try
        {
            var result = await _settings.GetAsync(SettingsKeyNames.DownloadCacheDir).ConfigureAwait(true);
            return result.IsSuccess ? result.Value : null;
        }
        catch (Exception)
        {
            // 设置服务本身炸了不该让托盘菜单跟着炸：按「路径未知」处理。
            return null;
        }
    }

    private static Task<OperationResult> OpenFolder(string? path, string what)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return Task.FromResult(OperationResult.Fail(what + "还没有确定：服务层暂时给不出路径"));
        }

        var directory = path.Trim();
        if (!Directory.Exists(directory))
        {
            return Task.FromResult(OperationResult.Fail("目录不存在：" + directory, retryable: false));
        }

        try
        {
            // UseShellExecute 让资源管理器接管，本进程不等待子进程退出。
            using var process = Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true });
            return Task.FromResult(OperationResult.Ok());
        }
        catch (Exception ex)
        {
            return Task.FromResult(OperationResult.Fail("打开" + what + "失败", ex.Message));
        }
    }
}