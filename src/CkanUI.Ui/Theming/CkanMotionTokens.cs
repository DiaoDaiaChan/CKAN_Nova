namespace CkanUI.Ui.Theming;

/// <summary>
/// 动效时长资源键（20 篇 P-10 / THM-07）。
/// </summary>
/// <remarks>
/// <para>键名本身是资源字典契约：<c>MotionHost</c>（宿主侧）负责把三档强度写成这三个 token，
/// 而 <see cref="SkeletonPulse"/> 这类共享 UI 基元要自己去资源字典里读时长——它不能反向引用宿主。</para>
/// <para>所以键名单独放在共享层，宿主与插件用的是同一份常量；谁再手写一遍字符串，
/// 拼错的表现就是"动画永远不动"，而拼错不会报错。</para>
/// </remarks>
public static class CkanMotionTokens
{
    /// <summary>快速反馈时长 token。</summary>
    public const string Fast = "CkanMotionDurationFast";

    /// <summary>常规过渡时长 token。</summary>
    public const string Base = "CkanMotionDurationBase";

    /// <summary>页面切换时长 token。</summary>
    public const string Page = "CkanMotionDurationPage";

    /// <summary>档位 1（临界阻尼落位）时长 token。</summary>
    public const string SpringSoft = "CkanMotionDurationSpringSoft";

    /// <summary>档位 2（跟手滑动）时长 token。</summary>
    public const string SpringSlide = "CkanMotionDurationSpringSlide";

    /// <summary>档位 3（清脆回弹）时长 token。</summary>
    public const string SpringCrisp = "CkanMotionDurationSpringCrisp";

    /// <summary>档位 4（冲量轻颤）时长 token。</summary>
    public const string SpringImpulse = "CkanMotionDurationSpringImpulse";
}
