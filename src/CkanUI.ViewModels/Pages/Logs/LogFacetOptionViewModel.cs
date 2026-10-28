using CkanUI.Services.Abstractions.Logs;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CkanUI.ViewModels.Pages.Logs;

/// <summary>
/// 过滤条里一个可勾选项的公共形状（级别/族/源三段共用一个父类型，
/// 这样过滤视图模型只需订阅基类的属性变化，不必为三段各写一套联动逻辑）。
/// </summary>
public abstract partial class LogFacetOptionViewModel : ObservableObject
{
    /// <summary>用显示名与初始勾选状态构造。</summary>
    protected LogFacetOptionViewModel(string label, bool isSelected)
    {
        Label = label;
        _isSelected = isSelected;
    }

    /// <summary>显示名（中文）。</summary>
    public string Label { get; }

    /// <summary>是否被勾选。</summary>
    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>级别选项（14 篇 §4.2① 第一段）。</summary>
public sealed class LogLevelOptionViewModel(LogLevel level, string glyph, bool isSelected)
    : LogFacetOptionViewModel(LogGlyphs.LevelLabel(level), isSelected)
{
    /// <summary>对应的级别。</summary>
    public LogLevel Level { get; } = level;

    /// <summary>字形前缀，勾选框旁显示，与列表行保持同一套语义。</summary>
    public string Glyph { get; } = glyph;
}

/// <summary>日志族选项（14 篇 §4.2① 第三段）。</summary>
public sealed class LogFamilyOptionViewModel(LogFamily family, bool isSelected)
    : LogFacetOptionViewModel(LogGlyphs.FamilyLabel(family), isSelected)
{
    /// <summary>对应的日志族。</summary>
    public LogFamily Family { get; } = family;
}

/// <summary>采集来源选项（14 篇 §4.2① 第二段）。</summary>
public sealed class LogSourceOptionViewModel(LogSourceKind kind, bool isSelected)
    : LogFacetOptionViewModel(LogGlyphs.SourceLabel(kind), isSelected)
{
    /// <summary>对应的采集通道。</summary>
    public LogSourceKind Kind { get; } = kind;
}
