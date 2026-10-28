using System.Globalization;

namespace CkanUI.Localization;

/// <summary>
/// 字符串本地化契约（16 篇 §4.1）。解析顺序：当前语言 → 父语言（zh-CN→zh）→ en-US → key 本身；
/// 回退到 key 时记 Warn 日志并按 key 累计计数，供缺译清单与覆盖率使用。
/// 线程安全：<see cref="SetCulture"/> 整体替换解析快照，<see cref="Get"/> 无锁读取，可并发调用。
/// </summary>
public interface ILocalizer
{
    /// <summary>当前语言标识（BCP-47，如 zh-CN）。</summary>
    string CurrentLocale { get; }

    /// <summary>当前语言对应的 CultureInfo。</summary>
    CultureInfo CurrentCulture { get; }

    /// <summary>语言切换完成事件；在调用 <see cref="SetCulture"/> 的线程上触发，UI 侧需自行封送到 UI 线程。</summary>
    event EventHandler<CultureInfo>? LanguageChanged;

    /// <summary>随包语言列表（含覆盖率），供设置页语言下拉使用（16 篇 §4.5、03 篇 §17.3）。</summary>
    IReadOnlyList<LocaleInfo> AvailableLocales { get; }

    /// <summary>缺 key 统计：key → 命中次数（进程内累计，不因切语言清零）。</summary>
    IReadOnlyDictionary<string, int> MissingKeys { get; }

    /// <summary>
    /// 清空缺译与告警诊断表，返回清掉的条目数（18 篇 §3 的 L1 档）。
    /// </summary>
    /// <remarks>
    /// 这两张表是进程内唯一会无界增长的本地化状态（每个没命中的 key 都留一行），
    /// 而它们只服务于「缺译清单」这个诊断功能，清空后下一次取译文会重新累计——
    /// 所以释放它不影响任何界面文案。默认实现不做事，便于只读的测试替身。
    /// </remarks>
    int ClearDiagnostics() => 0;

    /// <summary>取译文；解析链全未命中时返回 key 本身并记 Warn。</summary>
    string Get(string key);

    /// <summary>取译文并按 <see cref="CultureInfo.CurrentCulture"/> 做占位符格式化；模板坏掉时返回原文而不抛。</summary>
    string Format(string key, params object?[] args);

    /// <summary>探测解析链是否命中（不含"返回 key"的兜底）。</summary>
    bool TryGet(string key, out string value);

    /// <summary>切换语言（不重启、不改代码，16 篇 §6.2 / Q2 决策）。</summary>
    /// <param name="locale">BCP-47 语言标识；未随包时记 Warn 并只走回退链，不抛异常。</param>
    void SetCulture(string locale);

    /// <summary>当前语言已解析出的全部键值（已应用回退链），供 App 一次性构建 DynamicResource 资源字典。</summary>
    IReadOnlyDictionary<string, string> Snapshot();
}
