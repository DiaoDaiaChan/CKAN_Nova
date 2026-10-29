using Avalonia;

using CkanUI.Platform.Theming;
using CkanUI.Services.Abstractions;
using CkanUI.ViewModels.Pages.Settings;

using log4net;

namespace CkanUI.App.Theming;

/// <summary>
/// 动效强度宿主（20 篇 P-10 / THM-07）：把三档强度落成 `CkanMotionDurationFast/Base/Page` 三个时长 token。
/// </summary>
/// <remarks>
/// <para>做法与 <see cref="DensityHost"/> 一致：改资源字典而不是改每个控件，视图里的 <c>Transition.Duration</c>
/// 全部写 <c>{DynamicResource CkanMotionDuration*}</c>，一处生效全局。</para>
/// <para>「关闭」不是把动画删掉，而是把时长写成 0：控件仍然会走一次属性切换，只是瞬间完成，
/// 选中态与进度条等静态指示依旧可感知（20 篇 §11 的「状态必须仍可感知」）。</para>
/// <para><c>auto</c> 每次生效时重新读一次系统偏好：用户可能刚在系统设置里关掉动画再切回来。</para>
/// </remarks>
public sealed class MotionHost : IDisposable
{
    /// <summary>快速反馈时长 token。</summary>
    public const string FastKey = "CkanMotionDurationFast";

    /// <summary>常规过渡时长 token。</summary>
    public const string BaseKey = "CkanMotionDurationBase";

    /// <summary>页面切换时长 token。</summary>
    public const string PageKey = "CkanMotionDurationPage";

    /// <summary>档位 1（临界阻尼落位）时长 token。</summary>
    public const string SpringSoftKey = "CkanMotionDurationSpringSoft";

    /// <summary>档位 2（跟手滑动）时长 token。</summary>
    public const string SpringSlideKey = "CkanMotionDurationSpringSlide";

    /// <summary>档位 3（清脆回弹）时长 token。</summary>
    public const string SpringCrispKey = "CkanMotionDurationSpringCrisp";

    /// <summary>档位 4（冲量轻颤）时长 token。</summary>
    public const string SpringImpulseKey = "CkanMotionDurationSpringImpulse";

    private static readonly ILog Log = LogManager.GetLogger(typeof(MotionHost));

    private readonly IUiSettingsStore _settings;
    private readonly IDispatcher _dispatcher;
    private readonly Application _application;
    private readonly Func<bool> _systemAnimations;
    private bool _installed;
    private string? _applied;

    /// <param name="settings">UI 设置存储。</param>
    /// <param name="dispatcher">UI 线程调度器（设置可能从后台线程变更）。</param>
    /// <param name="application">应用对象，资源字典的宿主。</param>
    /// <param name="systemAnimations">系统是否允许动画；为空时读系统偏好（测试可注入固定值）。</param>
    public MotionHost(
        IUiSettingsStore settings,
        IDispatcher dispatcher,
        Application application,
        Func<bool>? systemAnimations = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _application = application ?? throw new ArgumentNullException(nameof(application));
        _systemAnimations = systemAnimations ?? (static () => SystemMotionPreference.AnimationsEnabled);
    }

    /// <summary>按当前设置落地一次，并订阅后续变更。</summary>
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

    private void OnSettingsChanged(UiSettings settings)
    {
        // 设置变更可能来自后台线程（命令里保存），资源字典只能在 UI 线程改。
        _dispatcher.Post(() => Apply(settings));
    }

    private void Apply(UiSettings settings)
    {
        if (!_installed)
        {
            return;
        }

        var intensity = settings.MotionIntensity;
        var effective = UiMotionScale.Resolve(intensity, _systemAnimations());
        var signature = intensity + "|" + effective;
        if (string.Equals(signature, _applied, StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            var system = _systemAnimations();
            _application.Resources[FastKey] = UiMotionScale.Duration(intensity, system, MotionSpeed.Fast);
            _application.Resources[BaseKey] = UiMotionScale.Duration(intensity, system, MotionSpeed.Base);
            _application.Resources[PageKey] = UiMotionScale.Duration(intensity, system, MotionSpeed.Page);

            // 第三阶段的四个弹簧档位：SpringEasing 的 Duration 只决定「这套物理参数在多长窗口里跑完」，
            // 归一化终点恒为 1，所以时长可以独立于刚度/阻尼调，手感与收敛性都不受影响。
            _application.Resources[SpringSoftKey] = UiMotionScale.Duration(intensity, system, MotionSpring.Soft);
            _application.Resources[SpringSlideKey] = UiMotionScale.Duration(intensity, system, MotionSpring.Slide);
            _application.Resources[SpringCrispKey] = UiMotionScale.Duration(intensity, system, MotionSpring.Crisp);
            _application.Resources[SpringImpulseKey] = UiMotionScale.Duration(intensity, system, MotionSpring.Impulse);
            _applied = signature;
        }
        catch (Exception ex)
        {
            // 工程约定 §12 第 4 条：表现层副作用失败只记日志，不把异常抛给调用方。
            Log.Warn("动效时长 token 落地失败。", ex);
        }
    }

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
}
