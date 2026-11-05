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

    /// <summary>
    /// 主题皮肤（日冕金 / 月白石墨 / 青瓷 / 铜金 / 星云紫 / 单色银）。
    /// 只在深色主题下生效；浅色与高对比度继续使用基础主题，保证极端对比度下的可读性不被皮肤破坏。
    /// </summary>
    public string ThemeSkin { get; init; } = ThemeSkinKinds.Default;

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

    /// <summary>
    /// 页面缩放比例（1.0 = 100%，75%–150%）：底栏滑块驱动，缩放整个页面承载区（文字、图标、间距一起变），
    /// 与只动字号的 <see cref="FontScale"/> 是两件事，可叠加。外壳（标题栏/侧栏/状态栏）不缩放。
    /// </summary>
    public double PageZoom { get; init; } = 1.0;

    /// <summary>AI 排障接口地址（OpenAI 兼容，例如 https://api.deepseek.com/v1）。空表示没配。</summary>
    public string? AiBaseUrl { get; init; }

    /// <summary>AI 排障模型名（例如 deepseek-chat）。</summary>
    public string? AiModel { get; init; }

    /// <summary>
    /// AI 排障的 API Key，已由 <c>ISecretProtector</c>（Windows 上是 DPAPI）加密成 Base64。
    /// 绝不存明文：设置文件会被备份、同步、贴进 issue。
    /// </summary>
    public string? AiApiKeyProtected { get; init; }

    /// <summary>界面背景（20 篇 P-06）：纯色 / 渐变 / 本机图片，含填充、暗化、模糊、不透明度。</summary>
    public BackgroundProfile? Background { get; init; }

    /// <summary>
    /// 按实例覆盖的背景（20 篇 P-07）：键是实例名，值是该实例专用的整份背景设置。
    /// 没有键表示「跟随全局」，不是「没有背景」。
    /// </summary>
    public IReadOnlyDictionary<string, BackgroundProfile> BackgroundByInstance { get; init; } =
        new Dictionary<string, BackgroundProfile>();

    /// <summary>
    /// 应用自带的程序化背景场景（星空 / 轨道蓝图 / 月面晨昏线 / 深空探测器 / 引力透镜）。
    /// 与 <see cref="Background"/> 分开：用户自选背景生效时，装饰场景整体让位；
    /// 高对比度下同样不画（THM-10）。空值按星空处理，保证旧配置升级后仍有背景。
    /// </summary>
    public string BackgroundScene { get; init; } = BackgroundSceneKinds.Starfield;

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

    /// <summary>模组页是否显示右侧详情面板（折叠后表格铺满全屏，展示更多模组）。默认开启。</summary>
    public bool ShowModDetailsPane { get; init; } = true;

    /// <summary>
    /// 首次启动指引是否已走完（02 篇 §1 Ⅱ-⑪）。置位后不再自动弹向导；
    /// 「设置 → 重新运行首次启动指引」仍可手动打开（只删本标志，不动其他设置）。
    /// </summary>
    public bool FirstRunCompleted { get; init; }

    /// <summary>
    /// 是否已展示过首次启动高亮指引（介绍软件界面与核心模块）。默认 false。
    /// </summary>
    public bool HasSeenOnboardingTour { get; init; }

    /// <summary>下载代理模式：direct / system / manual（与 <c>ProxyMode</c> 的 JSON 名一一对应，小写存储）。</summary>
    public string ProxyMode { get; init; } = "system";

    /// <summary>手动代理地址（<c>http://主机:端口</c>）；非 manual 模式下保留但不用。</summary>
    public string? ProxyServer { get; init; }

    /// <summary>代理分流列表（逗号/分号分隔），仅手动模式使用。</summary>
    public string? ProxyBypass { get; init; }

    /// <summary>
    /// 导航栏悬浮交互模式：
    /// "tabPopout" - 光标滑过单个 Tab 自动弹出独立卡片（默认）
    /// "drawer" - 光标滑过侧边栏自动抽屉式展开整个侧边栏
    /// </summary>
    public string NavHoverMode { get; init; } = "tabPopout";

    /// <summary>是否开启音效（选中、勾选、错误、警告、ok 等）。默认开启。</summary>
    public bool SoundEffectsEnabled { get; init; } = true;

    /// <summary>音效音量（0.0 - 1.0）。默认 0.8。</summary>
    public double SoundEffectsVolume { get; init; } = 0.8;

    /// <summary>是否开启背景音乐播放。默认开启。</summary>
    public bool MusicEnabled { get; init; } = true;

    /// <summary>音乐音量（0.0 - 1.0）。默认 0.7。</summary>
    public double MusicVolume { get; init; } = 0.7;

    /// <summary>全局静音（小喇叭快速静音开关）。默认 false。</summary>
    public bool MasterMuted { get; init; }

    public IReadOnlyDictionary<string, double> ColumnWidths { get; init; } =
        new Dictionary<string, double>();

    /// <summary>
    /// 主页部件布局：按顺序存放部件 id（<c>instance</c> / <c>quick-actions</c> / <c>screenshots</c> /
    /// <c>plugin:&lt;插件Id&gt;:&lt;部件键&gt;</c>）。
    /// </summary>
    /// <remarks>
    /// <para><c>null</c> 表示「用户还没动过」——宿主用默认布局，而不是当成「一个部件都不要」；
    /// 空数组才是用户明确清空。这两种状态必须分开，否则升级后主页会突然空掉。</para>
    /// <para>停用插件时它的 id 仍留在列表里：重新启用后回到原来的位置，而不是被挤到末尾。</para>
    /// </remarks>
    public IReadOnlyList<string>? HomeWidgets { get; init; }

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
        && string.Equals(ThemeSkin, other.ThemeSkin, StringComparison.Ordinal)
        && string.Equals(Density, other.Density, StringComparison.Ordinal)
        && string.Equals(Locale, other.Locale, StringComparison.Ordinal)
        && string.Equals(AccentColor, other.AccentColor, StringComparison.Ordinal)
        && StringsEqual(AccentByInstance, other.AccentByInstance)
        && string.Equals(RadiusScale, other.RadiusScale, StringComparison.Ordinal)
        && string.Equals(MotionIntensity, other.MotionIntensity, StringComparison.Ordinal)
        && string.Equals(BackdropKind, other.BackdropKind, StringComparison.Ordinal)
        && string.Equals(FontScale, other.FontScale, StringComparison.Ordinal)
        && PageZoom.Equals(other.PageZoom)
        && Equals(Background, other.Background)
        && BackgroundsEqual(BackgroundByInstance, other.BackgroundByInstance)
        && string.Equals(BackgroundScene, other.BackgroundScene, StringComparison.Ordinal)
        && ReleaseTier == other.ReleaseTier
        && HideVersionEpoch == other.HideVersionEpoch
        && HideVersionVPrefix == other.HideVersionVPrefix
        && AutoSortUpdates == other.AutoSortUpdates
        && SuppressRecommendations == other.SuppressRecommendations
        && MinimizeToTray == other.MinimizeToTray
        && CloseToTray == other.CloseToTray
        && ShowModDetailsPane == other.ShowModDetailsPane
        && FirstRunCompleted == other.FirstRunCompleted
        && HasSeenOnboardingTour == other.HasSeenOnboardingTour
        && string.Equals(ProxyMode, other.ProxyMode, StringComparison.Ordinal)
        && string.Equals(ProxyServer, other.ProxyServer, StringComparison.Ordinal)
        && string.Equals(ProxyBypass, other.ProxyBypass, StringComparison.Ordinal)
        && string.Equals(NavHoverMode, other.NavHoverMode, StringComparison.Ordinal)
        && string.Equals(AiBaseUrl, other.AiBaseUrl, StringComparison.Ordinal)
        && string.Equals(AiModel, other.AiModel, StringComparison.Ordinal)
        && string.Equals(AiApiKeyProtected, other.AiApiKeyProtected, StringComparison.Ordinal)
        && SoundEffectsEnabled == other.SoundEffectsEnabled
        && SoundEffectsVolume.Equals(other.SoundEffectsVolume)
        && MusicEnabled == other.MusicEnabled
        && MusicVolume.Equals(other.MusicVolume)
        && MasterMuted == other.MasterMuted
        && HomeWidgetsEqual(HomeWidgets, other.HomeWidgets)
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
        hash.Add(ThemeSkin, StringComparer.Ordinal);
        hash.Add(Density, StringComparer.Ordinal);
        hash.Add(Locale, StringComparer.Ordinal);
        hash.Add(AccentColor, StringComparer.Ordinal);
        hash.Add(RadiusScale, StringComparer.Ordinal);
        hash.Add(MotionIntensity, StringComparer.Ordinal);
        hash.Add(BackdropKind, StringComparer.Ordinal);
        hash.Add(FontScale, StringComparer.Ordinal);
        hash.Add(PageZoom);
        hash.Add(Background);
        hash.Add(BackgroundScene, StringComparer.Ordinal);
        hash.Add(ReleaseTier);
        hash.Add(HideVersionEpoch);
        hash.Add(HideVersionVPrefix);
        hash.Add(AutoSortUpdates);
        hash.Add(SuppressRecommendations);
        hash.Add(MinimizeToTray);
        hash.Add(CloseToTray);
        hash.Add(ShowModDetailsPane);
        hash.Add(FirstRunCompleted);
        hash.Add(HasSeenOnboardingTour);
        hash.Add(ProxyMode, StringComparer.Ordinal);
        hash.Add(ProxyServer, StringComparer.Ordinal);
        hash.Add(ProxyBypass, StringComparer.Ordinal);
        hash.Add(NavHoverMode, StringComparer.Ordinal);
        hash.Add(AiBaseUrl, StringComparer.Ordinal);
        hash.Add(AiModel, StringComparer.Ordinal);
        hash.Add(AiApiKeyProtected, StringComparer.Ordinal);
        hash.Add(SoundEffectsEnabled);
        hash.Add(SoundEffectsVolume);
        hash.Add(MusicEnabled);
        hash.Add(MusicVolume);
        hash.Add(MasterMuted);
        if (HomeWidgets is not null)
        {
            foreach (var widget in HomeWidgets)
            {
                hash.Add(widget, StringComparer.Ordinal);
            }
        }

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

    private static bool HomeWidgetsEqual(IReadOnlyList<string>? left, IReadOnlyList<string>? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null || left.Count != right.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Count; index++)
        {
            if (!string.Equals(left[index], right[index], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
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

    /// <summary>保存成功后触发别名。</summary>
    event Action<UiSettings>? Saved
    {
        add => Changed += value;
        remove => Changed -= value;
    }

    /// <summary>原子写回（临时文件 + 替换）。</summary>
    OperationResult<bool> Save(UiSettings settings);
}