namespace CkanUI.Localization;

/// <summary>三层解析：L1 新 UI 文案 → L2 官方 resx 快照 → L3 Core 资源（16 篇）。</summary>
public interface ILocalizer
{
    string Get(string key);

    string Format(string key, params object?[] args);

    string CurrentLocale { get; }
}
