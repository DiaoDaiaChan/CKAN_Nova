namespace CkanUI.Services.Abstractions;

/// <summary>
/// 装饰背景场景的可选值。与 <see cref="BackgroundProfile"/> 分开：那边是用户选的
/// 纯色/渐变/图片，这边是应用自带的程序化场景，两者不能混进同一个 Kind。
/// </summary>
public static class BackgroundSceneKinds
{
    public const string Starfield = "starfield";
    public const string Orbital = "orbital";
    public const string Lunar = "lunar";
    public const string Probe = "probe";
    public const string Lens = "lens";
    public const string Corona = "corona";
    public const string RingPlane = "ring";
    public const string Aurora = "aurora";
    public const string Pulsar = "pulsar";
    public const string SolarSail = "sail";
    public const string SpaceElevator = "elevator";
    public const string None = "none";

    /// <summary>设置页下拉框的顺序；第一项是现有星空。</summary>
    public static readonly string[] All =
    [
        Starfield,
        Orbital,
        Lunar,
        Probe,
        Lens,
        Corona,
        RingPlane,
        Aurora,
        Pulsar,
        SolarSail,
        SpaceElevator,
        None
    ];

    /// <summary>把外部输入夹进已知值：空值/未知值回到星空，避免旧配置把背景画成空白。</summary>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Starfield;
        }

        foreach (var candidate in All)
        {
            if (string.Equals(candidate, value, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        return Starfield;
    }

    /// <summary>该值是否由 <see cref="StarfieldBackground"/> 负责绘制。</summary>
    public static bool IsStarfield(string? value) =>
        string.Equals(Normalize(value), Starfield, StringComparison.Ordinal);

    /// <summary>该值是否由新的程序化场景画布负责绘制。</summary>
    public static bool IsSceneCanvas(string? value)
    {
        var normalized = Normalize(value);
        return normalized is not Starfield and not None;
    }
}
