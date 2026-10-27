using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.ViewModels.Dialogs.Mods;

namespace CkanUI.ViewModels.Dialogs;

/// <summary>
/// F-36 冲突/不一致详情对话框：把 RelationshipResolver 给出的冲突理由逐条列出（文本直接用 Core 已本地化的串）。
/// 只读对话框——主按钮是"复制全部"（不关闭），次按钮"关闭"（对齐 ErrorDialogViewModel 的按钮语义）。
/// </summary>
public sealed partial class ConflictDetailsDialogViewModel : DialogViewModelBase
{
    private readonly Func<string, Task>? _copyTextAsync;

    /// <summary>用请求数据构造对话框；<paramref name="copyTextAsync"/> 为剪贴板写入委托（测试可注入）。</summary>
    public ConflictDetailsDialogViewModel(
        ConflictDetailsRequest request,
        Func<string, Task>? copyTextAsync = null)
        : base("冲突与不一致")
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Conflicts);

        _copyTextAsync = copyTextAsync;
        Message = request.Message;
        IsBlocking = request.IsBlocking;
        PrimaryButtonText = "复制全部";
        SecondaryButtonText = "关闭";

        foreach (var entry in request.Conflicts)
        {
            Rows.Add(new ConflictRowViewModel(entry));
        }

        foreach (var warning in request.Warnings ?? Array.Empty<string>())
        {
            if (!string.IsNullOrWhiteSpace(warning))
            {
                Warnings.Add(warning);
            }
        }
    }

    /// <summary>对话框顶部说明。</summary>
    public string Message { get; }

    /// <summary>是否属于会阻止提交的硬冲突。</summary>
    public bool IsBlocking { get; }

    /// <summary>严重程度说明文案。</summary>
    public string SeverityText => IsBlocking
        ? "这些冲突会阻止安装提交，请先解决。"
        : "以下为提示性不一致，不影响提交。";

    /// <summary>冲突条目。</summary>
    public List<ConflictRowViewModel> Rows { get; } = [];

    /// <summary>附加警告（例如推荐模组被跳过）。</summary>
    public List<string> Warnings { get; } = [];

    /// <summary>是否有冲突条目。</summary>
    public bool HasConflicts => Rows.Count > 0;

    /// <summary>是否有警告。</summary>
    public bool HasWarnings => Warnings.Count > 0;

    /// <summary>警告文案（每行一条）。</summary>
    public string WarningsText => string.Join(Environment.NewLine, Warnings);

    /// <summary>冲突条数。</summary>
    public int ConflictCount => Rows.Count;

    /// <summary>摘要文案。</summary>
    public string SummaryText => HasConflicts ? "共 " + ConflictCount + " 条冲突。" : "没有冲突记录。";

    /// <summary>冲突文案拼接（与上游 string.Join("; ", ConflictDescriptions) 一致）。</summary>
    public string ConflictsText => string.Join("; ", Rows.Select(r => r.Description));

    /// <summary>复制到剪贴板的完整文本（说明 + 冲突 + 警告）。</summary>
    public string CopyText
    {
        get
        {
            var lines = new List<string> { Message };
            foreach (var row in Rows)
            {
                lines.Add(row.HasModule ? "· " + row.Description + "（" + row.ModuleText + "）" : "· " + row.Description);
            }

            foreach (var warning in Warnings)
            {
                lines.Add("· " + warning);
            }

            return string.Join(Environment.NewLine, lines);
        }
    }

    /// <summary>复制按钮可用性（没有内容时不复制）。</summary>
    public bool CanCopy => HasConflicts || HasWarnings;

    /// <summary>复制结果提示（成功/失败）。</summary>
    [ObservableProperty]
    private string? _copyStatus;

    /// <summary>是否有复制结果提示。</summary>
    public bool HasCopyStatus => !string.IsNullOrEmpty(CopyStatus);

    /// <summary>用户是否已通过"关闭"按钮确认（对话框本身没有业务返回值）。</summary>
    public bool Acknowledged { get; private set; }

    /// <summary>复制不关闭对话框。</summary>
    protected override bool CloseAfterPrimary => false;

    /// <summary>把全部内容写入剪贴板。</summary>
    [RelayCommand]
    private async Task CopyAsync()
    {
        if (!CanCopy)
        {
            ValidationError = "没有可复制的冲突记录。";
            return;
        }

        if (_copyTextAsync is null)
        {
            CopyStatus = "当前环境不支持复制。";
            return;
        }

        try
        {
            await _copyTextAsync(CopyText);
            CopyStatus = "已复制到剪贴板。";
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or IOException or UnauthorizedAccessException)
        {
            CopyStatus = "复制失败: " + ex.Message;
        }
    }

    /// <inheritdoc />
    protected override bool Validate()
    {
        if (!CanCopy)
        {
            ValidationError = "没有可复制的冲突记录。";
            return false;
        }

        ValidationError = null;
        return true;
    }

    /// <inheritdoc />
    protected override void OnSecondary() => Acknowledged = true;

    partial void OnCopyStatusChanged(string? value) => OnPropertyChanged(nameof(HasCopyStatus));
}
