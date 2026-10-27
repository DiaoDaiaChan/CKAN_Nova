using CkanUI.Services.Abstractions.Dtos;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CkanUI.ViewModels.Pages.Content;

/// <summary>内容树节点视图模型（支持折叠/展开与多级子节点）。</summary>
public sealed partial class ContentNodeViewModel : ObservableObject
{
    private readonly List<ContentNodeViewModel> _children = new();

    public ContentNodeViewModel(ContentEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        Entry = entry;
        var index = entry.RelativePath.LastIndexOf('/');
        Name = index < 0 ? entry.RelativePath : entry.RelativePath[(index + 1)..];
        _isExpanded = true;
    }

    public ContentEntry Entry { get; }

    /// <summary>相对 ZIP 根的正斜杠路径。</summary>
    public string RelativePath => Entry.RelativePath;

    /// <summary>当前目录或文件名。</summary>
    public string Name { get; }

    /// <summary>是否目录。</summary>
    public bool IsDirectory => Entry.IsDirectory;

    /// <summary>是否文件。</summary>
    public bool IsFile => !Entry.IsDirectory;

    /// <summary>未压缩大小（字节）。</summary>
    public long SizeBytes => Entry.SizeBytes;

    /// <summary>格式化后的大小文本。</summary>
    public string SizeText => IsDirectory ? string.Empty : ContentFormat.FormatSize(SizeBytes);

    /// <summary>类型描述。</summary>
    public string KindText => IsDirectory ? "目录" : "文件";

    /// <summary>兼容旧平铺显示属性。</summary>
    public string DisplayName => (IsDirectory ? "▸ " : "") + RelativePath + (IsDirectory ? string.Empty : "  ·  " + ContentFormat.FormatSize(SizeBytes));

    /// <summary>是否展开。</summary>
    [ObservableProperty]
    private bool _isExpanded;

    /// <summary>子节点列表。</summary>
    public IReadOnlyList<ContentNodeViewModel> Children => _children;

    internal List<ContentNodeViewModel> ChildrenList => _children;

    public void AddChild(ContentNodeViewModel child)
    {
        ArgumentNullException.ThrowIfNull(child);
        _children.Add(child);
    }

    public static ContentNodeViewModel From(ContentEntry entry) => new(entry);
}
