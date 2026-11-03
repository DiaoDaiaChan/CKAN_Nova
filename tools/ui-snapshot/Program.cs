#pragma warning disable CS0618
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CkanUI.App;
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
        // 场景 QA 入口：CKANUI_SNAP_SCENE=orbital|lunar|probe|lens|none，CKANUI_SNAP_MOTION=full 可看到动态帧。
        var snapshotScene = BackgroundSceneKinds.Normalize(Environment.GetEnvironmentVariable("CKANUI_SNAP_SCENE"));
        var snapshotMotion = Environment.GetEnvironmentVariable("CKANUI_SNAP_MOTION") ?? "none";
        snapshotStore.Save(new CkanUI.Services.Abstractions.UiSettings
        {
            MotionIntensity = snapshotMotion,
            BackgroundScene = snapshotScene,
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
            mainWindow.FindControl<BackgroundSceneCanvas>("BackgroundSceneCanvas")?.TrackPointer(new Point(pointerX, pointerY));
            Dispatcher.UIThread.RunJobs();
        }

        var outDir = Path.GetFullPath(
            Environment.GetEnvironmentVariable("CKANUI_SNAP_OUT") ?? "artifacts/snapshots");
        Directory.CreateDirectory(outDir);
        var maxPages = int.TryParse(Environment.GetEnvironmentVariable("CKANUI_SNAP_LIMIT"), out var limit)
            ? Math.Max(1, limit)
            : int.MaxValue;
        var onlyPage = Environment.GetEnvironmentVariable("CKANUI_SNAP_PAGE");
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

            // 让导航指示条淡出、滚动条自动隐藏等过渡动画收敛后再截图，
            // 否则离屏渲染会捕捉到 0.6s 过渡的中间帧。
            // 注意：每 tick 必须推进足够的动画时间（16ms ≈ 一帧），
            // 只推进 1ms 时 60 次累计才 60ms，view-layer 的 0.3-0.6s 交叉淡入会停在半透明中间帧上。
            // Avalonia 的过渡动画走真实时间（Stopwatch），空转 RunJobs 不会让它们推进，
            // 所以这里既推进渲染帧，也真的等一会儿，否则 view-layer 的交叉淡入会停在中间帧。
            var settleWatch = System.Diagnostics.Stopwatch.StartNew();
            while (settleWatch.ElapsedMilliseconds < settleMs)
            {
                AvaloniaHeadlessPlatform.ForceRenderTimerTick(16);
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(16);
            }

            var safeKey = item.Key.Replace('/', '_');
            var outPath = Path.Combine(outDir, $"page_{count:D2}_{safeKey}.png");

            var rtb = new RenderTargetBitmap(new PixelSize(1280, 800), new Vector(96, 96));
            rtb.Render(mainWindow);
            rtb.Save(outPath);

            var fi = new FileInfo(outPath);
            Console.WriteLine($"  -> 成功生成: {fi.Length} 字节 -> {Path.GetFileName(outPath)}");
        }

        Console.WriteLine("=== 全量页面离屏截图完成！===");
    }
}
