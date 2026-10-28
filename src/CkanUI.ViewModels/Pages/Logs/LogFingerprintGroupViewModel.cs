using System.Globalization;
using CkanUI.Services.Abstractions.Logs;

namespace CkanUI.ViewModels.Pages.Logs;

/// <summary>
/// 聚合视图的一组（14 篇 §4.2④）：连续相同指纹（级别 + 族 + 消息骨架）折叠成 ×N。
/// 刷新时按事件重算，所以是不可变的展示对象。
/// </summary>
public sealed class LogFingerprintGroupViewModel
{
    /// <summary>用服务层的指纹分组构造。</summary>
    public LogFingerprintGroupViewModel(LogFingerprintGroup group)
    {
        Group = group ?? throw new ArgumentNullException(nameof(group));
    }

    /// <summary>服务层原始分组。</summary>
    public LogFingerprintGroup Group { get; }

    /// <summary>折叠次数文本（×12）。</summary>
    public string CountText => string.Create(CultureInfo.InvariantCulture, $"×{Group.Count}");

    /// <summary>折叠次数。</summary>
    public int Count => Group.Count;

    /// <summary>级别字形。</summary>
    public string Glyph => LogGlyphs.Glyph(Group.Level);

    /// <summary>级别样式键。</summary>
    public string LevelClass => LogGlyphs.LevelClass(Group.Level);

    /// <summary>级别中文名。</summary>
    public string LevelLabel => LogGlyphs.LevelLabel(Group.Level);

    /// <summary>日志族中文名。</summary>
    public string FamilyLabel => LogGlyphs.FamilyLabel(Group.Family);

    /// <summary>代表样本（首条消息）。</summary>
    public string Sample => Group.Sample;

    /// <summary>首次出现的序号。</summary>
    public long FirstSeq => Group.FirstSeq;

    /// <summary>最近一次的序号。</summary>
    public long LastSeq => Group.LastSeq;

    /// <summary>是否错误组。</summary>
    public bool IsError => LogLevels.IsError(Group.Level);
}
