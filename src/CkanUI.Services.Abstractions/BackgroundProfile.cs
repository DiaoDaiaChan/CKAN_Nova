namespace CkanUI.Services.Abstractions;

/// <summary>
/// 界面背景设置（20 篇 P-06 / THM-05）。整份存进 <c>ckan-ui.json</c>，既是全局背景，也是
/// 「仅本实例」时的按实例覆盖值（20 篇 P-07）。
/// </summary>
/// <remarks>
/// 这里只放数据，不放渲染：读图、暗化、模糊、填充方式映射全在 App 层的 <c>BackgroundHost</c> 里，
/// 因为那部分要碰 Avalonia 的 <c>Bitmap</c>/<c>ImageBrush</c>（04 篇 §1 分层铁律）。
/// </remarks>
public sealed record BackgroundProfile
{
    /// <summary>不设背景（跟随主题底色）。</summary>
    public const string KindNone = "none";

    /// <summary>纯色。</summary>
    public const string KindSolid = "solid";

    /// <summary>线性渐变。</summary>
    public const string KindGradient = "gradient";

    /// <summary>本机图片。</summary>
    public const string KindImage = "image";

    /// <summary>
    /// 当前实例的游戏截图：取 <c>&lt;GameDir&gt;/Screenshots</c> 里最新的一张（20 篇 §9.1）。
    /// </summary>
    /// <remarks>
    /// 只存来源类型、不存路径：截图文件由游戏每次按时间命名，把路径落盘会立刻过期。
    /// 真正去找文件的是 Services 层的 <c>BackgroundImageLocator</c>（纯文件系统，可 headless 测）。
    /// </remarks>
    public const string KindScreenshot = "screenshot";

    /// <summary>填充（等比铺满，可能裁切）。</summary>
    public const string FillFill = "fill";

    /// <summary>适应（等比完整显示，可能留边）。</summary>
    public const string FillFit = "fit";

    /// <summary>拉伸（不保比例铺满）。</summary>
    public const string FillStretch = "stretch";

    /// <summary>平铺（原尺寸重复）。</summary>
    public const string FillTile = "tile";

    /// <summary>居中（原尺寸不缩放）。</summary>
    public const string FillCenter = "center";

    /// <summary>来源：none / solid / gradient / image。</summary>
    public string Kind { get; init; } = KindNone;

    /// <summary>纯色背景的 #RRGGBB。</summary>
    public string? Color { get; init; } = "#1B1F27";

    /// <summary>渐变起色 #RRGGBB。</summary>
    public string? GradientStart { get; init; } = "#1B1F27";

    /// <summary>渐变止色 #RRGGBB。</summary>
    public string? GradientEnd { get; init; } = "#0B1220";

    /// <summary>渐变角度（度）：0 向右、90 向下。</summary>
    public double GradientAngle { get; init; } = 90;

    /// <summary>图片绝对路径（仅 <see cref="KindImage"/> 有效）。</summary>
    public string? ImagePath { get; init; }

    /// <summary>填充方式：fill / fit / stretch / tile / center。</summary>
    public string Fill { get; init; } = FillFill;

    /// <summary>暗化百分比（0–100）：在背景上压一层黑色，保证文字清晰。</summary>
    public int DimPercent { get; init; } = 35;

    /// <summary>模糊半径（0–64 像素）：0 表示不模糊。</summary>
    public double BlurRadius { get; init; }

    /// <summary>背景不透明度百分比（0–100）。</summary>
    public int OpacityPercent { get; init; } = 100;

    /// <summary>默认值（不启用背景）。</summary>
    public static BackgroundProfile Default { get; } = new();

    /// <summary>是否真的要画背景（<see cref="KindNone"/> 与空值都算否）。</summary>
    public bool IsActive =>
        !string.IsNullOrWhiteSpace(Kind)
        && !string.Equals(Kind, KindNone, StringComparison.OrdinalIgnoreCase);

    /// <summary>把越界的数值夹回合法区间（手改 ckan-ui.json 也过得去）。</summary>
    public BackgroundProfile Clamp() => this with
    {
        GradientAngle = double.IsFinite(GradientAngle) ? ((GradientAngle % 360) + 360) % 360 : 90,
        DimPercent = Math.Clamp(DimPercent, 0, 100),
        OpacityPercent = Math.Clamp(OpacityPercent, 0, 100),
        BlurRadius = double.IsFinite(BlurRadius) ? Math.Clamp(BlurRadius, 0, 64) : 0,
    };
}
