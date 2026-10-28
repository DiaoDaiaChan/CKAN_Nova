using System.Globalization;
using System.Text;

using CkanUI.Services.Abstractions.Commands;
using CkanUI.ViewModels.Dialogs;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CkanUI.ViewModels.CommandPalette;

/// <summary>
/// 快捷键目录对话框的视图模型：按分类列出全部快捷键，可过滤，可导出成纯文本。
/// </summary>
/// <remarks>
/// 条目有两个来源：
/// ① <see cref="ICommandCatalog.Snapshot"/> 里 <c>ShortcutText</c> 非空的命令（内置 Ctrl+K / Ctrl+/，
///    以及插件后续注册的命令）——这是「真的接线了」的那批键，不虚构未生效的全局手势；
/// ② 面板与对话框自身的导航手势（↑ ↓ Enter Esc）——它们由视图的键盘处理实现，写进目录才不误导用户。
/// 继承 <see cref="DialogViewModelBase"/> 复用对话框的关闭语义（<c>IsClosed</c> → 宿主 Close()）；
/// 「复制为文本」不直接碰剪贴板，只抛 <see cref="CopyRequested"/>，真正的剪贴板调用留给视图（视图模型层不依赖 Avalonia）。
/// </remarks>
public sealed partial class ShortcutsCatalogViewModel : DialogViewModelBase
{
    private readonly ICommandCatalog? _catalog;
    private readonly Func<string, string> _text;
    private IReadOnlyList<ShortcutEntryDto> _entries = Array.Empty<ShortcutEntryDto>();

    /// <summary>构造视图模型。</summary>
    /// <param name="catalog">命令目录；为 null 时只列面板与对话框自身的手势。</param>
    /// <param name="translate">文案翻译函数；缺省时退化为内置中文兜底。</param>
    public ShortcutsCatalogViewModel(ICommandCatalog? catalog = null, Func<string, string>? translate = null)
        : base(Resolve(translate)("Ui_Cmd_Shortcuts_Title"))
    {
        _text = Resolve(translate);
        _catalog = catalog;

        PrimaryButtonText = _text("Ui_Cmd_Shortcuts_Close");
        SecondaryButtonText = string.Empty; // 目录是只读的，只需要一个「关闭」

        Refresh();
    }

    /// <summary>请求把纯文本清单写进剪贴板；视图订阅后调用剪贴板接口。</summary>
    public event Action<string>? CopyRequested;

    /// <summary>过滤词；空串表示列出全部。</summary>
    [ObservableProperty]
    private string _filter = string.Empty;

    /// <summary>过滤后的条目（扁平列表，导出纯文本也用这个顺序）。</summary>
    [ObservableProperty]
    private IReadOnlyList<ShortcutEntryDto> _filteredEntries = Array.Empty<ShortcutEntryDto>();

    /// <summary>过滤后的分组（按分类首次出现的顺序）。</summary>
    [ObservableProperty]
    private IReadOnlyList<ShortcutGroupViewModel> _groups = Array.Empty<ShortcutGroupViewModel>();

    /// <summary>未过滤的全部条目（刷新时重建）。</summary>
    public IReadOnlyList<ShortcutEntryDto> Entries => _entries;

    /// <summary>过滤后是否还有条目。</summary>
    public bool HasEntries => FilteredEntries.Count > 0;

    /// <summary>是否显示空态提示（过滤后一条都不剩）。</summary>
    public bool ShowEmptyHint => FilteredEntries.Count == 0;

    /// <summary>过滤框占位文案。</summary>
    public string FilterWatermark => _text("Ui_Cmd_Shortcuts_FilterWatermark");

    /// <summary>空结果提示文案。</summary>
    public string EmptyHintText => _text("Ui_Cmd_Shortcuts_Empty");

    /// <summary>复制按钮文案。</summary>
    public string CopyButtonText => _text("Ui_Cmd_Shortcuts_Copy");

    /// <summary>条目总数文案（跟随过滤结果变化）。</summary>
    public string TotalCountText => FormatCount(_text("Ui_Cmd_Shortcuts_TotalCountFormat"), FilteredEntries.Count);

    /// <summary>重新收集条目并按当前过滤词刷新（目录变化后由宿主调用）。</summary>
    public void Refresh()
    {
        _entries = BuildEntries(_catalog, _text);

        var keyword = Filter is null ? string.Empty : Filter.Trim();
        var filtered = keyword.Length == 0
            ? _entries
            : _entries.Where(entry => Matches(entry, keyword)).ToArray();

        FilteredEntries = filtered;
        Groups = filtered
            .GroupBy(entry => entry.Category, StringComparer.Ordinal)
            .Select(group => new ShortcutGroupViewModel(group.Key, group.ToArray()))
            .ToArray();
    }

    /// <summary>把当前（过滤后的）目录导出成纯文本，供剪贴板、日志或问题反馈使用。</summary>
    /// <returns>多行纯文本：首行标题 + 总数，随后每类一段「[分类] + 手势 空格 说明」。</returns>
    public string BuildPlainTextReport()
    {
        var builder = new StringBuilder();
        builder.AppendLine(_text("Ui_Cmd_Shortcuts_Title"));
        builder.AppendLine(TotalCountText);
        foreach (var group in Groups)
        {
            builder.Append('[').Append(group.Category).AppendLine("]");
            foreach (var entry in group.Entries)
            {
                builder.Append(entry.Gesture).Append("    ").AppendLine(entry.Description);
            }

            builder.AppendLine();
        }

        return builder.ToString();
    }

    /// <summary>把目录抛给视图写剪贴板。</summary>
    [RelayCommand]
    private void Copy() => CopyRequested?.Invoke(BuildPlainTextReport());

    partial void OnFilterChanged(string value) => Refresh();

    partial void OnFilteredEntriesChanged(IReadOnlyList<ShortcutEntryDto> value)
    {
        OnPropertyChanged(nameof(HasEntries));
        OnPropertyChanged(nameof(ShowEmptyHint));
        OnPropertyChanged(nameof(TotalCountText));
    }

    private static bool Matches(ShortcutEntryDto entry, string keyword) =>
        Contains(entry.Gesture, keyword) || Contains(entry.Description, keyword) || Contains(entry.Category, keyword);

    private static bool Contains(string? text, string keyword) =>
        text is not null && text.Contains(keyword, StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<ShortcutEntryDto> BuildEntries(ICommandCatalog? catalog, Func<string, string> text)
    {
        var entries = new List<ShortcutEntryDto>();
        var global = text("Ui_Cmd_Category_Global");

        // 目录里只收「真的接线了」的键：ShortcutText 非空的命令。
        if (catalog is not null)
        {
            foreach (var command in catalog.Snapshot())
            {
                if (command.HasShortcut)
                {
                    entries.Add(new ShortcutEntryDto(command.ShortcutText, command.Title, global));
                }
            }
        }

        var palette = text("Ui_Cmd_Category_Palette");
        entries.Add(new ShortcutEntryDto("↑", text("Ui_Cmd_Shortcut_MoveUp"), palette));
        entries.Add(new ShortcutEntryDto("↓", text("Ui_Cmd_Shortcut_MoveDown"), palette));
        entries.Add(new ShortcutEntryDto("Enter", text("Ui_Cmd_Shortcut_Execute"), palette));
        entries.Add(new ShortcutEntryDto("Esc", text("Ui_Cmd_Shortcut_Dismiss"), palette));

        var dialog = text("Ui_Cmd_Category_ShortcutsDialog");
        entries.Add(new ShortcutEntryDto("Esc", text("Ui_Cmd_Shortcut_CloseDialog"), dialog));

        return entries;
    }

    private static string FormatCount(string format, int value) =>
        format.Contains("{0}", StringComparison.Ordinal)
            ? string.Format(CultureInfo.InvariantCulture, format, value)
            : format + " " + value.ToString(CultureInfo.InvariantCulture);

    private static Func<string, string> Resolve(Func<string, string>? translate) =>
        translate ?? (key => FallbackTexts.TryGetValue(key, out var text) ? text : key);

    private static readonly Dictionary<string, string> FallbackTexts = new(StringComparer.Ordinal)
    {
        ["Ui_Cmd_Shortcuts_Title"] = "快捷键目录",
        ["Ui_Cmd_Shortcuts_FilterWatermark"] = "搜索快捷键或动作…",
        ["Ui_Cmd_Shortcuts_Empty"] = "没有匹配的快捷键",
        ["Ui_Cmd_Shortcuts_Copy"] = "复制为文本",
        ["Ui_Cmd_Shortcuts_Close"] = "关闭",
        ["Ui_Cmd_Shortcuts_TotalCountFormat"] = "共 {0} 项",
        ["Ui_Cmd_Category_Global"] = "全局",
        ["Ui_Cmd_Category_Palette"] = "命令面板",
        ["Ui_Cmd_Category_ShortcutsDialog"] = "快捷键目录",
        ["Ui_Cmd_Shortcut_MoveUp"] = "上一条",
        ["Ui_Cmd_Shortcut_MoveDown"] = "下一条",
        ["Ui_Cmd_Shortcut_Execute"] = "执行选中命令",
        ["Ui_Cmd_Shortcut_Dismiss"] = "关闭命令面板",
        ["Ui_Cmd_Shortcut_CloseDialog"] = "关闭对话框",
    };
}
