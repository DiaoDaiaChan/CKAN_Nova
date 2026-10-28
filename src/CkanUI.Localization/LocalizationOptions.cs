namespace CkanUI.Localization;

/// <summary>本地化装配选项（由 App 的 DI 组装根填写，本层不读配置文件）。</summary>
public sealed class LocalizationOptions
{
    /// <summary>日志出口；为空时丢弃告警（测试）或回落到 Trace（见 <see cref="ResolveLog"/>）。</summary>
    public ILocalizationLog? Log { get; set; }

    /// <summary>回退语言（任务口径：当前语言 → en-US → key 本身）。</summary>
    public string FallbackLocale { get; set; } = "en-US";

    /// <summary>初始语言；为空表示跟随系统 UI 语言，系统语言未随包时退到 zh-CN。</summary>
    public string? DefaultLocale { get; set; }

    /// <summary>
    /// 切换语言时是否同步线程的 UI Culture。开（默认）：与官方一致只设 CurrentUICulture，
    /// 数字/日期仍跟随系统区域（16 篇 §2.3、Q3 决策）。测试里常置 false 以免污染同进程的其他测试。
    /// </summary>
    public bool ApplyThreadCulture { get; set; } = true;

    /// <summary>
    /// 语言包之外的补充字符串来源，按顺序探测、先命中先返回（16 篇 §3 D11 的 L2 元数据包 / L3 Core 资源）。
    /// 为空表示只有语言包层——界面文案的常规形态；这些来源不参与覆盖率与快照。
    /// </summary>
    public IReadOnlyList<IStringSource> Sources { get; set; } = Array.Empty<IStringSource>();

    /// <summary>
    /// 伪本地化（16 篇 §7.3）：命中的译文统一包成 [!! Šéttíngš !!] 形状并加长约 30%，
    /// 未命中的键保持原样。只影响显示文本（含显示快照），不改覆盖率与缺译计数；
    /// 由环境变量 CKAN_UI_PSEUDO_LOCALE=1 打开（见 <see cref="LocalizerFactory.PseudoLocaleEnvironmentVariable"/>），发布版默认关闭。
    /// </summary>
    public bool PseudoLocalize { get; set; }

    /// <summary>取日志出口：未配置时用 Trace。</summary>
    public ILocalizationLog ResolveLog() => Log ?? TraceLocalizationLog.Instance;
}
