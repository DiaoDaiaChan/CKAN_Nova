using System.Globalization;

using CkanUI.Services.Abstractions.Commands;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CkanUI.ViewModels.CommandPalette;

/// <summary>
/// 命令面板（Ctrl+K）的视图模型：输入即搜、上下选择、回车执行。
/// </summary>
/// <remarks>
/// 数据只有一个来源：<see cref="ICommandCatalog"/>。面板本身不认任何具体命令 Id，
/// 只把选中的 Id 通过 <see cref="CommandInvoked"/> 抛给宿主去执行（导航、切主题、打开目录……）。
/// 这样「列出命令」与「执行命令」解耦，宿主执行失败也不会把面板拖进异常路径。
/// 结果整体替换、不做增量集合通知（05 篇 §4 铁律 7）；列表上限 <see cref="MaxResults"/> 条，
/// 视图用虚拟化列表承载，命令多到上千条时也不会因为一次搜索卡住。
/// </remarks>
public sealed partial class CommandPaletteViewModel : ObservableObject
{
    /// <summary>结果列表上限；被截断时界面显示「已截断」提示。</summary>
    public const int MaxResults = 50;

    private readonly ICommandCatalog _catalog;
    private readonly Func<string, string> _text;

    /// <summary>构造视图模型。</summary>
    /// <param name="catalog">命令目录（宿主启动时注册内置导航与界面动作）。</param>
    /// <param name="translate">文案翻译函数；缺省时退化为内置中文兜底。</param>
    public CommandPaletteViewModel(ICommandCatalog catalog, Func<string, string>? translate = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        _catalog = catalog;
        _text = translate ?? (key => FallbackTexts.TryGetValue(key, out var text) ? text : key);
    }

    /// <summary>用户按回车执行选中项时触发，参数是命令 Id。</summary>
    /// <remarks>先关闭面板再触发，宿主可以放心弹对话框或切页，浮层不会压在新内容上面。</remarks>
    public event Action<string>? CommandInvoked;

    /// <summary>面板是否可见。</summary>
    [ObservableProperty]
    private bool _isOpen;

    /// <summary>查询词；空串表示列出全部命令。</summary>
    [ObservableProperty]
    private string _query = string.Empty;

    /// <summary>当前选中行下标；没有结果时为 -1。</summary>
    [ObservableProperty]
    private int _selectedIndex = -1;

    /// <summary>当前结果（整体替换）。</summary>
    [ObservableProperty]
    private IReadOnlyList<CommandPaletteItemViewModel> _results = Array.Empty<CommandPaletteItemViewModel>();

    /// <summary>结果是否被 <see cref="MaxResults"/> 截断。</summary>
    [ObservableProperty]
    private bool _isCapped;

    /// <summary>是否有结果。</summary>
    public bool HasResults => Results.Count > 0;

    /// <summary>是否显示空结果提示（只在面板打开且确实输入了查询词时显示）。</summary>
    public bool ShowEmptyHint => IsOpen && !string.IsNullOrEmpty(Query) && Results.Count == 0;

    /// <summary>当前选中行；没有结果或下标越界时为 null。</summary>
    public CommandPaletteItemViewModel? SelectedItem
    {
        get
        {
            var index = SelectedIndex;
            return index >= 0 && index < Results.Count ? Results[index] : null;
        }
    }

    /// <summary>搜索框占位文案。</summary>
    public string PlaceholderText => _text("Ui_Cmd_Palette_Placeholder");

    /// <summary>空结果提示文案。</summary>
    public string EmptyHintText => _text("Ui_Cmd_Palette_Empty");

    /// <summary>底部提示行文案。</summary>
    public string FooterHintText => _text("Ui_Cmd_Palette_FooterHint");

    /// <summary>结果计数文案；被截断时追加提示。</summary>
    public string ResultCountText
    {
        get
        {
            if (Results.Count == 0)
            {
                return string.Empty;
            }

            var text = FormatCount(_text("Ui_Cmd_Palette_ResultCountFormat"), Results.Count);
            return IsCapped ? text + _text("Ui_Cmd_Palette_TruncatedSuffix") : text;
        }
    }

    /// <summary>重新查询并刷新结果；目录变化后由宿主调用。</summary>
    public void Refresh()
    {
        var matches = _catalog.Search(Query, MaxResults);
        var items = new CommandPaletteItemViewModel[matches.Count];
        for (var index = 0; index < items.Length; index++)
        {
            items[index] = new CommandPaletteItemViewModel(matches[index]);
        }

        Results = items;
        IsCapped = items.Length >= MaxResults;
        SelectedIndex = items.Length > 0 ? 0 : -1;
    }

    /// <summary>打开面板：清空查询、重算结果，默认选中第一条。</summary>
    [RelayCommand]
    private void Open()
    {
        IsOpen = true;
        Query = string.Empty;
        Refresh();
    }

    /// <summary>关闭面板并清空查询，保证下次打开是干净状态。</summary>
    [RelayCommand]
    private void Close()
    {
        if (!IsOpen && string.IsNullOrEmpty(Query))
        {
            return;
        }

        IsOpen = false;
        Query = string.Empty;
    }

    /// <summary>在开与关之间切换（Ctrl+K 的常规手感）。</summary>
    [RelayCommand]
    private void Toggle()
    {
        if (IsOpen)
        {
            Close();
        }
        else
        {
            Open();
        }
    }

    /// <summary>选中项上移一格；到顶后回绕到最后一条。</summary>
    [RelayCommand]
    private void MoveUp()
    {
        var count = Results.Count;
        if (count == 0)
        {
            SelectedIndex = -1;
            return;
        }

        var index = SelectedIndex;
        SelectedIndex = index <= 0 || index >= count ? count - 1 : index - 1;
    }

    /// <summary>选中项下移一格；到底后回绕到第一条。</summary>
    [RelayCommand]
    private void MoveDown()
    {
        var count = Results.Count;
        if (count == 0)
        {
            SelectedIndex = -1;
            return;
        }

        var index = SelectedIndex;
        SelectedIndex = index < 0 || index >= count - 1 ? 0 : index + 1;
    }

    /// <summary>执行选中项：先关闭面板，再把命令 Id 抛给宿主。</summary>
    /// <remarks>不可执行的命令（<see cref="CommandPaletteItemViewModel.IsEnabled"/> 为 false）静默忽略，不打断输入。</remarks>
    [RelayCommand]
    private void ExecuteSelected()
    {
        if (!IsOpen)
        {
            return;
        }

        var item = SelectedItem;
        if (item is null || !item.IsEnabled)
        {
            return;
        }

        var id = item.Id;
        Close();
        CommandInvoked?.Invoke(id);
    }

    partial void OnIsOpenChanged(bool value) => OnPropertyChanged(nameof(ShowEmptyHint));

    partial void OnQueryChanged(string value) => Refresh();

    partial void OnResultsChanged(IReadOnlyList<CommandPaletteItemViewModel> value)
    {
        OnPropertyChanged(nameof(HasResults));
        OnPropertyChanged(nameof(ShowEmptyHint));
        OnPropertyChanged(nameof(ResultCountText));
    }

    partial void OnSelectedIndexChanged(int value) => OnPropertyChanged(nameof(SelectedItem));

    partial void OnIsCappedChanged(bool value) => OnPropertyChanged(nameof(ResultCountText));

    private static string FormatCount(string format, int value) =>
        format.Contains("{0}", StringComparison.Ordinal)
            ? string.Format(CultureInfo.InvariantCulture, format, value)
            : format + " " + value.ToString(CultureInfo.InvariantCulture);

    private static readonly Dictionary<string, string> FallbackTexts = new(StringComparer.Ordinal)
    {
        ["Ui_Cmd_Palette_Placeholder"] = "搜索命令：页面、动作或模块名…",
        ["Ui_Cmd_Palette_Empty"] = "没有匹配的命令",
        ["Ui_Cmd_Palette_FooterHint"] = "↑↓ 选择 · Enter 执行 · Esc 关闭",
        ["Ui_Cmd_Palette_ResultCountFormat"] = "共 {0} 条",
        ["Ui_Cmd_Palette_TruncatedSuffix"] = "（结果过多，已截断）",
    };
}
