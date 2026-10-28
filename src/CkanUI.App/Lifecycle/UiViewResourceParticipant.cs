using CkanUI.App.Converters;
using CkanUI.App.Theming;
using CkanUI.Services.Abstractions;

using log4net;

namespace CkanUI.App.Lifecycle;

/// <summary>
/// L0 视图资源释放：导航字形缓存与背景位图（18 篇 §3）。
/// </summary>
/// <remarks>
/// 为什么这两样必须在 App 层：它们都是 <c>Avalonia.Media</c> 类型（<c>Geometry</c>/<c>Bitmap</c>），
/// 而且位图只能在 UI 线程释放——渲染线程可能正握着它。服务层的 L0 契约
/// （<see cref="IViewResourceParticipant"/>）只规定「数一数释放了多少」，具体怎么放由这里决定。
/// </remarks>
public sealed class UiViewResourceParticipant : IViewResourceParticipant
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(UiViewResourceParticipant));

    private readonly IDispatcher _dispatcher;

    /// <summary>构造参与者。</summary>
    /// <param name="dispatcher">用于判断当前是否在 UI 线程、必要时把释放排回 UI 线程。</param>
    public UiViewResourceParticipant(IDispatcher dispatcher)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    /// <summary>背景宿主；由 <c>App.axaml.cs</c> 在 <c>Install()</c> 之后回填，未装托盘/无主窗时为 <c>null</c>。</summary>
    public BackgroundHost? Background { get; set; }

    /// <inheritdoc />
    public string Name => "界面字形与背景位图";

    /// <inheritdoc />
    public int TrimViewResources()
    {
        // 字形几何是静态缓存，内部自带锁，任何线程都能清。
        var released = NavGlyphToGeometryConverter.ClearCache();

        if (!_dispatcher.IsOnUiThread)
        {
            // 位图不能跨线程 Dispose：排回 UI 线程执行，本次报告里就不计它的数（宁可少报，不可谎报）。
            _dispatcher.Post(() => ReleaseBackground());
            Log.Info("背景位图释放已排入 UI 线程。");
            return released;
        }

        return released + (ReleaseBackground() ? 1 : 0);
    }

    private bool ReleaseBackground()
    {
        try
        {
            return Background?.TryReleaseCachedBitmap() ?? false;
        }
        catch (Exception ex)
        {
            // 参与方的成败不该拖垮整层释放：ResourceReleaseService 会把本层记为失败，但要少一个异常面。
            Log.Warn("释放背景位图失败。", ex);
            return false;
        }
    }
}
