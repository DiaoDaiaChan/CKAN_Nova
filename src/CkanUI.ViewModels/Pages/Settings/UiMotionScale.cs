namespace CkanUI.ViewModels.Pages.Settings;

/// <summary>动效时长的三个档位（对应 03 篇 §12 的 <c>Motion.Fast/Base/Page</c>）。</summary>
public enum MotionSpeed
{
    /// <summary>快速反馈：悬停、按下、勾选（90ms）。</summary>
    Fast = 0,

    /// <summary>常规过渡：背景/边框变色、展开收起（150ms）。</summary>
    Base = 1,

    /// <summary>页面级切换（200ms）。</summary>
    Page = 2,
}

/// <summary>
/// 动效强度（20 篇 P-10 / THM-07）：完整 / 精简 / 关闭，默认跟随系统「减少动画」。
/// </summary>
/// <remarks>
/// <para>语义来自 20 篇 §11：<b>精简</b>只保留 90ms 的快速反馈，去掉页面切换与列表增删动画；
/// <b>关闭</b>则全部瞬时，但状态变化仍要靠静态指示可感知（选中态、进度条不打转）。</para>
/// <para><c>auto</c> 是「跟随系统」：系统开了动画等于完整，系统关了等于精简——不是关闭，
/// 因为精简档的 90ms 反馈本身是「减少动画」用户也接受的量级。</para>
/// </remarks>
public static class UiMotionScale
{
    /// <summary>跟随系统。</summary>
    public const string Auto = "auto";

    /// <summary>完整动效。</summary>
    public const string Full = "full";

    /// <summary>精简动效。</summary>
    public const string Reduced = "reduced";

    /// <summary>关闭动效。</summary>
    public const string None = "none";

    /// <summary>四档（顺序即界面下拉框顺序）。</summary>
    public static readonly string[] All = { Auto, Full, Reduced, None };

    /// <summary>快速反馈时长（毫秒）：精简档也保留这一档。</summary>
    public const int FastMilliseconds = 90;

    /// <summary>常规过渡时长（毫秒）。</summary>
    public const int BaseMilliseconds = 150;

    /// <summary>页面切换时长（毫秒）。</summary>
    public const int PageMilliseconds = 200;

    /// <summary>归一化写入值；不认识的取值退回 <c>auto</c>（而不是报错或写成脏值）。</summary>
    public static string Normalize(string? intensity)
    {
        var value = intensity?.Trim().ToLowerInvariant();
        return value switch
        {
            Full => Full,
            Reduced => Reduced,
            None => None,
            _ => Auto,
        };
    }

    /// <summary>解析出实际生效的档位（<c>auto</c> 依系统设置落地）。</summary>
    /// <param name="intensity">用户设置值。</param>
    /// <param name="systemAnimationsEnabled">系统是否允许动画（Windows <c>SPI_GETCLIENTAREAANIMATION</c>）。</param>
    public static string Resolve(string? intensity, bool systemAnimationsEnabled) =>
        Normalize(intensity) switch
        {
            Full => Full,
            Reduced => Reduced,
            None => None,
            _ => systemAnimationsEnabled ? Full : Reduced,
        };

    /// <summary>某档位的实际时长（毫秒）。</summary>
    /// <param name="intensity">用户设置值。</param>
    /// <param name="systemAnimationsEnabled">系统是否允许动画。</param>
    /// <param name="speed">时长档位。</param>
    public static int Milliseconds(string? intensity, bool systemAnimationsEnabled, MotionSpeed speed) =>
        Resolve(intensity, systemAnimationsEnabled) switch
        {
            None => 0,
            Reduced => speed == MotionSpeed.Fast ? FastMilliseconds : 0,
            _ => speed switch
            {
                MotionSpeed.Fast => FastMilliseconds,
                MotionSpeed.Base => BaseMilliseconds,
                _ => PageMilliseconds,
            },
        };

    /// <summary>某档位的实际时长（供资源 token 直接落地）。</summary>
    public static TimeSpan Duration(string? intensity, bool systemAnimationsEnabled, MotionSpeed speed) =>
        TimeSpan.FromMilliseconds(Milliseconds(intensity, systemAnimationsEnabled, speed));

    /// <summary>页面级切换是否允许（精简与关闭档都去掉页面动画）。</summary>
    public static bool PageTransitionsEnabled(string? intensity, bool systemAnimationsEnabled) =>
        Milliseconds(intensity, systemAnimationsEnabled, MotionSpeed.Page) > 0;
}
