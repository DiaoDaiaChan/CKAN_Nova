using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.ViewModels.Pages.Mods;

/// <summary>行过滤（纯函数，VM-01）：安装状态与兼容性为逻辑与，和搜索之间也是逻辑与（03 篇 §3.5）。</summary>
public static class ModsRowFilter
{
    /// <summary>该行是否通过当前过滤器。</summary>
    public static bool Matches(ModRowData row, ModsInstallFilter install, ModsCompatibilityFilter compatibility) =>
        MatchesInstall(row, install) && MatchesCompatibility(row, compatibility);

    /// <summary>安装状态过滤。</summary>
    public static bool MatchesInstall(ModRowData row, ModsInstallFilter filter) => filter switch
    {
        ModsInstallFilter.All => true,
        ModsInstallFilter.Installed => row.IsInstalled,
        ModsInstallFilter.NotInstalled => !row.IsInstalled,
        ModsInstallFilter.Upgradeable => row.IsInstalled && row.HasUpdate,
        _ => true,
    };

    /// <summary>兼容性过滤。</summary>
    public static bool MatchesCompatibility(ModRowData row, ModsCompatibilityFilter filter) => filter switch
    {
        ModsCompatibilityFilter.All => true,
        ModsCompatibilityFilter.CompatibleOnly => row.IsCompatible,
        ModsCompatibilityFilter.IncompatibleOnly => !row.IsCompatible,
        _ => true,
    };
}
