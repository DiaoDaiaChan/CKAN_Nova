#pragma warning disable CS0618
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CkanUI.App;
using CkanUI.App.Controls;
using CkanUI.App.Controls.Backgrounds;
using CkanUI.App.Localization;
using CkanUI.App.Theming;
using CkanUI.Localization;
using CkanUI.Services.Abstractions;
using CkanUI.Services;
using CkanUI.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using AppDispatcher = CkanUI.Services.Abstractions.IDispatcher;

namespace CkanUI.Tools.UiSnapshot;

public static class Program
{
    public static void Main(string[] args)
    {
        Console.WriteLine("=== CKAN UI Headless 全页面自主截图生成器 ===");

        var tempDir = Path.Combine(AppContext.BaseDirectory, "headless_temp", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var testConfig = Path.Combine(tempDir, "config.json");
        Environment.SetEnvironmentVariable("CKAN_CONFIG_FILE", testConfig);

        // 空配置下所有涉及实例的页面只能拍到空态（主页的「最近截图」胶片带就永远是空的）。
        // 本机有真实 CKAN 配置就照搬它的实例清单（不写死任何路径），否则种一个仓库内实例兜底。
        var realConfig = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CKAN", "config.json");
        // 真实实例被别的进程锁住（registry.locked）时导航会中断；CKANUI_SNAP_SEED=1 强制走仓库种子实例。
        if (Environment.GetEnvironmentVariable("CKANUI_SNAP_SEED") != "1" && File.Exists(realConfig))
        {
            File.Copy(realConfig, testConfig, overwrite: true);
        }
        else
        {
            var seedInstance = Path.GetFullPath("artifacts/ksp-instance");
            Directory.CreateDirectory(Path.Combine(seedInstance, "GameData"));
            File.WriteAllText(testConfig, JsonSerializer.Serialize(new
            {
                AutoStartInstance = "SnapshotInstance",
                GameInstances = new[] { new { Name = "SnapshotInstance", Path = seedInstance, Game = "KSP" } },
            }));
        }

        AppBuilder.Configure<CkanUI.App.App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions
            {
                UseHeadlessDrawing = true
            })
            .UseSkia()
            .SetupWithoutStarting();

        MainWindow? mainWindow = null;

        // 为快照工具准备临时设置文件，放在项目内部 artifacts/temp，避免系统 Temp / AppData 权限冲突
        var tempSettingsDir = Path.GetFullPath("artifacts/temp/snapshot-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempSettingsDir);
        var snapshotSettingsFile = Path.Combine(tempSettingsDir, "ckan-ui.json");
        var snapshotStore = new CkanUI.Services.Settings.UiSettingsStore(snapshotSettingsFile);
        // 场景 QA 入口：CKANUI_SNAP_SCENE=starfield|orbital|lunar|probe|lens|corona|ring|aurora|pulsar|sail|elevator|none，
        // CKANUI_SNAP_MOTION=full 可看到动态帧，CKANUI_SNAP_SKIN=<skin> 叠加主题皮肤。
        var snapshotScene = BackgroundSceneKinds.Normalize(Environment.GetEnvironmentVariable("CKANUI_SNAP_SCENE"));
        var snapshotMotion = Environment.GetEnvironmentVariable("CKANUI_SNAP_MOTION") ?? "none";
        var snapshotSkin = ThemeSkinKinds.Normalize(Environment.GetEnvironmentVariable("CKANUI_SNAP_SKIN"));
        snapshotStore.Save(new CkanUI.Services.Abstractions.UiSettings
        {
            MotionIntensity = snapshotMotion,
            BackgroundScene = snapshotScene,
            ThemeSkin = snapshotSkin,
        });

        var services = ServiceRegistration.Build(() => mainWindow, sc =>
        {
            sc.AddSingleton<IUiSettingsStore>(snapshotStore);
        });

        // 初始化本地化与主题
        var localizer = services.GetRequiredService<ILocalizer>();
        var locHost = new LocalizationHost(localizer, services.GetRequiredService<CkanUI.Services.Abstractions.IDispatcher>(), Application.Current!);
        locHost.Install();

        var themeHost = new ThemeHost(
            snapshotStore,
            services.GetRequiredService<CkanUI.Services.Abstractions.IDispatcher>(),
            Application.Current!,
            () => false);
        themeHost.Install();

        var motionHost = new MotionHost(
            snapshotStore,
            services.GetRequiredService<CkanUI.Services.Abstractions.IDispatcher>(),
            Application.Current!,
            () => false);
        motionHost.Install();
        Dispatcher.UIThread.RunJobs();
        Console.WriteLine($"Snapshot Store Motion: {snapshotStore.Load().MotionIntensity}");

        // 选中配置里的首选实例：CurrentInstance 只会由启动流程设置，快照不跑那套流程，
        // 少了这一步主页/实例页只能拍到空态（截图胶片带也就永远没有内容）。
        var instanceManager = services.GetRequiredService<CKAN.GameInstanceManager>();
        instanceManager.GetPreferredInstance();
        if (instanceManager.CurrentInstance is null && instanceManager.Instances.Values.FirstOrDefault() is { } fallback)
        {
            instanceManager.SetCurrentInstance(fallback);
        }
        services.GetRequiredService<IInstanceService>().RefreshAsync().GetAwaiter().GetResult();

        // 插件页（音乐/存档/…）默认只存在于 plugins\ 目录里、要等后台引导跑完才进导航栏；
        // 快照是同步流程，等不到那个后台任务。CKANUI_SNAP_PLUGINS=1 时先把引导跑完再建外壳。
        if (Environment.GetEnvironmentVariable("CKANUI_SNAP_PLUGINS") == "1")
        {
            services.GetRequiredService<CkanUI.Services.Plugins.PluginBootstrapper>()
                .RunAsync()
                .GetAwaiter()
                .GetResult();
            Dispatcher.UIThread.RunJobs();
        }

        var shell = services.GetRequiredService<ShellViewModel>();
        mainWindow = new MainWindow
        {
            DataContext = shell,
            Width = 1280,
            Height = 800
        };

        var palette = services.GetRequiredService<CkanUI.ViewModels.CommandPalette.CommandPaletteViewModel>();
        var catalog = services.GetRequiredService<CkanUI.Services.Abstractions.Commands.ICommandCatalog>();
        mainWindow.AttachCommandPalette(palette, catalog, s => s, null);

        // 背景宿主在真实窗口里由 App.axaml.cs 安装。快照必须同样装一遍：
        // 星空背景控件的 IsVisible 默认是 false、只由宿主打开，漏装它会让每张快照都少一层，
        // 页面全绿也不能证明这一层画得对。
        using var backgroundHost = new BackgroundHost(
            snapshotStore,
            services.GetRequiredService<AppDispatcher>(),
            Application.Current!,
            mainWindow,
            services.GetRequiredService<IInstanceService>());
        backgroundHost.Install();

        mainWindow.Show();
        Dispatcher.UIThread.RunJobs();
        mainWindow.Measure(new Size(1280, 800));
        mainWindow.Arrange(new Rect(0, 0, 1280, 800));
        Dispatcher.UIThread.RunJobs();

        // CKANUI_SNAP_POINTER=900,320 让 QA 截图带上光标交互态。
        if (Environment.GetEnvironmentVariable("CKANUI_SNAP_POINTER")?.Split(',') is { Length: 2 } pointerParts
            && double.TryParse(pointerParts[0], out var pointerX)
            && double.TryParse(pointerParts[1], out var pointerY))
        {
            var pointer = new Point(pointerX, pointerY);
            mainWindow.FindControl<StarfieldBackground>("BackgroundStarfield")?.TrackPointer(pointer);
            mainWindow.FindControl<BackgroundSceneCanvas>("BackgroundSceneCanvas")?.TrackPointer(pointer);
            Dispatcher.UIThread.RunJobs();
        }

        var outDir = Path.GetFullPath(
            Environment.GetEnvironmentVariable("CKANUI_SNAP_OUT") ?? "artifacts/snapshots");
        Directory.CreateDirectory(outDir);
        var maxPages = int.TryParse(Environment.GetEnvironmentVariable("CKANUI_SNAP_LIMIT"), out var limit)
            ? Math.Max(1, limit)
            : int.MaxValue;
        var onlyPage = Environment.GetEnvironmentVariable("CKANUI_SNAP_PAGE");
        var onlyWindow = Environment.GetEnvironmentVariable("CKANUI_SNAP_WINDOW");
        var settleMs = int.TryParse(Environment.GetEnvironmentVariable("CKANUI_SNAP_SETTLE_MS"), out var settle)
            ? Math.Max(100, settle)
            : 900;
        var scrollY = double.TryParse(Environment.GetEnvironmentVariable("CKANUI_SNAP_SCROLL"), out var scroll)
            ? Math.Max(0, scroll)
            : 0;

        Console.WriteLine($"场景 {snapshotScene} / 动效 {snapshotMotion}；找到导航项 {shell.NavItems.Count} 个，开始逐页无感渲染...");

        int count = 0;
        foreach (var item in shell.NavItems)
        {
            if (count >= maxPages)
            {
                break;
            }

            if (!string.IsNullOrWhiteSpace(onlyPage)
                && !string.Equals(item.Key, onlyPage, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            count++;
            Console.WriteLine($"[{count}/{shell.NavItems.Count}] 正在渲染: {item.Title} ({item.Key})...");
            shell.NavigateCommand.Execute(item.Key);
            Dispatcher.UIThread.RunJobs();
            mainWindow.Measure(new Size(1280, 800));
            mainWindow.Arrange(new Rect(0, 0, 1280, 800));
            Dispatcher.UIThread.RunJobs();

            // CKANUI_SNAP_SCROLL=1400 把当前页滚到指定纵向偏移，方便核对设置页靠下的行。
            if (scrollY > 0
                && mainWindow.FindControl<Control>("PageStage") is { } stage
                && stage.GetVisualDescendants().OfType<ScrollViewer>()
                    .FirstOrDefault(candidate => candidate.ScrollBarMaximum.Y > 0) is { } pageScroll)
            {
                pageScroll.Offset = new Vector(0, Math.Min(scrollY, pageScroll.ScrollBarMaximum.Y));
                mainWindow.Measure(new Size(1280, 800));
                mainWindow.Arrange(new Rect(0, 0, 1280, 800));
                Dispatcher.UIThread.RunJobs();
            }

            var safeKey = item.Key.Replace('/', '_');

            // 让导航指示条淡出、滚动条自动隐藏等过渡动画收敛后再截图，
            // 否则离屏渲染会捕捉到 0.6s 过渡的中间帧。
            // 注意：每 tick 必须推进足够的动画时间（16ms ≈ 一帧），
            // 只推进 1ms 时 60 次累计才 60ms，view-layer 的 0.3-0.6s 交叉淡入会停在半透明中间帧上。
            // Avalonia 的过渡动画走真实时间（Stopwatch），空转 RunJobs 不会让它们推进，
            // 所以这里既推进渲染帧，也真的等一会儿，否则 view-layer 的交叉淡入会停在中间帧。
            void Settle(int milliseconds)
            {
                var settleWatch = System.Diagnostics.Stopwatch.StartNew();
                while (settleWatch.ElapsedMilliseconds < milliseconds)
                {
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick(16);
                    Dispatcher.UIThread.RunJobs();
                    Thread.Sleep(16);
                }
            }

            void Capture(string suffix)
            {
                var outPath = Path.Combine(outDir, $"page_{count:D2}_{safeKey}{suffix}.png");
                var rtb = new RenderTargetBitmap(new PixelSize(1280, 800), new Vector(96, 96));
                rtb.Render(mainWindow);
                rtb.Save(outPath);
                var fi = new FileInfo(outPath);
                Console.WriteLine($"  -> 成功生成: {fi.Length} 字节 -> {Path.GetFileName(outPath)}");
            }

            // CKANUI_SNAP_POINTERS=120,120;1150,700 在同一进程里抓多组光标位置。
            // 星空种子每次启动都会换，跨进程对比会把「换了一份星空」误判成「视差移动」；
            // 同一进程内只改光标，才能把分层偏移单独拍出来。
            var pointerPoints = (Environment.GetEnvironmentVariable("CKANUI_SNAP_POINTERS") ?? string.Empty)
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(part => part.Split(','))
                .Where(parts => parts.Length == 2
                    && double.TryParse(parts[0], out _)
                    && double.TryParse(parts[1], out _))
                .Select(parts => new Point(double.Parse(parts[0]), double.Parse(parts[1])))
                .ToArray();

            if (pointerPoints.Length > 0)
            {
                for (var p = 0; p < pointerPoints.Length; p++)
                {
                    var pointer = pointerPoints[p];
                    mainWindow.FindControl<StarfieldBackground>("BackgroundStarfield")?.TrackPointer(pointer);
                    mainWindow.FindControl<BackgroundSceneCanvas>("BackgroundSceneCanvas")?.TrackPointer(pointer);
                    Dispatcher.UIThread.RunJobs();
                    Settle(500);
                    Capture($"_p{p + 1}");
                }

                continue;
            }

            // 进程内第一次换页要多等一会儿：入场弹簧靠 RequestAnimationFrame 推进，
            // 而本工具的 Settle 每轮约 32ms 真实时间只推进 16ms 动画时间，
            // 默认 900ms 只能推完一半，首屏会拍到半透明的中间帧（看着像整页空白）。
            // 之后的页面有前面的帧垫底，按默认时长就够。
            Settle(count == 1 ? settleMs * 3 : settleMs);
            Capture(string.Empty);
        }

        if (!string.IsNullOrWhiteSpace(onlyWindow))
        {
            CapturePluginWindow(mainWindow, shell, onlyWindow.Trim(), outDir);
        }

        Console.WriteLine("=== 全量页面离屏截图完成！===");
    }

    /// <summary>CKANUI_SNAP_WINDOW=music-library|sfs-workbench 时，额外打开插件窗口并单独截图。</summary>
    private static void CapturePluginWindow(MainWindow mainWindow, ShellViewModel shell, string kind, string outDir)
    {
        var pageKey = kind switch
        {
            "music-library" => "music",
            "sfs-workbench" => "saves",
            _ => throw new ArgumentException($"未知的快照窗口: {kind}")
        };

        shell.NavigateCommand.Execute(pageKey);
        mainWindow.Measure(new Size(1280, 800));
        mainWindow.Arrange(new Rect(0, 0, 1280, 800));
        Dispatcher.UIThread.RunJobs();
        Pump(300);

        if (kind == "music-library")
        {
            var pageView = mainWindow.GetVisualDescendants()
                .FirstOrDefault(visual => visual.GetType().FullName == "CkanUI.Plugin.Soundtrack.UI.SoundtrackPageView")
                ?? throw new InvalidOperationException("音乐页没有加载，无法打开乐曲库窗口。");
            var pageVm = pageView.DataContext ?? throw new InvalidOperationException("音乐页没有 DataContext。");
            var rows = pageVm.GetType().GetProperty("AllLibraryRows")?.GetValue(pageVm)
                ?? throw new InvalidOperationException("音乐页没有 AllLibraryRows。");
            var rowType = pageVm.GetType().GetProperty("AllLibraryRows")!.PropertyType.GetGenericArguments()[0];
            var add = typeof(Program).GetMethod(nameof(CreateAdd), BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(rowType).Invoke(null, null);
            var play = typeof(Program).GetMethod(nameof(CreatePlay), BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(rowType).Invoke(null, null);
            var playlistName = pageVm.GetType().GetProperty("SelectedPlaylistName")?.GetValue(pageVm) as string ?? string.Empty;

            var soundtrackAssembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(assembly => assembly.GetName().Name == "CkanUI.Plugin.Soundtrack")
                ?? throw new InvalidOperationException("音乐插件程序集没有加载。");
            var pickerType = soundtrackAssembly.GetType("CkanUI.Plugin.Soundtrack.UI.SoundtrackLibraryPickerViewModel")
                ?? throw new InvalidOperationException("找不到乐曲库 ViewModel 类型。");
            var windowType = soundtrackAssembly.GetType("CkanUI.Plugin.Soundtrack.UI.SoundtrackLibraryWindow")
                ?? throw new InvalidOperationException("找不到乐曲库窗口类型。");
            var picker = Activator.CreateInstance(pickerType, rows, add, play, playlistName)
                ?? throw new InvalidOperationException("乐曲库 ViewModel 创建失败。");
            var window = (Window)(Activator.CreateInstance(windowType)
                ?? throw new InvalidOperationException("乐曲库窗口创建失败。"));
            window.DataContext = picker;
            window.Width = 1080;
            window.Height = 720;
            window.Show(mainWindow);
            Pump(400);
            CaptureWindow(window, Path.Combine(outDir, "window_music_library.png"), 1080, 720);
            window.Close();
            return;
        }

        var savesView = mainWindow.GetVisualDescendants()
            .FirstOrDefault(visual => visual.GetType().FullName == "CkanUI.Plugin.Saves.SavesPageView")
            ?? throw new InvalidOperationException("存档页没有加载，无法打开 SFS 工作台。");
        var savesVm = savesView.DataContext ?? throw new InvalidOperationException("存档页没有 DataContext。");
        var rowList = savesVm.GetType().GetProperty("Rows")?.GetValue(savesVm) as IEnumerable;
        var firstRow = rowList?.Cast<object>().FirstOrDefault()
            ?? throw new InvalidOperationException("种子实例没有存档，无法打开 SFS 工作台。");
        savesVm.GetType().GetProperty("SelectedRow")!.SetValue(savesVm, firstRow);
        savesView.GetType().GetMethod("OpenWorkbenchCore", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(savesView, null);
        Pump(700);

        var workbench = mainWindow.OwnedWindows
            .FirstOrDefault(window => window.GetType().FullName == "CkanUI.Plugin.Saves.SfsWorkbenchWindow")
            ?? throw new InvalidOperationException("SFS 工作台窗口没有打开。");
        workbench.Width = 1280;
        workbench.Height = 860;
        Pump(300);
        CaptureWindow(workbench, Path.Combine(outDir, "window_sfs_workbench.png"), 1280, 860);
        workbench.Close();
    }

    private static void CaptureWindow(Window window, string path, int width, int height)
    {
        window.Measure(new Size(width, height));
        window.Arrange(new Rect(0, 0, width, height));
        Dispatcher.UIThread.RunJobs();
        Pump(250);
        using var bitmap = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96));
        bitmap.Render(window);
        bitmap.Save(path);
        Console.WriteLine($"  -> 窗口截图: {new FileInfo(path).Length} 字节 -> {Path.GetFileName(path)}");
    }

    private static void Pump(int milliseconds)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < milliseconds)
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(16);
            Dispatcher.UIThread.RunJobs();
            System.Threading.Thread.Sleep(16);
        }
    }

    private static Func<T, bool> CreateAdd<T>() => _ => true;

    private static Action<T> CreatePlay<T>() => _ => { };
}
