using Avalonia;

using CkanUI.Services.Abstractions;
using CkanUI.ViewModels.Pages.Settings;

using log4net;

namespace CkanUI.App.Theming;

/// <summary>
/// 界面密度宿主（20 篇 P-08、THM-06）：把 <see cref="UiSettings.Density"/> 落到 App 级资源 token，
/// 并在设置保存后立即生效。
/// </summary>
/// <remarks>
/// 与 <see cref="CkanUI.App.ThemeHost"/> 同构，理由也一样：VM 层禁止引用 Avalonia（架构铁律 1），
/// 所以「读设置 → 改资源」只能落在 App 项目里，通过 <see cref="IUiSettingsStore.Changed"/> 订阅。
/// <para>
/// 实现要点：密度不是 <see cref="Avalonia.Styling.ThemeVariant"/>，没法走 ThemeDictionaries，
/// 因此直接覆盖 <see cref="Application.Resources"/> 里的同名 token ——
/// 应用字典的本地项优先于合并进来的 <c>Resources/Tokens.axaml</c>，而页面全部用 DynamicResource 引用 token，
/// 索引器赋值会触发资源变更通知，所以第二次切换也无需重启。
/// </para>
/// </remarks>
public sealed class DensityHost : IDisposable
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(DensityHost));

    /// <summary>行高 token（<c>Tokens.axaml</c> 已有，默认 28 = standard）。</summary>
    public const string RowHeightKey = "CkanRowHeight";

    /// <summary>页面内边距 token。</summary>
    public const string PagePaddingKey = "CkanPagePadding";

    /// <summary>工具栏内边距 token。</summary>
    public const string ToolbarPaddingKey = "CkanToolbarPadding";

    private readonly IUiSettingsStore _settings;
    private readonly IDispatcher _dispatcher;
    private readonly Application _application;
    private bool _installed;
    private string? _appliedDensity;

    /// <summary>构造密度宿主。</summary>
    /// <param name="settings">UI 自有设置存储（密度的唯一真源）。</param>
    /// <param name="dispatcher">UI 线程封送器（<c>Changed</c> 可能在后台线程触发）。</param>
    /// <param name="application">当前 Avalonia 应用，用于覆盖资源 token。</param>
    public DensityHost(IUiSettingsStore settings, IDispatcher dispatcher, Application application)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(application);

        _settings = settings;
        _dispatcher = dispatcher;
        _application = application;
    }

    /// <summary>从磁盘读一次设置、应用密度，并订阅后续变更。可重复调用（幂等）。</summary>
    public void Install()
    {
        if (_installed)
        {
            return;
        }

        _installed = true;
        Apply(_settings.Load());
        _settings.Changed += OnSettingsChanged;
    }

    /// <summary>密度字符串 → 行高（像素）；三档映射的唯一实现见 <see cref="DensityScale"/>。</summary>
    /// <param name="density">compact / standard / comfortable。</param>
    public static double ToRowHeight(string? density) => DensityScale.RowHeight(density);

    /// <inheritdoc />
    public void Dispose()
    {
        if (!_installed)
        {
            return;
        }

        _installed = false;
        _settings.Changed -= OnSettingsChanged;
    }

    private void OnSettingsChanged(UiSettings settings)
    {
        // 05 篇 §2：设置保存可能发生在后台线程，改 Application 资源必须回到 UI 线程。
        _dispatcher.Post(() => Apply(settings));
    }

    private void Apply(UiSettings settings)
    {
        var density = DensityScale.Normalize(settings.Density);
        if (!_installed || string.Equals(density, _appliedDensity, StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            var resources = _application.Resources;
            resources[RowHeightKey] = DensityScale.RowHeight(density);
            resources[PagePaddingKey] = new Thickness(DensityScale.PagePadding(density));
            resources[ToolbarPaddingKey] = new Thickness(
                DensityScale.ToolbarHorizontalPadding(density),
                DensityScale.ToolbarVerticalPadding(density));
            _appliedDensity = density;
        }
        catch (Exception ex)
        {
            // 订阅方异常只记日志，不能把 UI 线程带走（工程约定 §12 第 4 条）。
            Log.Warn($"应用界面密度失败：{density}", ex);
        }
    }
}
