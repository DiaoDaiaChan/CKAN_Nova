namespace CkanUI.ViewModels.Pages.Settings;

/// <summary>
/// 界面密度档位到尺寸的映射（20 篇 P-08；三档只影响行高与内边距，不影响字号）。
/// </summary>
/// <remarks>
/// 放在 ViewModels 而不是 App 层，是因为 <c>UiSettings.Density</c> 属于 VM 层的数据，
/// 而 App 层的 <c>DensityHost</c> 只负责把这里的数字写进资源字典 —— 这样映射本身可 headless 断言，
/// 也不会让 VM 反向引用 Avalonia（架构铁律）。
/// 行高与 <c>src/CkanUI.App/Resources/Tokens.axaml</c> 的 <c>CkanRowHeight</c>（默认 28 = standard）一致。
/// </remarks>
public static class DensityScale
{
    /// <summary>紧凑档。</summary>
    public const string Compact = "compact";

    /// <summary>标准档（缺省）。</summary>
    public const string Standard = "standard";

    /// <summary>宽松档。</summary>
    public const string Comfortable = "comfortable";

    /// <summary>紧凑档行高。</summary>
    public const double CompactRowHeight = 24;

    /// <summary>标准档行高。</summary>
    public const double StandardRowHeight = 28;

    /// <summary>宽松档行高。</summary>
    public const double ComfortableRowHeight = 32;

    /// <summary>把任意字符串规整成三档之一（未知值回落 standard，与设置页的加载回退一致）。</summary>
    /// <param name="density">原始取值。</param>
    public static string Normalize(string? density)
    {
        if (string.IsNullOrWhiteSpace(density))
        {
            return Standard;
        }

        if (string.Equals(density, Compact, StringComparison.OrdinalIgnoreCase))
        {
            return Compact;
        }

        if (string.Equals(density, Comfortable, StringComparison.OrdinalIgnoreCase))
        {
            return Comfortable;
        }

        return Standard;
    }

    /// <summary>档位对应的行高（像素）。</summary>
    /// <param name="density">原始取值。</param>
    public static double RowHeight(string? density) => Normalize(density) switch
    {
        Compact => CompactRowHeight,
        Comfortable => ComfortableRowHeight,
        _ => StandardRowHeight,
    };

    /// <summary>档位对应的页面内边距（像素）。</summary>
    /// <param name="density">原始取值。</param>
    public static double PagePadding(string? density) => Normalize(density) switch
    {
        Compact => 12,
        Comfortable => 20,
        _ => 16,
    };

    /// <summary>档位对应的工具栏横向内边距（像素）。</summary>
    /// <param name="density">原始取值。</param>
    public static double ToolbarHorizontalPadding(string? density) => Normalize(density) switch
    {
        Compact => 8,
        Comfortable => 16,
        _ => 12,
    };

    /// <summary>档位对应的工具栏纵向内边距（像素）。</summary>
    /// <param name="density">原始取值。</param>
    public static double ToolbarVerticalPadding(string? density) => Normalize(density) switch
    {
        Compact => 4,
        Comfortable => 12,
        _ => 8,
    };
}
