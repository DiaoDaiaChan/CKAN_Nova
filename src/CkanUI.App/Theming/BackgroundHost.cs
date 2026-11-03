using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

using CkanUI.App.Controls;
using CkanUI.App.Controls.Backgrounds;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.Services.Settings;
using CkanUI.ViewModels.Pages.Settings;

using log4net;

namespace CkanUI.App.Theming;

/// <summary>
/// 界面背景宿主（20 篇 P-06 / THM-05、P-07 按实例覆盖）：把 <see cref="UiSettings.Background"/>
/// 画到 <c>MainWindow</c> 的背景层上，并在设置保存、主题切换、实例切换后立即重算。
/// </summary>
/// <remarks>
/// 为什么落在 App 层：这里要碰 <c>Bitmap</c>/<c>ImageBrush</c>/<c>BlurEffect</c>，而视图模型层
/// 禁止引用 Avalonia（04 篇 §1 分层铁律）。背景层由 <c>MainWindow.axaml</c> 里的
/// <c>BackgroundLayer</c>/<c>BackgroundImage</c>/<c>BackgroundDim</c>/<c>BackgroundScrim</c> 四个命名控件组成，
/// 外加一个不吃设置项的 <c>BackgroundStarfield</c>（星空装饰层）。本类只按名字取控件、按设置刷画笔，
/// 不改 XAML 结构。
/// </remarks>
public sealed class BackgroundHost : IDisposable
{
    /// <summary>纯色/渐变/平铺来源的画笔层。</summary>
    public const string LayerControlName = "BackgroundLayer";

    /// <summary>单图来源的图片层。</summary>
    public const string ImageControlName = "BackgroundImage";

    /// <summary>暗化层（黑色，按 <see cref="BackgroundProfile.DimPercent"/> 取 alpha）。</summary>
    public const string DimControlName = "BackgroundDim";

    /// <summary>表面洗白层（主题底色，压 74% 让底图隐约透出）。</summary>
    public const string ScrimControlName = "BackgroundScrim";

    /// <summary>星空背景层（自绘装饰）：由场景设置、高对比度与「用户自选了背景」共同决定是否露面。</summary>
    public const string StarfieldControlName = "BackgroundStarfield";

    /// <summary>新的程序化场景画布（轨道蓝图/月面/探测器/引力透镜）。</summary>
    public const string SceneControlName = "BackgroundSceneCanvas";

    private static readonly ILog Log = LogManager.GetLogger(typeof(BackgroundHost));

    private readonly IUiSettingsStore _settings;
    private readonly IDispatcher _dispatcher;
    private readonly Application _application;
    private readonly Window _window;
    private readonly IInstanceService? _instances;

    private bool _installed;
    private string? _cachedPath;
    private Bitmap? _cachedBitmap;
    private string? _appliedSignature;

    /// <summary>构造背景宿主。</summary>
    /// <param name="settings">UI 自有设置存储（背景的唯一真源）。</param>
    /// <param name="dispatcher">UI 线程封送器（<c>Changed</c> 可能在后台线程触发）。</param>
    /// <param name="application">当前 Avalonia 应用，用于读主题变体判断高对比度。</param>
    /// <param name="window">主窗口，用于按名字取背景层控件。</param>
    /// <param name="instances">实例服务（P-07）；为空时只认全局背景。</param>
    public BackgroundHost(
        IUiSettingsStore settings,
        IDispatcher dispatcher,
        Application application,
        Window window,
        IInstanceService? instances = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(window);

        _settings = settings;
        _dispatcher = dispatcher;
        _application = application;
        _window = window;
        _instances = instances;
    }

    /// <summary>安装：先按当前设置刷一遍，再订阅设置、主题与实例变化。</summary>
    public void Install()
    {
        if (_installed)
        {
            return;
        }

        _installed = true;
        Apply(_settings.Load());
        _settings.Changed += OnSettingsChanged;
        _application.ActualThemeVariantChanged += OnThemeVariantChanged;
        if (_instances is not null)
        {
            _instances.CurrentChanged += OnCurrentInstanceChanged;
        }
    }

    /// <summary>按当前设置重算背景（主题/实例切换后由订阅驱动，也可手工调用）。</summary>
    public void Refresh() => Apply(_settings.Load());

    private void OnSettingsChanged(UiSettings settings) => _dispatcher.Post(() => Apply(settings));

    private void OnThemeVariantChanged(object? sender, EventArgs e) => _dispatcher.Post(Refresh);

    private void OnCurrentInstanceChanged(InstanceInfo? instance) => _dispatcher.Post(Refresh);

    /// <summary>
    /// 把设置画到背景层。<b>必须在 UI 线程调用</b>（<c>Install</c>/<c>Refresh</c> 已做好封送）。
    /// </summary>
    public void Apply(UiSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var layer = Find<Border>(LayerControlName);
        var image = Find<Image>(ImageControlName);
        var dim = Find<Border>(DimControlName);
        var scrim = Find<Border>(ScrimControlName);
        if (layer is null || image is null || dim is null || scrim is null)
        {
            // 设计时预览或窗口还没套用模板：记一次调试日志就够了，别每帧刷警告。
            Log.Debug("背景层控件尚未就绪，跳过本次背景刷新。");
            return;
        }

        var profile = ResolveProfile(settings);
        var highContrast = _application.ActualThemeVariant == ThemeHost.HighContrastVariant;
        ApplySceneVisibility(highContrast, profile.IsActive, settings.BackgroundScene);
        if (highContrast || !profile.IsActive)
        {
            ClearLayers(layer, image, dim, scrim);
            if (highContrast && profile.IsActive)
            {
                Log.Info("系统高对比度优先，已跳过自定义背景（THM-10）。");
            }

            _appliedSignature = null;
            return;
        }

        var clamped = profile.Clamp();
        var imagePath = ResolveImagePath(clamped);
        var signature = BuildSignature(clamped, imagePath);
        if (string.Equals(signature, _appliedSignature, StringComparison.Ordinal))
        {
            return;
        }

        var isImage = IsImageKind(clamped.Kind);
        var opacity = BackgroundBrushFactory.ToOpacity(clamped);
        var blur = BackgroundBrushFactory.CreateBlur(clamped);
        var tiled = BackgroundBrushFactory.IsTiled(clamped);

        Bitmap? bitmap = null;
        if (isImage)
        {
            bitmap = EnsureBitmap(imagePath, layer, image);
            if (bitmap is null)
            {
                // 图读不出来（路径错、格式坏）时不要留一块黑底，直接回到无背景。
                ClearLayers(layer, image, dim, scrim);
                _appliedSignature = null;
                return;
            }
        }

        if (isImage && !tiled)
        {
            layer.Background = null;
            layer.Effect = null;
            image.Source = bitmap;
            image.Stretch = BackgroundBrushFactory.ToStretch(clamped.Fill);
            image.Effect = blur;
        }
        else
        {
            image.Source = null;
            image.Effect = null;
            layer.Background = tiled
                ? BackgroundBrushFactory.CreateTileBrush(bitmap)
                : BackgroundBrushFactory.CreateLayerBrush(clamped);
            layer.Effect = blur;
        }

        layer.Opacity = opacity;
        image.Opacity = opacity;
        dim.Background = new SolidColorBrush(BackgroundBrushFactory.DimColor(clamped));
        dim.Opacity = 1;
        scrim.Opacity = BackgroundBrushFactory.SurfaceWashAlpha;

        if (!isImage)
        {
            // 从图片切到纯色/渐变后，旧位图已经没有任何控件引用它了，别留着占几 MB。
            _ = TryReleaseCachedBitmap();
        }

        _appliedSignature = signature;
    }

    /// <summary>
    /// 按名字取背景层控件；取不到（设计时预览、窗口还没套模板、控件被模板重命名）返回 <c>null</c>。
    /// </summary>
    /// <remarks>
    /// <c>FindControl</c> 在找不到名字域时会抛 <see cref="InvalidOperationException"/>，
    /// 而「背景层还不存在」是启动早期的正常状态，不该让它把启动打断。
    /// </remarks>
    private T? Find<T>(string name)
        where T : Control
    {
        try
        {
            return _window.FindControl<T>(name);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>按实例覆盖解析生效的背景（P-07）：有本实例的键就用它，否则跟随全局。</summary>
    public static BackgroundProfile ResolveProfile(UiSettings settings, string? instanceName)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (!string.IsNullOrWhiteSpace(instanceName)
            && settings.BackgroundByInstance.TryGetValue(instanceName, out var perInstance)
            && perInstance is not null)
        {
            return perInstance;
        }

        return settings.Background ?? BackgroundProfile.Default;
    }

    private BackgroundProfile ResolveProfile(UiSettings settings) =>
        ResolveProfile(settings, SafeCurrentInstanceName());

    private string? SafeCurrentInstanceName()
    {
        try
        {
            return _instances?.Current?.Name;
        }
        catch (Exception)
        {
            // 启动早期实例服务可能还没读盘；背景退回全局即可，不该因此丢一帧。
            return null;
        }
    }

    private Bitmap? EnsureBitmap(string? path, Border layer, Image image)
    {
        if (string.Equals(path, _cachedPath, StringComparison.Ordinal) && _cachedBitmap is not null)
        {
            return _cachedBitmap;
        }

        // 先摘掉旧图再释放：位图还被画笔/控件引用时 Dispose 会让渲染线程炸。
        image.Source = null;
        layer.Background = null;

        _cachedBitmap?.Dispose();
        _cachedBitmap = null;
        _cachedPath = path;

        var bitmap = BackgroundImageLoader.Load(path, out var error);
        if (bitmap is null && error is not null)
        {
            Log.Warn(error);
        }

        _cachedBitmap = bitmap;
        return bitmap;
    }

    /// <summary>
    /// 释放缓存的背景位图，返回是否真的释放了（18 篇 §3 的 L0 档）。
    /// </summary>
    /// <remarks>
    /// 只有「位图没有贴在控件上」时才敢 Dispose：Avalonia 的渲染线程会直接用画笔里的位图，
    /// 提前释放等于把已销毁的对象交给渲染器。所以正在显示时这里返回 <c>false</c>，
    /// 等换图（<see cref="EnsureBitmap"/>）或关窗（<see cref="Dispose"/>）时再放。
    /// 换到纯色/渐变来源后旧位图本来就没人用了，<see cref="Apply"/> 会顺手调本方法收掉。
    /// </remarks>
    public bool TryReleaseCachedBitmap()
    {
        var bitmap = _cachedBitmap;
        if (bitmap is null)
        {
            return false;
        }

        var image = Find<Image>(ImageControlName);
        var layer = Find<Border>(LayerControlName);
        var tiledSource = layer?.Background is ImageBrush { Source: { } source } ? source : null;
        if (IsReferencedBy(bitmap, image?.Source, tiledSource))
        {
            Log.Debug("背景位图仍在被控件引用，本次释放跳过。");
            return false;
        }

        bitmap.Dispose();
        _cachedBitmap = null;
        _cachedPath = null;
        Log.Info("已释放缓存的背景位图。");
        return true;
    }

    /// <summary>
    /// 判断位图是否仍被界面引用（引用比对，不比较像素）。
    /// </summary>
    /// <remarks>
    /// 抽成静态方法是为了能在无头测试里验证：无头宿主没有渲染后端，造不出真的 <see cref="Bitmap"/>，
    /// 而「谁还拿着它」只用到引用相等，用任意替身对象就能验。参数取 <see cref="object"/>
    /// 也是同一个原因：<c>ImageBrush.Source</c> 的静态类型 <c>IImageBrushSource</c> 由平台实现、
    /// 用户代码无法构造替身，所以由调用方把两端都摊平成 <see cref="object"/> 再比。
    /// </remarks>
    /// <param name="bitmap">缓存里的位图。</param>
    /// <param name="imageSource">图片层的当前图源。</param>
    /// <param name="tiledSource">平铺画笔的图源。</param>
    public static bool IsReferencedBy(object bitmap, object? imageSource, object? tiledSource) =>
        (imageSource is not null && ReferenceEquals(imageSource, bitmap))
        || (tiledSource is not null && ReferenceEquals(tiledSource, bitmap));

    /// <summary>
    /// 装饰场景的可见性：系统高对比度优先级最高（THM-10），其次是用户自选的背景（不该被装饰层盖住）。
    /// 星空与四个程序化场景互斥，同一时刻最多只画一层。
    /// </summary>
    /// <remarks>
    /// 刻意不把场景做成 <see cref="BackgroundProfile"/> 的一种 Kind：那会把「用户选的背景」和
    /// 「应用自带的装饰层」混进同一个枚举，高对比度、无背景、按实例覆盖三条路径都得各判一次，更容易漏。
    /// </remarks>
    private void ApplySceneVisibility(bool highContrast, bool customBackgroundActive, string? sceneKey)
    {
        var visible = !highContrast && !customBackgroundActive;
        var scene = BackgroundSceneKinds.Normalize(sceneKey);

        var starfield = Find<StarfieldBackground>(StarfieldControlName);
        if (starfield is not null)
        {
            starfield.IsVisible = visible && BackgroundSceneKinds.IsStarfield(scene);
        }

        var canvas = Find<BackgroundSceneCanvas>(SceneControlName);
        if (canvas is null)
        {
            return;
        }

        canvas.Scene = scene;
        canvas.IsVisible = visible && BackgroundSceneKinds.IsSceneCanvas(scene);
    }

    private static void ClearLayers(Border layer, Image image, Border dim, Border scrim)
    {
        layer.Background = null;
        layer.Effect = null;
        layer.Opacity = 1;
        image.Source = null;
        image.Effect = null;
        image.Opacity = 1;
        dim.Background = null;
        dim.Opacity = 0;
        scrim.Opacity = 0;
    }

    /// <summary>图片类来源（本机图片 / 游戏截图）共用同一条位图渲染路径。</summary>
    private static bool IsImageKind(string? kind) =>
        string.Equals(kind, BackgroundProfile.KindImage, StringComparison.OrdinalIgnoreCase)
        || string.Equals(kind, BackgroundProfile.KindScreenshot, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 把来源解析成真正要加载的图片路径：截图来源每次现取最新一张，其余直接用配置里的路径。
    /// </summary>
    private string? ResolveImagePath(BackgroundProfile profile) =>
        ResolveImagePathFor(profile, SafeCurrentGameDir());

    /// <summary>
    /// <see cref="ResolveImagePath"/> 的纯函数版（可测）：把「来源 + 游戏目录」映射成图片路径。
    /// </summary>
    /// <remarks>
    /// 截图来源不落盘路径，每次按游戏目录现找最新一张；因此调用方必须把返回值一起算进
    /// 缓存签名，否则切了实例但配置没变时会命中旧签名、背景不换。
    /// </remarks>
    public static string? ResolveImagePathFor(BackgroundProfile? profile, string? gameDirectory)
    {
        if (profile is null)
        {
            return null;
        }

        return string.Equals(profile.Kind, BackgroundProfile.KindScreenshot, StringComparison.OrdinalIgnoreCase)
            ? BackgroundImageLocator.FindLatestScreenshot(gameDirectory)
            : profile.ImagePath;
    }

    /// <summary>当前实例的游戏根目录；没有实例或读取失败都返回 null。</summary>
    private string? SafeCurrentGameDir()
    {
        try
        {
            return _instances?.Current?.GameDir;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string BuildSignature(BackgroundProfile profile, string? imagePath) => string.Join(
        '|',
        profile.Kind,
        profile.Color,
        profile.GradientStart,
        profile.GradientEnd,
        profile.GradientAngle.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
        imagePath,
        profile.Fill,
        profile.DimPercent.ToString(System.Globalization.CultureInfo.InvariantCulture),
        profile.BlurRadius.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
        profile.OpacityPercent.ToString(System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>退订并释放缓存的位图。</summary>
    public void Dispose()
    {
        if (_installed)
        {
            _settings.Changed -= OnSettingsChanged;
            _application.ActualThemeVariantChanged -= OnThemeVariantChanged;
            if (_instances is not null)
            {
                _instances.CurrentChanged -= OnCurrentInstanceChanged;
            }

            _installed = false;
        }

        _cachedBitmap?.Dispose();
        _cachedBitmap = null;
        _cachedPath = null;
    }
}
