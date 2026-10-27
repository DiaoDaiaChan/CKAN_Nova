using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using CkanUI.Services.Abstractions;
using CkanUI.ViewModels.Pages;
using CkanUI.ViewModels.Pages.Instances;

namespace CkanUI.App.Views;

/// <summary>
/// 实例页视图（03 篇 §5 V-03 / 06 篇 UI-04·UI-05）：只做绑定同步、快捷键与平台能力接线。
/// </summary>
/// <remarks>
/// <para>业务全部在 <see cref="InstancesPageViewModel"/> 里（04 篇 §1：视图不写业务逻辑）；
/// 这里承担两件视图专属的事：把 <see cref="ListBox"/> 的选中项与 VM 的 <c>SelectedRow</c> 双向同步，
/// 以及把依赖平台 API 的宿主能力（目录选择 / 路径探测 / 打开目录 / 启动游戏 / Steam 库扫描）注入 VM。</para>
/// <para>宿主实现放在本文件里，是因为它们都要碰 Avalonia 与文件系统（04 篇 §1 架构铁律：
/// <c>Avalonia.*</c> 只允许出现在 App 层），而 VM 侧只认纯接口。</para>
/// </remarks>
public partial class InstancesPageView : UserControl
{
    private InstancesPageViewModel? _viewModel;

    /// <summary>构造视图并接线（视图由 ViewLocator 无参构造，因此这里不能有参数）。</summary>
    public InstancesPageView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private InstancesPageViewModel? ViewModel => DataContext as InstancesPageViewModel;

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = DataContext as InstancesPageViewModel;
        if (_viewModel is null)
        {
            return;
        }

        // 宿主能力注入：这些实现都要碰平台 API / 文件系统，VM 侧不能引用（04 篇 §1）。
        // 用 ??= 是为了让宿主（测试夹具 / 未来 DI 注册）已经塞好的实现优先。
        _viewModel.FolderPicker ??= new AvaloniaInstanceFolderPicker(this);
        _viewModel.PathProbe ??= new FileSystemPathProbe();
        _viewModel.ShellLauncher ??= new ShellInstanceLauncher();
        _viewModel.SteamLibraries ??= new SteamLibrarySource(new FileSystemPathProbe());

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        SyncSelection();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(InstancesPageViewModel.SelectedRow))
        {
            SyncSelection();
        }
    }

    /// <summary>把 VM 的选中行推回列表控件（服务事件引起的整体替换后需要它恢复高亮）。</summary>
    private void SyncSelection()
    {
        if (_viewModel is null || InstancesList is null)
        {
            return;
        }

        if (!ReferenceEquals(InstancesList.SelectedItem, _viewModel.SelectedRow))
        {
            InstancesList.SelectedItem = _viewModel.SelectedRow;
        }
    }

    private void OnRowSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_viewModel is null || sender is not ListBox list)
        {
            return;
        }

        _viewModel.SelectedRow = list.SelectedItem as InstanceRowViewModel;
    }

    /// <summary>页面级快捷键（03 篇 §9）：F5 重新扫描实例，Esc 关闭表单/错误条。</summary>
    private void OnRootKeyDown(object? sender, KeyEventArgs e)
    {
        if (_viewModel is not { } vm)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.F5:
                if (vm.RefreshCommand.CanExecute(null))
                {
                    vm.RefreshCommand.Execute(null);
                }

                e.Handled = true;
                break;

            case Key.Escape:
                if (vm.HasForm)
                {
                    vm.CloseForm();
                }
                else if (vm.HasError)
                {
                    vm.ClearErrorCommand.Execute(null);
                }

                e.Handled = true;
                break;

            default:
                break;
        }
    }
}

/// <summary>目录选择器实现（Avalonia <c>StorageProvider</c>）：03 篇 §8 D-06「路径校验」。</summary>
internal sealed class AvaloniaInstanceFolderPicker : IInstanceFolderPicker
{
    private readonly Control _owner;

    public AvaloniaInstanceFolderPicker(Control owner)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
    }

    /// <inheritdoc />
    public async Task<string?> PickFolderAsync(string title, string? startPath = null)
    {
        // 视图可能还没挂到可视化树上（例如首次 Activate 前调用），因此每次现取 TopLevel。
        var storage = TopLevel.GetTopLevel(_owner)?.StorageProvider;
        if (storage is null || !storage.CanPickFolder)
        {
            return null;
        }

        var start = await ResolveStartFolderAsync(storage, startPath).ConfigureAwait(true);
        var folders = await storage
            .OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = title,
                AllowMultiple = false,
                SuggestedStartLocation = start,
            })
            .ConfigureAwait(true);

        return folders.Count > 0 ? folders[0].Path.LocalPath : null;
    }

    private static async Task<IStorageFolder?> ResolveStartFolderAsync(IStorageProvider storage, string? startPath)
    {
        if (string.IsNullOrWhiteSpace(startPath) || !Path.IsPathRooted(startPath))
        {
            return null;
        }

        try
        {
            return await storage.TryGetFolderFromPathAsync(new Uri(startPath)).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is ArgumentException or UriFormatException or NotSupportedException)
        {
            // 起始目录不可解析时退回系统默认位置，不影响用户继续选目录。
            return null;
        }
    }
}

/// <summary>路径探测器实现：VM 不允许调用 <c>Directory.Exists</c>（04 篇 §1）。</summary>
internal sealed class FileSystemPathProbe : IInstancePathProbe
{
    /// <inheritdoc />
    public bool DirectoryExists(string path) => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path);
}

/// <summary>
/// 外壳动作实现：打开实例目录 / 启动游戏。
/// </summary>
/// <remarks>
/// 启动游戏只是「把 exe 拉起来」：完整的启动流程（DisposeAll → 启动 → 精简模式，18 篇 E6）
/// 还需要资源释放状态机，等 <c>GameLaunchViewModel</c> 就绪后替换本实现。
/// </remarks>
internal sealed class ShellInstanceLauncher : IInstanceShellLauncher
{
    private const string PrimaryExecutable = "KSP_x64.exe";

    private const string FallbackExecutable = "KSP.exe";

    /// <inheritdoc />
    public Task<OperationResult> OpenFolderAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return Task.FromResult(OperationResult.Fail("游戏目录为空，无法打开。"));
        }

        if (!Directory.Exists(path))
        {
            return Task.FromResult(OperationResult.Fail($"目录不存在：{path}（不可重试）"));
        }

        return Start(new ProcessStartInfo(path) { UseShellExecute = true }, "打开目录失败");
    }

    /// <inheritdoc />
    public Task<OperationResult> LaunchGameAsync(string gameDir)
    {
        if (string.IsNullOrWhiteSpace(gameDir) || !Directory.Exists(gameDir))
        {
            return Task.FromResult(OperationResult.Fail($"目录不存在：{gameDir}（不可重试）"));
        }

        // TODO(18 篇 / E6)：此处应改为「释放注册表锁 → 启动进程 → 进入精简模式」，由 GameLaunchViewModel 接管。
        var executable = FindExecutable(gameDir);
        if (executable is null)
        {
            return Task.FromResult(
                OperationResult.Fail($"未在该目录找到 {PrimaryExecutable} 或 {FallbackExecutable}，请确认这是 KSP 安装目录。"));
        }

        return Start(
            new ProcessStartInfo(executable) { UseShellExecute = true, WorkingDirectory = gameDir },
            "启动游戏失败");
    }

    private static string? FindExecutable(string gameDir)
    {
        var primary = Path.Combine(gameDir, PrimaryExecutable);
        if (File.Exists(primary))
        {
            return primary;
        }

        var fallback = Path.Combine(gameDir, FallbackExecutable);
        return File.Exists(fallback) ? fallback : null;
    }

    private static Task<OperationResult> Start(ProcessStartInfo startInfo, string failureMessage)
    {
        try
        {
            var process = Process.Start(startInfo);
            if (process is null)
            {
                return Task.FromResult(OperationResult.Fail(failureMessage));
            }

            // 外壳启动的进程句柄用完即弃，不影响子进程继续运行。
            process.Dispose();
            return Task.FromResult(OperationResult.Ok());
        }
        catch (Exception ex) when (ex is InvalidOperationException
                                       or System.ComponentModel.Win32Exception
                                       or PlatformNotSupportedException
                                       or FileNotFoundException)
        {
            return Task.FromResult(OperationResult.Fail(failureMessage, ex.Message));
        }
    }
}

/// <summary>
/// Steam 库探测实现（03 篇 §5「自动查找」/ 06 篇 UI-32）。
/// </summary>
/// <remarks>
/// 只读 <c>steamapps/libraryfolders.vdf</c>，不写任何文件；VDF 解析复用
/// <see cref="SteamLibraryParser"/>（纯函数，已被单测覆盖），本类只负责 IO 与存在性判断。
/// 未读注册表：契约尚未包含、且注册表访问属于平台 API，等 Core/服务层提供探测后再替换。
/// </remarks>
internal sealed class SteamLibrarySource : ISteamLibrarySource
{
    private readonly IInstancePathProbe _probe;

    public SteamLibrarySource(IInstancePathProbe probe)
    {
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
    }

    /// <inheritdoc />
    public Task<OperationResult<IReadOnlyList<string>>> DiscoverGameDirsAsync(CancellationToken ct = default)
    {
        try
        {
            var found = new List<string>();
            foreach (var libraryFile in EnumerateLibraryFiles())
            {
                ct.ThrowIfCancellationRequested();
                if (!File.Exists(libraryFile))
                {
                    continue;
                }

                var content = File.ReadAllText(libraryFile);
                foreach (var candidate in SteamLibraryParser.CandidateGameDirs(SteamLibraryParser.ParseLibraryRoots(content)))
                {
                    if (_probe.DirectoryExists(candidate) && !found.Contains(candidate, StringComparer.OrdinalIgnoreCase))
                    {
                        found.Add(candidate);
                    }
                }
            }

            return Task.FromResult(OperationResult<IReadOnlyList<string>>.Ok(found));
        }
        catch (OperationCanceledException)
        {
            return Task.FromResult(OperationResult<IReadOnlyList<string>>.Fail("扫描 Steam 库已取消。"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return Task.FromResult(OperationResult<IReadOnlyList<string>>.Fail("扫描 Steam 库失败。", ex.Message));
        }
    }

    /// <summary>枚举可能的 <c>libraryfolders.vdf</c> 位置（Steam 默认安装在 Program Files 两个变体下）。</summary>
    private static IEnumerable<string> EnumerateLibraryFiles()
    {
        var steamRoots = new List<string>();

        var programFilesX86 = Environment.GetEnvironmentVariable("ProgramFiles(x86)");
        if (!string.IsNullOrWhiteSpace(programFilesX86))
        {
            steamRoots.Add(Path.Combine(programFilesX86, "Steam"));
        }

        var programFiles = Environment.GetEnvironmentVariable("ProgramFiles");
        if (!string.IsNullOrWhiteSpace(programFiles))
        {
            steamRoots.Add(Path.Combine(programFiles, "Steam"));
        }

        foreach (var root in steamRoots)
        {
            yield return Path.Combine(root, "steamapps", "libraryfolders.vdf");
        }
    }
}
