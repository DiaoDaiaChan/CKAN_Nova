using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using CkanUI.App.Tests.Automation;
using CkanUI.App.Theming;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using NUnit.Framework;

namespace CkanUI.App.Tests;

/// <summary>
/// 背景层（20 篇 P-06 / THM-05、P-07）：画笔工厂的算术、按实例覆盖的解析、
/// 以及宿主对四个命名控件的实际写入。
/// </summary>
/// <remarks>
/// 无头宿主用 <c>UseHeadlessDrawing</c>，没有真实渲染后端，所以这里只验「画笔设进去了没有、
/// 参数对不对」，不验像素；也不解码真实图片（<c>Bitmap</c> 解码需要渲染后端），
/// 涉及位图的分支一律用「路径不存在」来覆盖「读不出来就回到无背景」。
/// </remarks>
[TestFixture]
public sealed class BackgroundHostTests
{
    [Test]
    public void Factory_CreateLayerBrush_SolidUsesTheProfileColor()
    {
        var profile = new BackgroundProfile { Kind = BackgroundProfile.KindSolid, Color = "#123456" };

        var brush = BackgroundBrushFactory.CreateLayerBrush(profile) as ISolidColorBrush;

        Assert.That(brush, Is.Not.Null);
        Assert.That(brush!.Color, Is.EqualTo(Color.Parse("#123456")));
    }

    [Test]
    public void Factory_CreateGradient_AngleNinetyPointsDownwards()
    {
        var profile = new BackgroundProfile
        {
            Kind = BackgroundProfile.KindGradient,
            GradientStart = "#101010",
            GradientEnd = "#F0F0F0",
            GradientAngle = 90,
        };

        var brush = BackgroundBrushFactory.CreateLayerBrush(profile) as LinearGradientBrush;

        Assert.That(brush, Is.Not.Null);
        // 90° = 从上往下：起点贴顶边，终点贴底边。
        Assert.That(brush!.StartPoint.Point.Y, Is.EqualTo(0).Within(1e-6));
        Assert.That(brush.EndPoint.Point.Y, Is.EqualTo(1).Within(1e-6));
        Assert.That(brush.StartPoint.Point.X, Is.EqualTo(0.5).Within(1e-6));
        Assert.That(brush.GradientStops, Has.Count.EqualTo(2));
        Assert.That(brush.GradientStops[0].Color, Is.EqualTo(Color.Parse("#101010")));
        Assert.That(brush.GradientStops[1].Color, Is.EqualTo(Color.Parse("#F0F0F0")));
    }

    [Test]
    public void Factory_CreateGradient_AngleZeroPointsRightwards()
    {
        var brush = (LinearGradientBrush)BackgroundBrushFactory.CreateGradient(Colors.Black, Colors.White, 0)!;

        Assert.That(brush.StartPoint.Point.X, Is.EqualTo(0).Within(1e-6));
        Assert.That(brush.EndPoint.Point.X, Is.EqualTo(1).Within(1e-6));
        Assert.That(brush.StartPoint.Point.Y, Is.EqualTo(0.5).Within(1e-6));
    }

    [Test]
    public void Factory_CreateLayerBrush_IsNullWithoutAColorSource()
    {
        Assert.That(BackgroundBrushFactory.CreateLayerBrush(BackgroundProfile.Default), Is.Null);
        Assert.That(
            BackgroundBrushFactory.CreateLayerBrush(new BackgroundProfile { Kind = "solid", Color = "不是颜色" }),
            Is.Null);
    }

    [TestCase("fill", Stretch.UniformToFill)]
    [TestCase("fit", Stretch.Uniform)]
    [TestCase("stretch", Stretch.Fill)]
    [TestCase("center", Stretch.None)]
    [TestCase("tile", Stretch.None)]
    [TestCase(null, Stretch.UniformToFill)]
    [TestCase("乱写", Stretch.UniformToFill)]
    public void Factory_ToStretch_MapsTheFillModes(string? fill, Stretch expected) =>
        Assert.That(BackgroundBrushFactory.ToStretch(fill), Is.EqualTo(expected));

    [Test]
    public void Factory_DimColor_TakesAlphaFromThePercent()
    {
        var color = BackgroundBrushFactory.DimColor(new BackgroundProfile { DimPercent = 40 });

        Assert.That(color.R, Is.EqualTo(0));
        Assert.That(color.A, Is.EqualTo(102));
        Assert.That(BackgroundBrushFactory.DimColor(new BackgroundProfile { DimPercent = 0 }).A, Is.EqualTo(0));
        Assert.That(BackgroundBrushFactory.DimColor(new BackgroundProfile { DimPercent = 250 }).A, Is.EqualTo(255));
    }

    [Test]
    public void Factory_SurfaceWashKeepsTextReadable()
    {
        // 洗白层越不透明底图越淡、文字越清楚；这条常量就是「P-06 与可读性」的折中值。
        Assert.That(BackgroundBrushFactory.SurfaceWashAlpha, Is.EqualTo(0.74).Within(1e-9));
        Assert.That(BackgroundBrushFactory.ToOpacity(new BackgroundProfile { OpacityPercent = 250 }), Is.EqualTo(1));
        Assert.That(BackgroundBrushFactory.ToOpacity(new BackgroundProfile { OpacityPercent = -20 }), Is.EqualTo(0));
    }

    [Test]
    public void ResolveProfile_PrefersTheInstanceOverride()
    {
        var settings = new UiSettings
        {
            Background = new BackgroundProfile { Kind = "solid", Color = "#111111" },
            BackgroundByInstance = new Dictionary<string, BackgroundProfile>(StringComparer.Ordinal)
            {
                ["默认"] = new BackgroundProfile { Kind = GradientKind },
            },
        };

        Assert.That(BackgroundHost.ResolveProfile(settings, "默认").Kind, Is.EqualTo(GradientKind));
        Assert.That(BackgroundHost.ResolveProfile(settings, "另一个实例").Kind, Is.EqualTo("solid"));
        Assert.That(BackgroundHost.ResolveProfile(settings, null).Kind, Is.EqualTo("solid"));
        Assert.That(BackgroundHost.ResolveProfile(new UiSettings(), null).IsActive, Is.False);
    }

    [Test]
    public void ResolveImagePathFor_KeepsTheConfiguredPathForTheImageKind()
    {
        var profile = new BackgroundProfile { Kind = BackgroundProfile.KindImage, ImagePath = "C:/pictures/bg.png" };

        Assert.That(
            BackgroundHost.ResolveImagePathFor(profile, gameDirectory: null),
            Is.EqualTo("C:/pictures/bg.png"));
    }

    [Test]
    public void ResolveImagePathFor_FindsTheLatestScreenshotOfTheGameDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "ckanui-shot-" + Guid.NewGuid().ToString("N"));
        var folder = Path.Combine(root, CkanUI.Services.Settings.BackgroundImageLocator.ScreenshotsFolderName);
        Directory.CreateDirectory(folder);
        try
        {
            var older = Path.Combine(folder, "older.png");
            var newer = Path.Combine(folder, "newer.png");
            File.WriteAllBytes(older, new byte[] { 1 });
            File.WriteAllBytes(newer, new byte[] { 1 });
            File.SetLastWriteTimeUtc(older, new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            File.SetLastWriteTimeUtc(newer, new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc));

            var profile = new BackgroundProfile { Kind = BackgroundProfile.KindScreenshot };

            Assert.That(BackgroundHost.ResolveImagePathFor(profile, root), Is.EqualTo(newer));
            // 没有截图目录时退化成「无背景」，而不是抛异常或留旧图。
            Assert.That(BackgroundHost.ResolveImagePathFor(profile, Path.Combine(root, "现在不存在")), Is.Null);
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (IOException)
            {
                // 清理失败不影响断言结论。
            }
        }
    }

    [Test]
    public void Loader_ReportsWhyItCannotLoad()
    {
        Assert.That(BackgroundImageLoader.Load(null, out var empty), Is.Null);
        Assert.That(empty, Does.Contain("未设置"));

        var missing = Path.Combine(Path.GetTempPath(), "ckan-ui-" + Guid.NewGuid().ToString("N") + ".png");
        Assert.That(BackgroundImageLoader.Load(missing, out var error), Is.Null);
        Assert.That(error, Does.Contain("找不到背景图片"));
    }

    [Test]
    public void Host_PaintsSolidAndClearsWhenTheKindGoesBackToNone()
    {
        var (host, window, controls, store) = NewHost(new UiSettings
        {
            Background = new BackgroundProfile { Kind = BackgroundProfile.KindSolid, Color = "#123456", DimPercent = 20 },
        });

        using (host)
        {
            host.Install();

            var solid = controls.Layer.Background as ISolidColorBrush;
            Assert.That(solid, Is.Not.Null, "纯色来源要落在画笔层上");
            Assert.That(solid!.Color, Is.EqualTo(Color.Parse("#123456")));
            Assert.That(controls.Layer.Opacity, Is.EqualTo(1));
            Assert.That(controls.Dim.Opacity, Is.EqualTo(1));
            Assert.That(controls.Scrim.Opacity, Is.EqualTo(BackgroundBrushFactory.SurfaceWashAlpha));

            store.Save(new UiSettings { Background = new BackgroundProfile { Kind = BackgroundProfile.KindNone } });

            Assert.That(controls.Layer.Background, Is.Null);
            Assert.That(controls.Dim.Opacity, Is.EqualTo(0));
            Assert.That(controls.Scrim.Opacity, Is.EqualTo(0), "没有背景时不能留一层洗白");
        }

        window.Close();
    }

    [Test]
    public void Host_ReappliesOnlyWhenTheProfileChanges()
    {
        var (host, window, controls, store) = NewHost(new UiSettings
        {
            Background = new BackgroundProfile { Kind = BackgroundProfile.KindSolid, Color = "#123456" },
        });

        using (host)
        {
            host.Install();
            var painted = controls.Layer.Background;

            // 相同 profile 再来一次：签名一致，不该重建画笔（否则每次保存设置都抖一帧）。
            store.Save(store.Current);
            Assert.That(controls.Layer.Background, Is.SameAs(painted));

            store.Save(new UiSettings
            {
                Background = new BackgroundProfile { Kind = BackgroundProfile.KindSolid, Color = "#654321" },
            });
            Assert.That(((ISolidColorBrush)controls.Layer.Background!).Color, Is.EqualTo(Color.Parse("#654321")));
        }

        window.Close();
    }

    [Test]
    public void Host_SkipsEverythingInHighContrast()
    {
        HeadlessAppHost.EnsureInitialized();
        var app = Application.Current!;
        var original = app.RequestedThemeVariant;
        app.RequestedThemeVariant = ThemeHost.HighContrastVariant;

        var (host, window, controls, _) = NewHost(new UiSettings
        {
            Background = new BackgroundProfile { Kind = BackgroundProfile.KindSolid, Color = "#123456" },
        });

        try
        {
            using (host)
            {
                host.Install();

                // THM-10：系统高对比度优先，背景一层都不画。
                Assert.That(controls.Layer.Background, Is.Null);
                Assert.That(controls.Scrim.Opacity, Is.EqualTo(0));
            }
        }
        finally
        {
            app.RequestedThemeVariant = original;
            window.Close();
        }
    }

    [Test]
    public void Host_MissingImageFallsBackToNoBackground()
    {
        var missing = Path.Combine(Path.GetTempPath(), "ckan-ui-" + Guid.NewGuid().ToString("N") + ".png");
        var (host, window, controls, _) = NewHost(new UiSettings
        {
            Background = new BackgroundProfile { Kind = BackgroundProfile.KindImage, ImagePath = missing },
        });

        using (host)
        {
            host.Install();

            // 读不出来的图不能留一块黑底：整层回到无背景。
            Assert.That(controls.Layer.Background, Is.Null);
            Assert.That(controls.Image.Source, Is.Null);
            Assert.That(controls.Dim.Opacity, Is.EqualTo(0));
        }

        window.Close();
    }

    [Test]
    public void Host_WithoutTheNamedControls_DoesNotThrow()
    {
        HeadlessAppHost.EnsureInitialized();
        var store = new FakeStore(new UiSettings
        {
            Background = new BackgroundProfile { Kind = BackgroundProfile.KindSolid, Color = "#123456" },
        });
        var window = HeadlessAppHost.CreateTestWindow(new Border());

        using (var host = new BackgroundHost(store, new InlineDispatcher(), Application.Current!, window))
        {
            // 设计时预览/窗口还没套模板：少控件只记一条调试日志，不能把启动搞崩。
            Assert.DoesNotThrow(host.Install);
            Assert.That(store.Load().Background!.Kind, Is.EqualTo("solid"));
        }

        window.Close();
    }

    [Test]
    public void Host_IsReferencedBy_ComparesReferencesNotPixels()
    {
        var cached = new FakeImage();
        var other = new FakeImage();

        // 图片层正在显示同一张图 → 不能释放（渲染线程还在用它）。
        Assert.That(BackgroundHost.IsReferencedBy(cached, cached, null), Is.True);
        // 换了另一张图 → 缓存里的这张已经没人用了。
        Assert.That(BackgroundHost.IsReferencedBy(cached, other, null), Is.False);
        // 平铺来源同样按引用比。
        Assert.That(BackgroundHost.IsReferencedBy(cached, null, cached), Is.True);
        Assert.That(BackgroundHost.IsReferencedBy(cached, null, other), Is.False);
        Assert.That(BackgroundHost.IsReferencedBy(cached, null, null), Is.False);
        Assert.That(BackgroundHost.IsReferencedBy(cached, null, new SolidColorBrush(Colors.Black)), Is.False);
    }

    [Test]
    public void Host_ReleaseCachedBitmap_IsFalseWithoutACache()
    {
        var (host, window, controls, _) = NewHost(new UiSettings
        {
            Background = new BackgroundProfile { Kind = BackgroundProfile.KindSolid, Color = "#123456" },
        });

        using (host)
        {
            host.Install();

            // 纯色来源本来就没有位图，调用方（L0 释放）要能靠返回值区分「放掉了」和「没得放」。
            Assert.That(host.TryReleaseCachedBitmap(), Is.False);
            Assert.That(host.TryReleaseCachedBitmap(), Is.False);
            Assert.That(controls.Layer.Background, Is.Not.Null, "释放不该顺手清掉画笔层");
        }

        window.Close();
    }

    private sealed class FakeImage : IImage
    {
        public Size Size => new(1, 1);

        public void Draw(DrawingContext context, Rect sourceRect, Rect destRect)
        {
        }
    }

    private const string GradientKind = "gradient";

    private static (BackgroundHost Host, Window Window, BackgroundControls Controls, FakeStore Store) NewHost(UiSettings settings)
    {
        HeadlessAppHost.EnsureInitialized();
        var controls = new BackgroundControls();
        var window = HeadlessAppHost.CreateTestWindow(controls.Root);
        // FindControl 是在传入的控件（这里是 Window）的名字域里查的，名字域必须挂在窗口上。
        NameScope.SetNameScope(window, controls.Scope);
        var store = new FakeStore(settings);
        var host = new BackgroundHost(store, new InlineDispatcher(), Application.Current!, window, new FakeInstances());
        return (host, window, controls, store);
    }

    /// <summary>按 <c>MainWindow.axaml</c> 里的名字搭一套背景层，并注册名字域供 <c>FindControl</c> 命中。</summary>
    private sealed class BackgroundControls
    {
        public BackgroundControls()
        {
            Scope = new NameScope();
            Scope.Register(BackgroundHost.LayerControlName, Layer);
            Scope.Register(BackgroundHost.ImageControlName, Image);
            Scope.Register(BackgroundHost.DimControlName, Dim);
            Scope.Register(BackgroundHost.ScrimControlName, Scrim);
            Root = new Panel { Children = { Layer, Image, Dim, Scrim } };
            NameScope.SetNameScope(Root, Scope);
        }

        public Panel Root { get; }

        public NameScope Scope { get; }

        public Border Layer { get; } = new() { Name = BackgroundHost.LayerControlName };

        public Image Image { get; } = new() { Name = BackgroundHost.ImageControlName };

        public Border Dim { get; } = new() { Name = BackgroundHost.DimControlName };

        public Border Scrim { get; } = new() { Name = BackgroundHost.ScrimControlName };
    }

    private sealed class InlineDispatcher : IDispatcher
    {
        public bool IsOnUiThread => true;

        public void Post(Action action) => action();

        public Task InvokeAsync(Action action)
        {
            action();
            return Task.CompletedTask;
        }

        public Task<T> InvokeAsync<T>(Func<T> func) => Task.FromResult(func());
    }

    private sealed class FakeStore : IUiSettingsStore
    {
        public FakeStore(UiSettings current) => Current = current;

        public UiSettings Current { get; private set; }

        public event Action<UiSettings>? Changed;

        public UiSettings Load() => Current;

        public OperationResult<bool> Save(UiSettings settings)
        {
            Current = settings;
            Changed?.Invoke(settings);
            return OperationResult<bool>.Ok(true);
        }
    }

    private sealed class FakeInstances : IInstanceService
    {
        public IReadOnlyList<InstanceInfo> Instances { get; } = Array.Empty<InstanceInfo>();

        public InstanceInfo? Current { get; set; }

        public event Action? InstancesChanged;

        public event Action<InstanceInfo?>? CurrentChanged;

        public bool IsGameRunning(string instanceName = "") => false;

        public Task<OperationResult<IReadOnlyList<InstanceInfo>>> RefreshAsync(CancellationToken ct = default) =>
            Task.FromResult(OperationResult<IReadOnlyList<InstanceInfo>>.Ok(Instances));

        public Task<OperationResult<InstanceInfo>> SwitchAsync(string instanceName, CancellationToken ct = default) =>
            Task.FromResult(OperationResult<InstanceInfo>.Fail("测试不切换实例"));

        public Task<OperationResult<InstanceInfo>> AddAsync(string path, string? name = null, CancellationToken ct = default) =>
            Task.FromResult(OperationResult<InstanceInfo>.Fail("测试不添加实例"));

        public Task<OperationResult<InstanceInfo>> CloneAsync(string sourceName, string newName, CancellationToken ct = default) =>
            Task.FromResult(OperationResult<InstanceInfo>.Fail("测试不克隆实例"));

        public Task<OperationResult<bool>> RemoveAsync(string instanceName, CancellationToken ct = default) =>
            Task.FromResult(OperationResult<bool>.Ok(true));

        public Task<OperationResult<InstanceInfo>> RenameAsync(string instanceName, string newName, CancellationToken ct = default) =>
            Task.FromResult(OperationResult<InstanceInfo>.Fail("测试不重命名实例"));

        public void RaiseCurrentChanged(InstanceInfo? instance) => CurrentChanged?.Invoke(instance);

        public void RaiseInstancesChanged() => InstancesChanged?.Invoke();
    }
}
