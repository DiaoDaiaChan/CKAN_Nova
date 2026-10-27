namespace CkanUI.Localization;

/// <summary>语言下拉条目：本地名 + 英文名 + 覆盖率（16 篇 §4.5、§6.1；显示格式见 03 篇 §17.3）。</summary>
public sealed record LocaleInfo
{
    /// <summary>创建语言条目。</summary>
    /// <param name="name">BCP-47 语言标识。</param>
    /// <param name="nativeName">本地名（如 简体中文），避免乱码环境里选不出来。</param>
    /// <param name="englishName">英文名（如 Chinese, Simplified）。</param>
    /// <param name="coverage">相对回退语言（en-US）的键覆盖率，0..1。</param>
    /// <param name="isComplete">覆盖率是否满 100%。</param>
    public LocaleInfo(string name, string nativeName, string englishName, double coverage, bool isComplete)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        NativeName = string.IsNullOrWhiteSpace(nativeName) ? name : nativeName;
        EnglishName = string.IsNullOrWhiteSpace(englishName) ? name : englishName;
        Coverage = Math.Clamp(coverage, 0d, 1d);
        IsComplete = isComplete;
    }

    /// <summary>BCP-47 语言标识。</summary>
    public string Name { get; }

    /// <summary>本地名。</summary>
    public string NativeName { get; }

    /// <summary>英文名。</summary>
    public string EnglishName { get; }

    /// <summary>键覆盖率（0..1）。</summary>
    public double Coverage { get; }

    /// <summary>是否已完整翻译。</summary>
    public bool IsComplete { get; }

    /// <summary>覆盖率百分比（取整，避免依赖区域设置的小数格式）。</summary>
    public int CoveragePercent => (int)Math.Round(Coverage * 100d, MidpointRounding.AwayFromZero);

    /// <summary>"简体中文 · Chinese, Simplified"。</summary>
    public string DisplayName => $"{NativeName} · {EnglishName}";

    /// <summary>"简体中文 · Chinese, Simplified · 100%"（03 篇 §17.3 规定格式）。</summary>
    public string DisplayText => $"{DisplayName} · {CoveragePercent}%";
}
