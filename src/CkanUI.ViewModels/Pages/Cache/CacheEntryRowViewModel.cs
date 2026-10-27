using CkanUI.Services.Abstractions;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CkanUI.ViewModels.Pages.Cache;

/// <summary>下载缓存条目表里的一行。</summary>
/// <remarks>多选靠行上的 <see cref="IsSelected"/> 勾选，而不是列表的 SelectedItems——
/// 与未管理文件页一致，避免视图层再同步一套选中状态。</remarks>
public sealed partial class CacheEntryRowViewModel : ObservableObject
{
    private readonly Action? _selectionChanged;

    /// <summary>用契约条目构造一行。</summary>
    public CacheEntryRowViewModel(CacheEntryInfo entry, Action? selectionChanged = null)
    {
        ArgumentNullException.ThrowIfNull(entry);
        Entry = entry;
        _selectionChanged = selectionChanged;
    }

    /// <summary>该行对应的缓存条目。</summary>
    public CacheEntryInfo Entry { get; }

    /// <summary>缓存文件名。</summary>
    public string FileName => Entry.FileName;

    /// <summary>模块标签（<c>标识符 版本</c>，解析不出时是「未知模块」）。</summary>
    public string ModuleLabel => Entry.ModuleLabel;

    /// <summary>大小文案。</summary>
    public string SizeText => CacheFormat.FormatSize(Entry.SizeBytes);

    /// <summary>最后修改时间文案。</summary>
    public string ModifiedText => CacheFormat.FormatTime(Entry.ModifiedTimeUtc);

    /// <summary>是否正被已安装的模组引用（引用中的条目不会被清理）。</summary>
    public bool IsReferenced => Entry.IsReferenced;

    /// <summary>引用状态文案。</summary>
    public string ReferenceText => IsReferenced ? "使用中" : "未使用";

    /// <summary>是否被勾选。</summary>
    [ObservableProperty]
    private bool _isSelected;

    partial void OnIsSelectedChanged(bool value) => _selectionChanged?.Invoke();
}
