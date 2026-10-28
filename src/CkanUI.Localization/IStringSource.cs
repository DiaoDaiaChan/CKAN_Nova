namespace CkanUI.Localization;

/// <summary>
/// 语言包之外的一层字符串来源（16 篇 §3 D11 三层解析中的 L2 元数据包与 L3 Core 运行时资源）。
/// </summary>
/// <remarks>
/// 为什么不复用 <see cref="LanguagePack"/>：语言包是"随包的完整界面词典"，它参与覆盖率统计、
/// 参与回退链拼装、并会被整表塞进 DynamicResource 快照；而元数据层（模组译文）与 Core 层
/// （上游 CKAN 的 209 条资源）是按 key 惰性探测的补充来源，既不该进覆盖率分母，也不该进界面快照
/// ——否则设置页的语言覆盖率会莫名其妙地超过 100%，或者模组译文被当成界面文案混进资源字典。
/// 拆成独立接口后，<see cref="Localizer"/> 只在语言包全链未命中时才问它们，
/// 顺序天然就是任务口径的"界面语言包 → 元数据包 → 回退键"。
/// 实现约定：实例必须只读且线程安全（<see cref="Localizer.Get"/> 可被任意线程并发调用，不加锁）。
/// </remarks>
public interface IStringSource
{
    /// <summary>层名，用于诊断计数（如 meta / core）。</summary>
    string Name { get; }

    /// <summary>按当前语言探测一条字符串；没有译文时返回 false 且不抛异常。</summary>
    /// <param name="locale">当前界面语言（BCP-47）。</param>
    /// <param name="key">键名（通常是 LocalizationKeys 的常量值）。</param>
    /// <param name="value">命中的字符串。</param>
    bool TryGet(string locale, string key, out string value);
}
