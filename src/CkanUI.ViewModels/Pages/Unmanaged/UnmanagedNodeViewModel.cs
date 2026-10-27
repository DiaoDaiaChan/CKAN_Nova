using CkanUI.Services.Abstractions;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CkanUI.ViewModels.Pages.Unmanaged;

/// <summary>
/// 未管理文件树的一个节点（03 篇 §2 V-07）。目录节点带递归统计：受管子项计数大于 0 时不允许整目录删除。
/// </summary>
/// <remarks>展开状态与勾选状态都是可观察属性；树本身由 <see cref="UnmanagedTreeBuilder"/> 一次性拼好后整体替换。</remarks>
public sealed partial class UnmanagedNodeViewModel : ObservableObject
{
    private readonly List<UnmanagedNodeViewModel> _children = new();

    /// <summary>由一个扫描项构造节点。</summary>
    public UnmanagedNodeViewModel(UnmanagedFileInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        Info = info;
        var index = info.RelativePath.LastIndexOf('/');
        Name = index < 0 ? info.RelativePath : info.RelativePath[(index + 1)..];
    }

    /// <summary>扫描结果原始投影（相对路径、尺寸、时间、受管子项计数）。</summary>
    public UnmanagedFileInfo Info { get; }

    /// <summary>节点显示名（路径最后一段）。</summary>
    public string Name { get; }

    /// <summary>相对 GameData 的正斜杠路径。</summary>
    public string RelativePath => Info.RelativePath;

    /// <summary>是否目录。</summary>
    public bool IsDirectory => Info.IsDirectory;

    /// <summary>是否文件。</summary>
    public bool IsFile => !Info.IsDirectory;

    /// <summary>尺寸字节数（目录为未管理文件的递归合计）。</summary>
    public long SizeBytes => Info.SizeBytes;

    /// <summary>目录下（递归）已受管文件数；文件恒为 0。</summary>
    public int ManagedChildCount => Info.ManagedChildCount;

    /// <summary>目录下（递归）未受管文件数；文件恒为 1。</summary>
    public int UnmanagedChildCount => Info.UnmanagedChildCount;

    /// <summary>最后修改时间（UTC）。</summary>
    public DateTime? LastWriteTimeUtc => Info.LastWriteTimeUtc;

    /// <summary>子节点（目录为空时为只读空表）。</summary>
    public IReadOnlyList<UnmanagedNodeViewModel> Children => _children;

    /// <summary>是否有子节点。</summary>
    public bool HasChildren => _children.Count > 0;

    /// <summary>是否展开（与 TreeViewItem.IsExpanded 双向绑定）。</summary>
    [ObservableProperty]
    private bool _isExpanded;

    /// <summary>是否勾选（多选删除用）。</summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>尺寸文案。</summary>
    public string SizeText => UnmanagedFormat.FormatSize(SizeBytes);

    /// <summary>修改时间文案。</summary>
    public string ModifiedText => UnmanagedFormat.FormatTime(LastWriteTimeUtc);

    /// <summary>类型文案。</summary>
    public string KindText => IsDirectory ? "目录" : "文件";

    /// <summary>受管内容提示：混着受管文件的目录要明确标出来，避免误删。</summary>
    public string ManagedText => IsDirectory
        ? (ManagedChildCount > 0 ? $"含受管 {ManagedChildCount} 项" : "无受管内容")
        : "未管理";

    /// <summary>是否含受管内容（含则不可删除）。</summary>
    public bool HasManagedChildren => IsDirectory && ManagedChildCount > 0;

    /// <summary>是否允许删除：文件可以，目录必须不含受管子项。</summary>
    public bool CanDelete => !HasManagedChildren;

    /// <summary>猜出的模块标识符（DLL 才有）。</summary>
    public string? GuessIdentifier => Info.GuessIdentifier;

    /// <summary>是否有可猜出的标识符。</summary>
    public bool HasGuessIdentifier => !string.IsNullOrEmpty(Info.GuessIdentifier);

    internal List<UnmanagedNodeViewModel> ChildrenList => _children;

    internal void AddChild(UnmanagedNodeViewModel child)
    {
        ArgumentNullException.ThrowIfNull(child);
        _children.Add(child);
    }

    internal void ReplaceChildren(IEnumerable<UnmanagedNodeViewModel> children)
    {
        ArgumentNullException.ThrowIfNull(children);
        _children.Clear();
        _children.AddRange(children);
        OnPropertyChanged(nameof(HasChildren));
    }

    /// <summary>递归展开自身与所有后代目录。</summary>
    public void ExpandAll()
    {
        if (IsDirectory) IsExpanded = true;
        foreach (var child in _children) child.ExpandAll();
    }

    /// <summary>递归折叠自身与所有后代目录。</summary>
    public void CollapseAll()
    {
        if (IsDirectory) IsExpanded = false;
        foreach (var child in _children) child.CollapseAll();
    }

    /// <summary>自身 + 全部后代（先序）。</summary>
    public IEnumerable<UnmanagedNodeViewModel> Walk()
    {
        yield return this;
        foreach (var child in _children)
        {
            foreach (var descendant in child.Walk()) yield return descendant;
        }
    }
}
