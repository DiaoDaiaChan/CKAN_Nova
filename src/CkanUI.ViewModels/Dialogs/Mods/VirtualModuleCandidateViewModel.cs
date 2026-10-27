using CommunityToolkit.Mvvm.ComponentModel;
using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.ViewModels.Dialogs.Mods;

/// <summary>虚拟模组的一个提供者（F-35）；展示缓存状态与下载量，帮助用户挑一个。</summary>
public sealed partial class VirtualModuleCandidateViewModel : ObservableObject
{
    /// <summary>用 DTO 构造一行。</summary>
    public VirtualModuleCandidateViewModel(VirtualModuleCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        Candidate = candidate;
    }

    /// <summary>原始候选数据。</summary>
    public VirtualModuleCandidate Candidate { get; }

    /// <summary>模组标识。</summary>
    public string Identifier => Candidate.Identifier;

    /// <summary>模组显示名。</summary>
    public string Name => Candidate.Name;

    /// <summary>是否已安装（已安装的通常更省下载）。</summary>
    public bool IsInstalled => Candidate.IsInstalled;

    /// <summary>显示名（已安装加后缀）。</summary>
    public string DisplayName => IsInstalled ? Name + "（已安装）" : Name;

    /// <summary>一句话描述。</summary>
    public string? Description => Candidate.Description;

    /// <summary>是否带描述。</summary>
    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);

    /// <summary>是否已缓存到本地。</summary>
    public bool IsCached => Candidate.IsCached;

    /// <summary>下载次数（可为空）。</summary>
    public long? DownloadCount => Candidate.DownloadCount;

    /// <summary>缓存/下载量摘要，如 "已缓存 · 下载量 12345"。</summary>
    public string AvailabilityText
    {
        get
        {
            var parts = new List<string>(2)
            {
                IsCached ? "已缓存" : "未缓存",
            };

            if (DownloadCount is { } count)
            {
                parts.Add("下载量 " + count);
            }

            return string.Join(" · ", parts);
        }
    }

    /// <summary>是否被选中（同组内互斥）。</summary>
    [ObservableProperty]
    private bool _isSelected;
}
