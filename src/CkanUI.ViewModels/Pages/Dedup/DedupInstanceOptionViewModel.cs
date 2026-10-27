using CkanUI.Services.Abstractions.Dtos;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CkanUI.ViewModels.Pages.Dedup;

/// <summary>去重页的实例多选项（勾选参与扫描的实例）。</summary>
public sealed partial class DedupInstanceOptionViewModel : ObservableObject
{
    /// <summary>从实例信息投影。</summary>
    public DedupInstanceOptionViewModel(InstanceInfo instance, bool isCurrent)
    {
        ArgumentNullException.ThrowIfNull(instance);
        Name = instance.Name;
        IsCurrent = isCurrent;
        VersionText = string.IsNullOrWhiteSpace(instance.GameVersion) ? "版本未知" : $"KSP {instance.GameVersion}";
        GameDir = instance.GameDir;
        ModuleCountText = instance.ModuleCount is { } count ? $"{count} 个模组" : "模组数未知";
        _isSelected = true;
    }

    /// <summary>实例名（唯一键，与服务端 <see cref="InstanceInfo.Name"/> 一致）。</summary>
    public string Name { get; }

    /// <summary>是否当前实例（当前实例默认排在最前并标注）。</summary>
    public bool IsCurrent { get; }

    /// <summary>游戏目录（仅供展示，页面不读盘）。</summary>
    public string GameDir { get; }

    /// <summary>版本文案。</summary>
    public string VersionText { get; }

    /// <summary>模组数文案。</summary>
    public string ModuleCountText { get; }

    /// <summary>是否参与扫描。</summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>表格里显示的名字（当前实例带标记）。</summary>
    public string DisplayName => IsCurrent ? $"{Name}（当前）" : Name;
}
