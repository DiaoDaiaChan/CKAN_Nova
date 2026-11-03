using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CkanUI.Plugin.Saves;

/// <summary>
/// SFS 工作台（SFS-05/06/07 的界面侧）：分区大纲 + 搜索 + 就地改值 / 复制 / 删除。
/// </summary>
/// <remarks>
/// <para>与存档页的分工：存档页负责“选哪份存档、体检、整体格式化”，本窗口负责“进去改具体内容”。
/// 解析、语义投影、写入全在 <see cref="ISaveEditorService"/> 里，本类只编排状态与命令，不碰文件系统。</para>
/// <para>不硬编码字段：字段来自词表投影，未知键（整合包私有数据）原样列在后面，一样可编辑。</para>
/// <para>破坏性操作（删除/复制）一律先“上膛”再确认，且服务层写前快照 + 原子写；结果用它自己的
/// 语义自校验（节点数/键值多重集）兜底，校验不过就不落盘。</para>
/// </remarks>
public sealed partial class SfsWorkbenchViewModel : ObservableObject
{
    private readonly ISaveEditorService _editor;
    private readonly string _filePath;
    private SaveOutlineDto? _outline;

    /// <summary>构造工作台：<paramref name="filePath"/> 是被打开的那份 .sfs。</summary>
    public SfsWorkbenchViewModel(string filePath, ISaveEditorService editor)
    {
        _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
    }

    /// <summary>设计器/预览用：不连服务，调用任何命令都只给提示。</summary>
    public SfsWorkbenchViewModel()
        : this(string.Empty, new NullSaveEditorService())
    {
    }

    /// <summary>窗口标题。</summary>
    [ObservableProperty]
    private string _title = "存档工作台";

    /// <summary>副标题：模式 / 版本 / 规模。</summary>
    [ObservableProperty]
    private string _summaryText = string.Empty;

    /// <summary>常用内容（资金/科技/声望/时间）。</summary>
    [ObservableProperty]
    private IReadOnlyList<SaveOutlineFieldDto> _highlights = [];

    /// <summary>全部展示分区，已按“常用在前”排序。</summary>
    [ObservableProperty]
    private IReadOnlyList<SfsSectionItem> _sections = [];

    /// <summary>当前分区。</summary>
    [ObservableProperty]
    private SfsSectionItem? _selectedSection;

    /// <summary>当前列出的键值（分区字段或展开块的字段）。</summary>
    [ObservableProperty]
    private IReadOnlyList<SaveOutlineFieldDto> _fields = [];

    /// <summary>当前选中的键值行。</summary>
    [ObservableProperty]
    private SaveOutlineFieldDto? _selectedField;

    /// <summary>值输入框：选中字段时载入原始值。</summary>
    [ObservableProperty]
    private string _editValue = string.Empty;

    /// <summary>键名输入框：整合包写错键名时在 GUI 里直接改。</summary>
    [ObservableProperty]
    private string _keyEdit = string.Empty;

    /// <summary>当前列出的子块（飞行器/部件/场景）。</summary>
    [ObservableProperty]
    private IReadOnlyList<SaveOutlineNodeDto> _nodes = [];

    /// <summary>当前选中的块。</summary>
    [ObservableProperty]
    private SaveOutlineNodeDto? _selectedNode;

    /// <summary>面包屑：当前展开的是哪个块。</summary>
    [ObservableProperty]
    private string _breadcrumbText = string.Empty;

    /// <summary>搜索关键字。</summary>
    [ObservableProperty]
    private string _searchText = string.Empty;

    /// <summary>搜索命中（键值/块/原文三类）。</summary>
    [ObservableProperty]
    private IReadOnlyList<SaveOutlineMatchDto> _matches = [];

    /// <summary>是否处于搜索结果视图。</summary>
    [ObservableProperty]
    private bool _isSearchResult;

    /// <summary>忙碌。</summary>
    [ObservableProperty]
    private bool _isBusy;

    /// <summary>状态栏（成功路径）。</summary>
    [ObservableProperty]
    private string _statusText = string.Empty;

    /// <summary>错误提示。</summary>
    [ObservableProperty]
    private string _errorText = string.Empty;

    /// <summary>删除确认文案；非空时显示确认条。</summary>
    [ObservableProperty]
    private string _pendingDeleteText = string.Empty;

    /// <summary>当前存档是否走流式大文件模式（不建整棵语法树）。</summary>
    [ObservableProperty]
    private bool _isLargeFileMode;

    /// <summary>大文件模式说明（为什么改用流式、哪些操作受限）。</summary>
    [ObservableProperty]
    private string _largeFileNote = string.Empty;

    /// <summary>超出展开预算的超大块（只统计，不展开）。</summary>
    [ObservableProperty]
    private IReadOnlyList<SaveOutlineNodeDto> _oversizedBlocks = [];

    /// <summary>字段编辑区是否可用。</summary>
    public bool CanEditField => SelectedField is not null && !IsBusy;

    /// <summary>块操作是否可用。大文件模式不支持整块复制/删除（要搬动未知长度的大段内容）。</summary>
    public bool CanTouchNode => SelectedNode is not null && !IsBusy && !IsLargeFileMode;

    /// <summary>展开是否可用：大文件模式也允许按需展开，只看有没有选中。</summary>
    public bool CanExpandNode => SelectedNode is not null && !IsBusy;

    /// <summary>大文件横幅是否可见。</summary>
    public bool HasLargeFileNote => LargeFileNote.Length > 0;

    /// <summary>有没有超大块提示。</summary>
    public bool HasOversizedBlocks => OversizedBlocks.Count > 0;

    /// <summary>超大块提示：超过单块扫描预算的块只统计、不展开，避免界面以为数据丢了。</summary>
    public string OversizedBlocksNote => OversizedBlocks.Count switch
    {
        0 => string.Empty,
        1 => $"另有 1 个超大块（{OversizedBlocks[0].Name}，第 {OversizedBlocks[0].Line} 行）超过单块扫描预算，只统计不展开。",
        _ => $"另有 {OversizedBlocks.Count} 个超大块（{string.Join("、", OversizedBlocks.Take(3).Select(block => block.Name))} 等）超过单块扫描预算，只统计不展开。",
    };

    /// <summary>有没有常用内容卡片。</summary>
    public bool HasHighlights => Highlights.Count > 0;

    /// <summary>错误条是否可见。</summary>
    public bool HasError => ErrorText.Length > 0;

    /// <summary>状态栏是否可见。</summary>
    public bool HasStatus => StatusText.Length > 0;

    /// <summary>删除确认条是否可见。</summary>
    public bool HasPendingDelete => PendingDeleteText.Length > 0;

    /// <summary>搜索命中数量文案。</summary>
    public string MatchCountText => Matches.Count == 0 ? "没有命中" : $"命中 {Matches.Count} 处";

    /// <summary>页头资金（沙盒模式没有这行，显示破折号而不是空框）。</summary>
    public string FundsText => StatText("funds");

    /// <summary>页头科技点。</summary>
    public string ScienceText => StatText("sci");

    /// <summary>页头声望。</summary>
    public string ReputationText => StatText("rep");

    /// <summary>页头游戏时间。</summary>
    public string GameTimeText => StatText("UT");

    /// <summary>
    /// 按词表键名取常用内容的展示值。
    /// 为什么按键名查而不是按下标取 <see cref="Highlights"/>：不同存档类型高亮的条目数不一样，
    /// 按下标会在沙盒存档上错位成“科技显示声望值”。
    /// </summary>
    private string StatText(string key)
    {
        foreach (var field in Highlights)
        {
            if (string.Equals(field.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                return field.DisplayValue;
            }
        }

        return "—";
    }

    /// <summary>
    /// 加载（或重新加载）整份存档的大纲。
    /// <paramref name="keepLine"/> 用于“改完值以后回到原来那行”：重新解析后行号会整体位移，
    /// 不按行号回选的话用户刚改的东西会从视野里消失。
    /// </summary>
    public async Task InitializeAsync(int? keepLine = null)
    {
        if (string.IsNullOrWhiteSpace(_filePath))
        {
            ErrorText = "没有指定存档路径。";
            return;
        }

        IsBusy = true;
        ErrorText = string.Empty;
        StatusText = "正在解析存档…";
        try
        {
            var result = await _editor.LoadOutlineAsync(_filePath).ConfigureAwait(true);
            if (!result.IsSuccess || result.Value is null)
            {
                ErrorText = result.Message ?? "解析存档失败";
                StatusText = string.Empty;
                return;
            }

            var outline = result.Value;
            _outline = outline;
            Title = string.IsNullOrEmpty(outline.SaveName) ? Path.GetFileName(_filePath) : outline.SaveName;
            SummaryText = string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"{outline.Mode} · v{outline.Version} · {outline.VesselCount} 艘 / {outline.PartCount} 部件 / {outline.ScenarioCount} 场景 / {outline.CrewCount} 乘员 · {outline.PairCount} 键值 · {outline.LineCount} 行");
            Highlights = outline.Highlights;
            IsLargeFileMode = outline.LargeFileMode;
            LargeFileNote = outline.LargeFileNote;
            OversizedBlocks = outline.OversizedBlocks;

            var items = new List<SfsSectionItem>(outline.Sections.Count);
            foreach (var section in outline.Sections)
            {
                items.Add(new SfsSectionItem(section));
            }

            Sections = items;
            SelectedSection = items.FirstOrDefault();
            if (keepLine is not null)
            {
                RestoreSelection(keepLine.Value);
            }

            IsSearchResult = false;
            Matches = [];
            SearchText = string.Empty;
            StatusText = outline.LargeFileMode
                ? "已按流式模式加载：可改值和改键名，整块复制/删除请用 ckanui-sfs 命令行。"
                : outline.Truncated
                    ? "已加载；超大存档超出展示预算的部分只统计不展开。"
                    : "已加载。";
        }
        catch (Exception ex)
        {
            ErrorText = ex.Message;
            StatusText = string.Empty;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>搜索：键名 / 中文标签 / 值 / 原文行都能搜到。</summary>
    [RelayCommand]
    private async Task SearchAsync()
    {
        var query = SearchText?.Trim() ?? string.Empty;
        if (query.Length == 0)
        {
            IsSearchResult = false;
            Matches = [];
            StatusText = string.Empty;
            return;
        }

        IsBusy = true;
        ErrorText = string.Empty;
        try
        {
            var result = await _editor.SearchAsync(_filePath, query, 400).ConfigureAwait(true);
            if (!result.IsSuccess)
            {
                ErrorText = result.Message ?? "搜索失败";
                return;
            }

            Matches = result.Value ?? [];
            IsSearchResult = true;
            StatusText = MatchCountText;
        }
        catch (Exception ex)
        {
            ErrorText = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>退出搜索视图，回到分区大纲。</summary>
    [RelayCommand]
    private void ClearSearch()
    {
        SearchText = string.Empty;
        Matches = [];
        IsSearchResult = false;
        StatusText = string.Empty;
        if (SelectedSection is not null)
        {
            ShowSection(SelectedSection);
        }
    }

    /// <summary>把值写回选中的键（写前快照 + 原子写，服务层自校验）。</summary>
    [RelayCommand]
    private async Task ApplyValueAsync()
    {
        if (SelectedField is null)
        {
            StatusText = "先选中一行键值。";
            return;
        }

        var field = SelectedField;
        if (string.Equals(field.Value, EditValue, StringComparison.Ordinal))
        {
            StatusText = "值没有变化。";
            return;
        }

        IsBusy = true;
        ErrorText = string.Empty;
        try
        {
            var result = await _editor.EditAsync(new SaveEditRequest
            {
                FilePath = _filePath,
                Kind = SaveEditKind.SetValue,
                Line = field.Line,
                Value = EditValue,
                Write = true,
            }).ConfigureAwait(true);

            await ReportAsync(result, $"第 {field.Line} 行已更新", field.Line).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ErrorText = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>改键名（整合包写错键、手抖打错键名时用）。</summary>
    [RelayCommand]
    private async Task RenameKeyAsync()
    {
        if (SelectedField is null)
        {
            StatusText = "先选中一行键值。";
            return;
        }

        var field = SelectedField;
        var key = KeyEdit?.Trim() ?? string.Empty;
        if (string.Equals(field.Key, key, StringComparison.Ordinal))
        {
            StatusText = "键名没有变化。";
            return;
        }

        IsBusy = true;
        ErrorText = string.Empty;
        try
        {
            var result = await _editor.EditAsync(new SaveEditRequest
            {
                FilePath = _filePath,
                Kind = SaveEditKind.RenameKey,
                Line = field.Line,
                Key = key,
                Write = true,
            }).ConfigureAwait(true);

            await ReportAsync(result, $"第 {field.Line} 行键名已改为 {key}", field.Line).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ErrorText = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>复制选中的块（整块出现在原块后面）。</summary>
    [RelayCommand]
    private async Task DuplicateNodeAsync()
    {
        if (SelectedNode is null)
        {
            return;
        }

        if (IsLargeFileMode)
        {
            StatusText = "大文件模式不支持整块复制；请用 ckanui-sfs duplicate 命令行。";
            return;
        }

        var node = SelectedNode;
        IsBusy = true;
        ErrorText = string.Empty;
        try
        {
            var result = await _editor.EditAsync(new SaveEditRequest
            {
                FilePath = _filePath,
                Kind = SaveEditKind.DuplicateElement,
                Line = node.Line,
                Write = true,
            }).ConfigureAwait(true);

            await ReportAsync(result, $"已复制 {node.Name}（第 {node.Line} 行）").ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ErrorText = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>删除选中的块：第一次只上膛，确认后才真的删。</summary>
    [RelayCommand]
    private void ArmDelete()
    {
        if (SelectedNode is null)
        {
            return;
        }

        if (IsLargeFileMode)
        {
            StatusText = "大文件模式不支持整块删除；请用 ckanui-sfs delete 命令行。";
            return;
        }

        PendingDeleteText = $"确认删除第 {SelectedNode.Line} 行的整块 {SelectedNode.Name}（{SelectedNode.NodeCount} 个块 / {SelectedNode.PairCount} 个键值）？写前会自动留快照。";
    }

    /// <summary>取消删除。</summary>
    [RelayCommand]
    private void CancelDelete() => PendingDeleteText = string.Empty;

    /// <summary>确认删除。</summary>
    [RelayCommand]
    private async Task ConfirmDeleteAsync()
    {
        if (SelectedNode is null)
        {
            PendingDeleteText = string.Empty;
            return;
        }

        if (IsLargeFileMode)
        {
            PendingDeleteText = string.Empty;
            StatusText = "大文件模式不支持整块删除；请用 ckanui-sfs delete 命令行。";
            return;
        }

        var node = SelectedNode;
        PendingDeleteText = string.Empty;
        IsBusy = true;
        ErrorText = string.Empty;
        try
        {
            var result = await _editor.EditAsync(new SaveEditRequest
            {
                FilePath = _filePath,
                Kind = SaveEditKind.DeleteElement,
                Line = node.Line,
                Write = true,
            }).ConfigureAwait(true);

            await ReportAsync(result, $"已删除 {node.Name}（第 {node.Line} 行）").ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ErrorText = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>展开一个块（只读）：看它下面的键值与子块。</summary>
    [RelayCommand]
    private async Task ExpandNodeAsync()
    {
        if (SelectedNode is null)
        {
            return;
        }

        var node = SelectedNode;
        IsBusy = true;
        ErrorText = string.Empty;
        try
        {
            var result = await _editor.DescribeNodeAsync(_filePath, node.Line).ConfigureAwait(true);
            if (!result.IsSuccess || result.Value is null)
            {
                ErrorText = result.Message ?? "展开失败";
                return;
            }

            var detail = result.Value;
            BreadcrumbText = string.IsNullOrEmpty(detail.Path) ? detail.Title : detail.Path;
            Fields = detail.Fields;
            Nodes = detail.Nodes;
            StatusText = detail.Truncated
                ? $"{detail.Title}：内容超出展示预算，只列前一部分。"
                : $"{detail.Title}：{detail.Fields.Count} 键值 / {detail.Nodes.Count} 子块。";
        }
        catch (Exception ex)
        {
            ErrorText = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>点搜索命中：跳到原文行（能定位到就展开所在块）。</summary>
    [RelayCommand]
    private async Task OpenMatchAsync(SaveOutlineMatchDto? match)
    {
        if (match is null)
        {
            return;
        }

        IsSearchResult = false;
        IsBusy = true;
        ErrorText = string.Empty;
        try
        {
            if (await RevealLineAsync(match.Line).ConfigureAwait(true))
            {
                StatusText = $"第 {match.Line} 行：{match.DisplayValue}";
                return;
            }

            // 注释行或预算外内容：定位不到块时只报行号，不假装选中了别的内容。
            StatusText = $"第 {match.Line} 行：{match.DisplayValue}（未能定位到可编辑块）";
        }
        catch (Exception ex)
        {
            ErrorText = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 把某个行号还原成“分区 → 块 → 子块 → 字段”的可见路径。
    /// 为什么不能只按行号直接改：搜索命中可能藏在 VESSEL/PART/MODULE 深处，
    /// 不展开祖先块就没有可编辑的字段行。这里逐层按 [Line, EndLine] 区间下钻，最多 32 层。
    /// </summary>
    private async Task<bool> RevealLineAsync(int line)
    {
        if (_outline is null)
        {
            return false;
        }

        foreach (var section in Sections)
        {
            if (section.Dto.Fields.Any(field => field.Line == line))
            {
                SelectedSection = section;
                SelectedField = Fields.FirstOrDefault(field => field.Line == line);
                return SelectedField is not null;
            }
        }

        FindSectionNode(Sections, line, out var sectionWithNode, out var current);
        if (sectionWithNode is null || current is null)
        {
            return false;
        }

        SelectedSection = sectionWithNode;
        for (var depth = 0; depth < 32; depth++)
        {
            var result = await _editor.DescribeNodeAsync(_filePath, current.Line).ConfigureAwait(true);
            if (!result.IsSuccess || result.Value is null)
            {
                return false;
            }

            var detail = result.Value;
            BreadcrumbText = string.IsNullOrEmpty(detail.Path) ? detail.Title : detail.Path;
            Fields = detail.Fields;
            Nodes = detail.Nodes;

            var field = detail.Fields.FirstOrDefault(candidate => candidate.Line == line);
            if (field is not null)
            {
                SelectedField = field;
                return true;
            }

            if (detail.Line == line)
            {
                return true;
            }

            var child = FindContaining(detail.Nodes, line);
            if (child is null)
            {
                return false;
            }

            current = child;
        }

        return false;
    }

    /// <summary>
    /// 跨分区找包含目标行的顶层块：先按 [Line, EndLine] 取最小区间，区间缺失时退回起始行最近的块。
    /// 不能只拿“第一个有节点的分区”——飞行器分区在场景分区之前，会把它后面的场景块全认错。
    /// </summary>
    private static void FindSectionNode(
        IReadOnlyList<SfsSectionItem> sections,
        int line,
        out SfsSectionItem? bestSection,
        out SaveOutlineNodeDto? bestNode)
    {
        bestSection = null;
        bestNode = null;
        var bestSpan = long.MaxValue;

        SfsSectionItem? nearestSection = null;
        SaveOutlineNodeDto? nearestNode = null;

        foreach (var section in sections)
        {
            foreach (var node in section.Dto.Nodes)
            {
                if (node.Line > line)
                {
                    continue;
                }

                if (node.EndLine >= line)
                {
                    var span = (long)node.EndLine - node.Line;
                    if (span < bestSpan)
                    {
                        bestSection = section;
                        bestNode = node;
                        bestSpan = span;
                    }
                }

                if (nearestNode is null || node.Line > nearestNode.Line)
                {
                    nearestSection = section;
                    nearestNode = node;
                }
            }
        }

        bestSection ??= nearestSection;
        bestNode ??= nearestNode;
    }

    /// <summary>在子块里找包含目标行的最小区间；没有区间信息时退回“起始行最近”的块。</summary>
    private static SaveOutlineNodeDto? FindContaining(IReadOnlyList<SaveOutlineNodeDto> nodes, int line)
    {
        SaveOutlineNodeDto? best = null;
        var bestSpan = long.MaxValue;
        foreach (var node in nodes)
        {
            if (node.Line > line || node.EndLine < line)
            {
                continue;
            }

            var span = (long)node.EndLine - node.Line;
            if (span < bestSpan)
            {
                best = node;
                bestSpan = span;
            }
        }

        if (best is not null)
        {
            return best;
        }

        foreach (var node in nodes)
        {
            if (node.Line <= line && (best is null || node.Line > best.Line))
            {
                best = node;
            }
        }

        return best;
    }

    /// <summary>选中分区：列出它的字段与块。</summary>
    partial void OnSelectedSectionChanged(SfsSectionItem? value)
    {
        if (value is null)
        {
            return;
        }

        ShowSection(value);
    }

    /// <summary>选中字段：值/键名输入框载入原始内容。</summary>
    partial void OnSelectedFieldChanged(SaveOutlineFieldDto? value)
    {
        EditValue = value?.Value ?? string.Empty;
        KeyEdit = value?.Key ?? string.Empty;
        OnPropertyChanged(nameof(CanEditField));
    }

    /// <summary>选中块：刷新块操作可用性。</summary>
    partial void OnSelectedNodeChanged(SaveOutlineNodeDto? value)
    {
        OnPropertyChanged(nameof(CanTouchNode));
        OnPropertyChanged(nameof(CanExpandNode));
    }

    partial void OnHighlightsChanged(IReadOnlyList<SaveOutlineFieldDto> value)
    {
        OnPropertyChanged(nameof(HasHighlights));
        OnPropertyChanged(nameof(FundsText));
        OnPropertyChanged(nameof(ScienceText));
        OnPropertyChanged(nameof(ReputationText));
        OnPropertyChanged(nameof(GameTimeText));
    }

    partial void OnErrorTextChanged(string value) => OnPropertyChanged(nameof(HasError));

    partial void OnLargeFileNoteChanged(string value) => OnPropertyChanged(nameof(HasLargeFileNote));

    partial void OnOversizedBlocksChanged(IReadOnlyList<SaveOutlineNodeDto> value)
    {
        OnPropertyChanged(nameof(HasOversizedBlocks));
        OnPropertyChanged(nameof(OversizedBlocksNote));
    }

    partial void OnIsLargeFileModeChanged(bool value)
        => OnPropertyChanged(nameof(CanTouchNode));

    partial void OnStatusTextChanged(string value) => OnPropertyChanged(nameof(HasStatus));

    partial void OnPendingDeleteTextChanged(string value) => OnPropertyChanged(nameof(HasPendingDelete));

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanEditField));
        OnPropertyChanged(nameof(CanTouchNode));
        OnPropertyChanged(nameof(CanExpandNode));
    }

    private void ShowSection(SfsSectionItem section)
    {
        BreadcrumbText = section.Dto.Subtitle;
        Fields = section.Dto.Fields;
        Nodes = section.Dto.Nodes;
        StatusText = section.Dto.Truncated
            ? $"{section.Title}：内容超出展示预算，只列前一部分。"
            : $"{section.Title}：{section.Dto.TotalPairCount} 键值 / {section.Dto.TotalNodeCount} 块。";
    }

    /// <summary>把“改完以后回到第几行”做成回选：先切分区，再选回同一行的字段。</summary>
    private void RestoreSelection(int line)
    {
        var field = Fields.FirstOrDefault(item => item.Line == line);
        if (field is not null)
        {
            SelectedField = field;
            return;
        }

        foreach (var section in Sections)
        {
            if (!section.Dto.Fields.Any(item => item.Line == line))
            {
                continue;
            }

            SelectedSection = section;
            SelectedField = Fields.FirstOrDefault(item => item.Line == line);
            return;
        }
    }

    private async Task ReportAsync(OperationResult<SaveEditOutcomeDto> result, string successText, int? keepLine = null)
    {
        if (!result.IsSuccess || result.Value is null)
        {
            ErrorText = result.Message ?? "编辑失败";
            return;
        }

        var outcome = result.Value;
        if (!outcome.Succeeded)
        {
            ErrorText = outcome.Issues.Count > 0
                ? string.Join("；", outcome.Issues)
                : outcome.Summary;
            return;
        }

        if (!outcome.Written)
        {
            StatusText = outcome.Summary;
            return;
        }

        var message = string.IsNullOrEmpty(outcome.BackupPath)
            ? successText
            : $"{successText}（快照 {Path.GetFileName(outcome.BackupPath)}）";

        // 改完必须重新解析：行号会整体位移，继续拿旧行号改就是改错行。
        await InitializeAsync(keepLine).ConfigureAwait(true);

        // 重新解析会把状态栏刷成“已加载”，改动的结果得留在用户眼前。
        StatusText = message;
    }
}

/// <summary>分区的展示包装：界面只读 Title/Subtitle/BadgeText，编辑逻辑仍走原始 DTO。</summary>
public sealed class SfsSectionItem
{
    public SfsSectionItem(SaveOutlineSectionDto dto)
    {
        Dto = dto;
    }

    /// <summary>原始分区 DTO（字段、块、截断状态都在这里）。</summary>
    public SaveOutlineSectionDto Dto { get; }

    public string Title => Dto.Title;

    public string Subtitle => Dto.Subtitle;

    /// <summary>分区规模摘要；被预算截断时明确标出来，避免用户以为块丢了。</summary>
    public string BadgeText => Dto.Truncated
        ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{Dto.TotalPairCount} 键值 · {Dto.TotalNodeCount} 块 · 已截断")
        : string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{Dto.TotalPairCount} 键值 · {Dto.TotalNodeCount} 块");
}

/// <summary>设计器/预览用的空实现：任何调用都明确失败，不伪造数据。</summary>
public sealed class NullSaveEditorService : ISaveEditorService
{
    private const string Reason = "设计器预览模式没有存档服务。";

    public Task<OperationResult<SaveOutlineDto>> LoadOutlineAsync(string filePath, CancellationToken cancellationToken = default)
        => Task.FromResult(OperationResult<SaveOutlineDto>.Fail(Reason));

    public Task<OperationResult<IReadOnlyList<SaveOutlineMatchDto>>> SearchAsync(
        string filePath,
        string query,
        int limit = 200,
        CancellationToken cancellationToken = default)
        => Task.FromResult(OperationResult<IReadOnlyList<SaveOutlineMatchDto>>.Fail(Reason));

    public Task<OperationResult<SaveNodeDetailDto>> DescribeNodeAsync(string filePath, int line, CancellationToken cancellationToken = default)
        => Task.FromResult(OperationResult<SaveNodeDetailDto>.Fail(Reason));

    public Task<OperationResult<SaveEditOutcomeDto>> EditAsync(SaveEditRequest request, CancellationToken cancellationToken = default)
        => Task.FromResult(OperationResult<SaveEditOutcomeDto>.Fail(Reason));
}
