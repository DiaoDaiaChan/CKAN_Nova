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

    /// <summary>取日志出口：未配置时用 Trace。</summary>
    public ILocalizationLog ResolveLog() => Log ?? TraceLocalizationLog.Instance;
}
