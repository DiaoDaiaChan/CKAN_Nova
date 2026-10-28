namespace CkanUI.Localization;

/// <summary>
/// 解析层命中统计（16 篇 §4.5 缺译可见的调试端点用）。
/// </summary>
/// <remarks>
/// 与 <see cref="ILocalizer.MissingKeys"/> 分开成独立接口，是为了不给只读替身（设计时 VM、
/// 无本地化的单元测试）增加必须实现的成员：诊断端点只在调试/QA 装配下按需转型取用。
/// 计数键：pack（界面语言包命中）、fallback（语言包回退链命中）、source:层名（元数据层命中）、
/// missing（全链未命中，已计入 MissingKeys）。进程内累计，不因切语言清零，由 ClearDiagnostics 释放。
/// </remarks>
public interface ILocalizationDiagnostics
{
    /// <summary>各解析层的命中次数。</summary>
    IReadOnlyDictionary<string, int> LayerHits { get; }

    /// <summary>是否处于伪本地化模式（16 篇 §7.3）。</summary>
    bool IsPseudoLocalized { get; }
}
