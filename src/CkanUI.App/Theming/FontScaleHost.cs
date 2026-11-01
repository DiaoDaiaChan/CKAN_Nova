using Avalonia;

using CkanUI.Services.Abstractions;
using CkanUI.ViewModels.Pages.Settings;

using log4net;

namespace CkanUI.App.Theming;

/// <summary>
/// 字号缩放宿主（20 篇 P-09、THM-06）：把 <see cref="UiSettings.FontScale"/> 落到五个文案字号 token，
/// 设置保存后即时生效。
/// </summary>
/// <remarks>
/// 与 <see cref="DensityHost"/> 同构，理由也一样：VM 层禁止引用 Avalonia（架构铁律 1），
/// 所以「读设置 → 改资源」只能落在 App 项目里，通过 <see cref="IUiSettingsStore.Changed"/> 订阅。
/// <para>
/// 只覆盖 <c>CkanFontSize*</c> 五个文案 token：DataGrid 单元格、徽章这类数据文本用的是固定字号，
/// 不受缩放影响（20 篇 §3 原则 5、风险 R-80），避免把模组名放大到认错。
/// </para>
/// </remarks>
public sealed class FontScaleHost : IDisposable
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(FontScaleHost));

    /// <summary>小字 token（基准 12）。</summary>
    public const string CaptionKey = "CkanFontSizeCaption";

    /// <summary>正文 token（基准 13）。</summary>
    public const string BodyKey = "CkanFontSizeBody";

    /// <summary>副标题 token（基准 15）。</summary>
    public const string SubtitleKey = "CkanFontSizeSubtitle";

    /// <summary>小标题 token（基准 17）。</summary>
    public const string HeadingKey = "CkanFontSizeHeading";

    /// <summary>标题 token（基准 26）。</summary>
    public const string TitleKey = "CkanFontSizeTitle";

    /// <summary>token 与 Tokens.axaml 里的基准字号一一对应（顺序即覆盖顺序）。</summary>
    private static readonly (string Key, double BaseSize)[] Tokens =
    {
        (CaptionKey, 12),
        (BodyKey, 13),
        (SubtitleKey, 15),
        (HeadingKey, 17),
        (TitleKey, 26),
    };

    private readonly IUiSettingsStore _settings;
    private readonly IDispatcher _dispatcher;
    private readonly Application _application;
    private bool _installed;
    private string? _appliedScale;

    /// <summary>构造字号宿主。</summary>
    /// <param name="settings">UI 自有设置存储（字号的唯一真源）。</param>
    /// <param name="dispatcher">UI 线程封送器（<c>Changed</c> 可能在后台线程触发）。</param>
    /// <param name="application">当前 Avalonia 应用，用于覆盖资源 token。</param>
    public FontScaleHost(IUiSettingsStore settings, IDispatcher dispatcher, Application application)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(application);

        _settings = settings;
        _dispatcher = dispatcher;
        _application = application;
    }

    /// <summary>从磁盘读一次设置、应用字号，并订阅后续变更。可重复调用（幂等）。</summary>
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

    /// <summary>基准字号 → 缩放后的字号；映射的唯一实现见 <see cref="UiFontScale.Apply"/>。</summary>
    /// <param name="baseSize">Tokens.axaml 里的基准字号。</param>
    /// <param name="scale">90 / 100 / 110 / 120 / 130。</param>
    public static double ResolveFontSize(double baseSize, string? scale) => UiFontScale.Apply(baseSize, scale);

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
        var scale = UiFontScale.Normalize(settings.FontScale);
        if (!_installed || string.Equals(scale, _appliedScale, StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            var resources = _application.Resources;
            foreach (var (key, baseSize) in Tokens)
            {
                resources[key] = ResolveFontSize(baseSize, scale);
            }

            _appliedScale = scale;
        }
        catch (Exception ex)
        {
            // 订阅方异常只记日志，不能把 UI 线程带走（工程约定 §12 第 4 条）。
            Log.Warn($"应用字号缩放失败：{scale}", ex);
        }
    }
}
