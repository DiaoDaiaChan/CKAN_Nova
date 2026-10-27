using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.ViewModels.Pages.Mods;

/// <summary>
/// 列表投影（04 篇 §2.1 / VM-01）：把 `IRepositoryService` 的仓库快照与 `IRegistryService` 的
/// 已装快照合并成不可变的 <see cref="ModRowData"/> 行集合，整表一次替换。
/// </summary>
/// <remarks>
/// Services 层已有等价纯函数 <c>CkanUI.Services.Models.ModListProjection.Project</c>，
/// 但它位于 CkanUI.Services 程序集，ViewModels 引用它会破坏依赖方向铁律。
/// 合并逻辑在此重写一份（只依赖 Abstractions 的 DTO），语义与之一致：
/// ①仓库行按注册表打标；②已装但仓库未提供的模组（手动安装/已下架/本地 Dll）合成追加，避免"已装模组失踪"。
/// 待 Lead 决策：把 ModListProjection 下沉到 Services.Abstractions 后可删掉本类。
/// </remarks>
public static class ModsRowProjection
{
    /// <summary>合并两份快照。任一快照为 null 时按空快照处理（不抛异常）。</summary>
    public static IReadOnlyList<ModRowData> Merge(RepositorySnapshot? repository, RegistrySnapshot? registry)
    {
        var available = repository?.Available ?? Array.Empty<ModRowData>();
        var installed = registry?.Installed;

        // 预估容量：仓库行 + 极少量仓库外的已装模组。
        var rows = new List<ModRowData>(available.Count + 8);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in available)
        {
            seen.Add(row.Identifier);

            if (installed is null || !installed.TryGetValue(row.Identifier, out var info) || info is null)
            {
                // 仓库行可能自带 IsInstalled（RepositoryService 已算过），此处只补齐"无注册表信息"的情况。
                rows.Add(row);
                continue;
            }

            rows.Add(row with
            {
                IsInstalled = true,
                InstalledVersion = info.Version,
                IsAutoInstalled = info.IsAutoInstalled,
                HasUpdate = row.IsCompatible && ModsVersionComparer.IsNewer(row.LatestVersion, info.Version),
            });
        }

        if (installed is not null)
        {
            foreach (var (identifier, info) in installed)
            {
                if (info is null || seen.Contains(identifier))
                {
                    continue;
                }

                rows.Add(new ModRowData(
                    Identifier: info.Identifier,
                    Name: info.Identifier,
                    Author: null,
                    InstalledVersion: info.Version,
                    LatestVersion: null,
                    DownloadCount: 0,
                    Tags: Array.Empty<string>(),
                    SizeBytes: null,
                    IsInstalled: true,
                    HasUpdate: false,
                    IsCompatible: true,
                    IsAutoInstalled: info.IsAutoInstalled,
                    IsSelected: false,
                    IsImplicit: false));
            }
        }

        return rows;
    }
}
