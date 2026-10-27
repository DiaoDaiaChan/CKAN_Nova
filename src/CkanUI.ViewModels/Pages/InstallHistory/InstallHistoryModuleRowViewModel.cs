using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.ViewModels.Pages.InstallHistory;

/// <summary>历史快照里的一个模组行（右侧"已装 / 未装"分组的单元）。</summary>
public sealed class InstallHistoryModuleRowViewModel
{
    public InstallHistoryModuleRowViewModel(InstallHistoryModule module)
    {
        ArgumentNullException.ThrowIfNull(module);
        Identifier = module.Identifier;
        Version = module.Version;
        InstalledVersion = module.InstalledVersion;
        IsInstalled = module.IsInstalled;
        IsChanged = module.IsChanged;
        StateText = InstallHistoryText.ModuleStateLabel(module);
    }

    /// <summary>模组标识（CKAN identifier）。</summary>
    public string Identifier { get; }

    /// <summary>快照里记的版本。</summary>
    public string Version { get; }

    /// <summary>当前注册表里的版本；未安装为 null。</summary>
    public string? InstalledVersion { get; }

    /// <summary>当前是否装着。</summary>
    public bool IsInstalled { get; }

    /// <summary>与快照不同（未装或版本不同）。</summary>
    public bool IsChanged { get; }

    /// <summary>状态文案（已安装 / 未安装 / 版本不同（当前 x））。</summary>
    public string StateText { get; }

    /// <summary>版本列文案：快照版本（→ 当前版本，若不同）。</summary>
    public string VersionText => IsChanged && IsInstalled && InstalledVersion is { Length: > 0 }
        ? Version + " → " + InstalledVersion
        : Version;
}
