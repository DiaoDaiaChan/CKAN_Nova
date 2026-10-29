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
/// 物理弹簧的四个手感档位（第三阶段）：数值本身不是时长，而是「这套刚度/阻尼参数该给多长的
/// 归一化窗口」。<c>SpringEasing</c> 把 Duration 归一化成 t∈[0,1] 再解微分方程，
/// 所以同一组参数配不同 Duration 只是快慢不同，形状与收敛性不变。
/// </summary>
public enum MotionSpring
{
    /// <summary>临界阻尼（稳）：m=1 k=180 c=26，零过冲，用于阅读区域的入场落位。</summary>
    Soft = 0,

    /// <summary>微欠阻尼（跟手）：m=1 k=220 c=22，用于抽屉与滑动。</summary>
    Slide = 1,

    /// <summary>高刚度微反馈（清脆）：m=1 k=320 c=24，用于按钮与开关。</summary>
    Crisp = 2,

    /// <summary>强欠阻尼冲量（衰减震荡）：m=1 k=650 c=14 v0=30，用于校验报错轻颤。</summary>
    Impulse = 3,
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

    /// <summary>临界阻尼落位时长（毫秒）：页面卡片入场、视图层切换、大抽屉收起（档位 1）。</summary>
    public const int SpringSoftMilliseconds = 320;

    /// <summary>跟手滑动时长（毫秒）：导航胶囊滑动、底部抽屉弹出、悬浮条浮起（档位 2）。</summary>
    public const int SpringSlideMilliseconds = 360;

    /// <summary>清脆回弹时长（毫秒）：按钮松手回弹、开关切换、胶囊标签生成（档位 3）。</summary>
    public const int SpringCrispMilliseconds = 250;

    /// <summary>冲量抖动时长（毫秒）：表单必填项校验报错的衰减轻颤（档位 4）。</summary>
    public const int SpringImpulseMilliseconds = 450;

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

    /// <summary>弹簧档位的实际时长（毫秒）：精简档一律压到 90ms 的快速反馈，关闭档为 0。</summary>
    /// <param name="intensity">用户设置值。</param>
    /// <param name="systemAnimationsEnabled">系统是否允许动画。</param>
    /// <param name="spring">弹簧手感档位。</param>
    public static int Milliseconds(string? intensity, bool systemAnimationsEnabled, MotionSpring spring) =>
        Resolve(intensity, systemAnimationsEnabled) switch
        {
            None => 0,
            Reduced => FastMilliseconds,
            _ => spring switch
            {
                MotionSpring.Soft => SpringSoftMilliseconds,
                MotionSpring.Slide => SpringSlideMilliseconds,
                MotionSpring.Crisp => SpringCrispMilliseconds,
                _ => SpringImpulseMilliseconds,
            },
        };

    /// <summary>某档位的实际时长（供资源 token 直接落地）。</summary>
    public static TimeSpan Duration(string? intensity, bool systemAnimationsEnabled, MotionSpeed speed) =>
        TimeSpan.FromMilliseconds(Milliseconds(intensity, systemAnimationsEnabled, speed));

    /// <summary>弹簧档位的实际时长（供资源 token 直接落地）。</summary>
    /// <param name="intensity">用户设置值。</param>
    /// <param name="systemAnimationsEnabled">系统是否允许动画。</param>
    /// <param name="spring">弹簧手感档位。</param>
    public static TimeSpan Duration(string? intensity, bool systemAnimationsEnabled, MotionSpring spring) =>
        TimeSpan.FromMilliseconds(Milliseconds(intensity, systemAnimationsEnabled, spring));

    /// <summary>页面级切换是否允许（精简与关闭档都去掉页面动画）。</summary>
    public static bool PageTransitionsEnabled(string? intensity, bool systemAnimationsEnabled) =>
        Milliseconds(intensity, systemAnimationsEnabled, MotionSpeed.Page) > 0;
}
