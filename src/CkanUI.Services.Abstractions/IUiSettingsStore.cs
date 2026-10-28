using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.Services.Abstractions;

/// <summary>UI 自有设置（%LocalAppData%/CKAN/ckan-ui.json，01 篇 §7）。窗口几何、列宽、上次页面、主题。</summary>
public sealed record UiSettings
{
    public double WindowWidth { get; init; } = 1280;
    public double WindowHeight { get; init; } = 800;
    public double? WindowX { get; init; }
    public double? WindowY { get; init; }
    public bool IsMaximized { get; init; }
    public string LastPage { get; init; } = "mods";
    public string Theme { get; init; } = "dark";

    /// <summary>界面密度：compact / standard / comfortable（03 篇 §21、20 篇 P-08）。</summary>
    public string? Density { get; init; } = "standard";

    /// <summary>强调色（20 篇 P-03）：default(跟随系统) / 预设名(blue, purple...) / #RRGGBB 十六进制。</summary>
    public string? AccentColor { get; init; }

    /// <summary>
    /// 按实例覆盖的强调色（20 篇 P-07）：键是实例名，值是 <see cref="AccentColor"/> 的同一套取值。
    /// 没有键表示「跟随全局」，不是「没有强调色」。
    /// </summary>
    public IReadOnlyDictionary<string, string> AccentByInstance { get; init; } =
        new Dictionary<string, string>();

    /// <summary>圆角缩放档位（20 篇 P-05）：none(0x) / standard(1x) / round(1.5x)。</summary>
    public string? RadiusScale { get; init; } = "standard";

    /// <summary>动效强度（20 篇 P-10）：full(完全动画) / reduced(减少动画) / none(无动画)。</summary>
    public string? MotionIntensity { get; init; } = "auto";

    /// <summary>窗口材质背景（20 篇 P-04）：auto / mica / acrylic / none。</summary>
    public string? BackdropKind { get; init; } = "auto";

    /// <summary>字号缩放（20 篇 P-09）：90 / 100 / 110 / 120 / 130（百分比），只作用于界面文案。</summary>
    public string? FontScale { get; init; } = "100";

    /// <summary>界面背景（20 篇 P-06）：纯色 / 渐变 / 本机图片，含填充、暗化、模糊、不透明度。</summary>
    public BackgroundProfile? Background { get; init; }

    /// <summary>
    /// 按实例覆盖的背景（20 篇 P-07）：键是实例名，值是该实例专用的整份背景设置。
    /// 没有键表示「跟随全局」，不是「没有背景」。
    /// </summary>
    public IReadOnlyDictionary<string, BackgroundProfile> BackgroundByInstance { get; init; } =
        new Dictionary<string, BackgroundProfile>();

    /// <summary>界面语言（BCP-47）。UI 语言的唯一真源，不回写 Core 的 config.json（见工程约定 §12）。</summary>
    public string? Locale { get; init; }

    /// <summary>
    /// 启动游戏后的资源释放档位（18 篇 §7）。默认均衡档（18 篇 §15 Q1），激进档需用户显式开启。
    /// 存的是枚举，UiSettingsStore 的 JSON 选项没有枚举转换器，因此按数字落盘、往返稳定。
    /// </summary>
    public ResourceReleaseTier ReleaseTier { get; init; } = ResourceReleaseTier.Balanced;

    /// <summary>版本号显示：隐藏 epoch 前缀（12 篇 F-54）。</summary>
    public bool HideVersionEpoch { get; init; }

    /// <summary>版本号显示：隐藏 "v" 前缀（12 篇 F-54）。</summary>
    public bool HideVersionVPrefix { get; init; }

    /// <summary>更新列表是否自动排序（12 篇 F-54）。</summary>
    public bool AutoSortUpdates { get; init; }

    /// <summary>推荐对话框「总是取消全选」的记忆位（12 篇 F-35）。</summary>
    public bool SuppressRecommendations { get; init; }

    /// <summary>最小化到托盘（12 篇 F-51）：最小化时只留托盘图标，不占任务栏。默认关闭。</summary>
    public bool MinimizeToTray { get; init; }

    /// <summary>关闭到托盘（12 篇 F-51）：点关闭按钮时隐藏窗口而不退出进程。默认关闭。</summary>
    public bool CloseToTray { get; init; }

    public IReadOnlyDictionary<string, double> ColumnWidths { get; init; } =
        new Dictionary<string, double>();

    // record 合成的 Equals 对 ColumnWidths 走引用比较（IReadOnlyDictionary 不是 record 友好的值类型），
    // 会让「内容完全相同的两份设置」判为不等。这里改成按列名序数比较的结构相等：
    // 一是让测试与 UI 层能可靠断言默认值，二是给后续「设置未变更就跳过落盘」的脏检查用。
    public bool Equals(UiSettings? other) =>
        other is not null
        && WindowWidth.Equals(other.WindowWidth)
        && WindowHeight.Equals(other.WindowHeight)
        && Nullable.Equals(WindowX, other.WindowX)
        && Nullable.Equals(WindowY, other.WindowY)
        && IsMaximized == other.IsMaximized
        && string.Equals(LastPage, other.LastPage, StringComparison.Ordinal)
        && string.Equals(Theme, other.Theme, StringComparison.Ordinal)
        && string.Equals(Density, other.Density, StringComparison.Ordinal)
        && string.Equals(Locale, other.Locale, StringComparison.Ordinal)
        && string.Equals(AccentColor, other.AccentColor, StringComparison.Ordinal)
        && StringsEqual(AccentByInstance, other.AccentByInstance)
        && string.Equals(RadiusScale, other.RadiusScale, StringComparison.Ordinal)
        && string.Equals(MotionIntensity, other.MotionIntensity, StringComparison.Ordinal)
        && string.Equals(BackdropKind, other.BackdropKind, StringComparison.Ordinal)
        && string.Equals(FontScale, other.FontScale, StringComparison.Ordinal)
        && Equals(Background, other.Background)
        && BackgroundsEqual(BackgroundByInstance, other.BackgroundByInstance)
        && ReleaseTier == other.ReleaseTier
        && HideVersionEpoch == other.HideVersionEpoch
        && HideVersionVPrefix == other.HideVersionVPrefix
        && AutoSortUpdates == other.AutoSortUpdates
        && SuppressRecommendations == other.SuppressRecommendations
        && MinimizeToTray == other.MinimizeToTray
        && CloseToTray == other.CloseToTray
        && ColumnWidthsEqual(ColumnWidths, other.ColumnWidths);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(WindowWidth);
        hash.Add(WindowHeight);
        hash.Add(WindowX);
        hash.Add(WindowY);
        hash.Add(IsMaximized);
        hash.Add(LastPage, StringComparer.Ordinal);
        hash.Add(Theme, StringComparer.Ordinal);
        hash.Add(Density, StringComparer.Ordinal);
        hash.Add(Locale, StringComparer.Ordinal);
        hash.Add(AccentColor, StringComparer.Ordinal);
        hash.Add(RadiusScale, StringComparer.Ordinal);
        hash.Add(MotionIntensity, StringComparer.Ordinal);
        hash.Add(BackdropKind, StringComparer.Ordinal);
        hash.Add(FontScale, StringComparer.Ordinal);
        hash.Add(Background);
        hash.Add(ReleaseTier);
        hash.Add(HideVersionEpoch);
        hash.Add(HideVersionVPrefix);
        hash.Add(AutoSortUpdates);
        hash.Add(SuppressRecommendations);
        hash.Add(MinimizeToTray);
        hash.Add(CloseToTray);

        // 字典无序：逐项哈希异或，保证顺序无关。
        var columns = 0;
        foreach (var pair in ColumnWidths)
        {
            columns ^= HashCode.Combine(pair.Key, pair.Value);
        }

        foreach (var pair in BackgroundByInstance)
        {
            columns ^= HashCode.Combine(pair.Key, pair.Value);
        }

        foreach (var pair in AccentByInstance)
        {
            columns ^= HashCode.Combine(pair.Key, pair.Value, 7);
        }

        hash.Add(columns);
        return hash.ToHashCode();
    }

    private static bool BackgroundsEqual(
        IReadOnlyDictionary<string, BackgroundProfile> left,
        IReadOnlyDictionary<string, BackgroundProfile> right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null || left.Count != right.Count)
        {
            return false;
        }

        foreach (var pair in left)
        {
            if (!right.TryGetValue(pair.Key, out var value) || !Equals(pair.Value, value))
            {
                return false;
            }
        }

        return true;
    }

    private static bool StringsEqual(
        IReadOnlyDictionary<string, string> left,
        IReadOnlyDictionary<string, string> right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null || left.Count != right.Count)
        {
            return false;
        }

        foreach (var pair in left)
        {
            if (!right.TryGetValue(pair.Key, out var value) || !string.Equals(pair.Value, value, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ColumnWidthsEqual(
        IReadOnlyDictionary<string, double> left,
        IReadOnlyDictionary<string, double> right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null || left.Count != right.Count)
        {
            return false;
        }

        foreach (var pair in left)
        {
            if (!right.TryGetValue(pair.Key, out var value) || !value.Equals(pair.Value))
            {
                return false;
            }
        }

        return true;
    }
}

public interface IUiSettingsStore
{
    UiSettings Current { get; }

    /// <summary>从磁盘加载；文件缺失或损坏时回退默认值（不抛异常）。</summary>
    UiSettings Load();

    /// <summary>保存成功后触发，携带新值。供主题/密度/语言这类「写完就要生效」的订阅方使用（03 篇 §21）。</summary>
    event Action<UiSettings>? Changed;

    /// <summary>原子写回（临时文件 + 替换）。</summary>
    OperationResult<bool> Save(UiSettings settings);
}
