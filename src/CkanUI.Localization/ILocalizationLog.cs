namespace CkanUI.Localization;

/// <summary>
/// 本地化内部的日志出口（缺 key、语言包损坏等）。本层不依赖 log4net：
/// App 侧通过 <see cref="LocalizationOptions.Log"/> 传入自己的 Warn 实现（见 docs/ops/i18n-guide.md）。
/// </summary>
public interface ILocalizationLog
{
    /// <summary>记录一条警告。</summary>
    void Warn(string message);
}
