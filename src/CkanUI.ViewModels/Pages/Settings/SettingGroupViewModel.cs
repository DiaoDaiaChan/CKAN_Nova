namespace CkanUI.ViewModels.Pages.Settings;

/// <summary>
/// 一个设置分组（03 篇 §7 的七组）：标题 + 说明 + 设置项列表；
/// 认证令牌组的行由 <see cref="TokenEditor"/> 承载（它的数据形态与标量项不同，单独成型更清楚）。
/// </summary>
public sealed class SettingGroupViewModel
{
    /// <summary>创建一个分组。</summary>
    /// <param name="key">分组标识。</param>
    /// <param name="title">分组标题。</param>
    /// <param name="description">分组说明。</param>
    /// <param name="items">标量设置项。</param>
    /// <param name="tokenEditor">令牌组编辑器（仅认证令牌组有）。</param>
    /// <param name="diagnostics">诊断区视图模型（仅高级组有）。</param>
    public SettingGroupViewModel(
        SettingGroupKey key,
        string title,
        string? description,
        IReadOnlyList<SettingItemViewModel> items,
        AuthTokenEditorViewModel? tokenEditor = null,
        SettingsDiagnosticsViewModel? diagnostics = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(title);

        Key = key;
        Title = title;
        Description = description;
        Items = items ?? Array.Empty<SettingItemViewModel>();
        TokenEditor = tokenEditor;
        Diagnostics = diagnostics;
    }

    /// <summary>分组标识。</summary>
    public SettingGroupKey Key { get; }

    /// <summary>分组标题。</summary>
    public string Title { get; }

    /// <summary>分组说明。</summary>
    public string? Description { get; }

    /// <summary>是否有分组说明。</summary>
    public bool HasDescription => !string.IsNullOrEmpty(Description);

    /// <summary>标量设置项。</summary>
    public IReadOnlyList<SettingItemViewModel> Items { get; }

    /// <summary>令牌组编辑器；非令牌组为 null。</summary>
    public AuthTokenEditorViewModel? TokenEditor { get; }

    /// <summary>是否认证令牌组。</summary>
    public bool IsTokenGroup => TokenEditor is not null;

    /// <summary>诊断区视图模型；非高级组为 null。</summary>
    public SettingsDiagnosticsViewModel? Diagnostics { get; }

    /// <summary>是否诊断组（高级组）。</summary>
    public bool IsDiagnosticsGroup => Diagnostics is not null;

    /// <summary>是否有标量设置项。</summary>
    public bool HasItems => Items.Count > 0;

    /// <summary>本组是否有未保存修改。</summary>
    public bool HasDirty
    {
        get
        {
            foreach (var item in Items)
            {
                if (item.IsDirty)
                {
                    return true;
                }
            }

            return TokenEditor?.HasChanges ?? false;
        }
    }
}
