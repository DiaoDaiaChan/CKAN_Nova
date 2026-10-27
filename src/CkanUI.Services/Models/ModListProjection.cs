using System;
using System.Collections.Generic;
using System.Linq;
using CkanUI.Services.Abstractions.Dtos;
using CKAN.Versioning;

namespace CkanUI.Services.Models;

/// <summary>
/// 纯函数投影器（MOD-04）：
/// 把「已安装模组字典 (IReadOnlyDictionary&lt;string, InstalledModuleInfo&gt;) + 仓库快照 (RepositorySnapshot)」
/// 投影合并为最终展示的模组行集合 (IReadOnlyList&lt;ModRowData&gt;)。
/// 保证老 ModList.cs 行为对拍：
/// 1. 仓库可用模组按安装状态打标（IsInstalled, InstalledVersion, IsAutoInstalled, HasUpdate）。
/// 2. 未在可用仓库中（仅本地安装/已下架/手动Dll等）的模组作为已安装行合成追加，避免已装模组失踪。
/// 3. 版本比较判定 HasUpdate（可用最新版本 &gt; 已安装版本）。
/// </summary>
public static class ModListProjection
{
    /// <summary>
    /// 将已安装快照与可用仓库快照投影合并为不可变的 ModRowData 集合。
    /// </summary>
    /// <param name="installed">注册表中已安装模组的信息，Key 为模组 Identifier</param>
    /// <param name="repository">仓库可用快照，其中包含已有的 Available 模组行</param>
    /// <returns>投影合并后的行列表</returns>
    public static IReadOnlyList<ModRowData> Project(
        IReadOnlyDictionary<string, InstalledModuleInfo> installed,
        RepositorySnapshot repository)
    {
        installed ??= new Dictionary<string, InstalledModuleInfo>(StringComparer.OrdinalIgnoreCase);
        var available = repository?.Available ?? Array.Empty<ModRowData>();

        var matchedIdentifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<ModRowData>(available.Count + installed.Count);

        // 1. 遍历仓库中所有可用模组，与已安装状态对拍
        foreach (var row in available)
        {
            var isInstalled = installed.TryGetValue(row.Identifier, out var instInfo);
            if (isInstalled && instInfo != null)
            {
                matchedIdentifiers.Add(row.Identifier);
                var installedVer = instInfo.Version;
                var hasUpdate = CheckHasUpdate(installedVer, row.LatestVersion);

                result.Add(row with
                {
                    IsInstalled = true,
                    InstalledVersion = installedVer,
                    IsAutoInstalled = instInfo.IsAutoInstalled,
                    HasUpdate = hasUpdate
                });
            }
            else
            {
                result.Add(row);
            }
        }

        // 2. 遍历已安装模组，若仓库中未提供（例如手动安装、已下架、私有模组），作为独立行合成保留
        foreach (var (ident, instInfo) in installed)
        {
            if (matchedIdentifiers.Contains(ident))
            {
                continue;
            }

            result.Add(new ModRowData(
                Identifier: instInfo.Identifier,
                Name: instInfo.Identifier,
                Author: null,
                InstalledVersion: instInfo.Version,
                LatestVersion: null,
                DownloadCount: 0,
                Tags: Array.Empty<string>(),
                SizeBytes: null,
                IsInstalled: true,
                HasUpdate: false,
                IsCompatible: true,
                IsAutoInstalled: instInfo.IsAutoInstalled,
                IsSelected: false,
                IsImplicit: false
            ));
        }

        return result;
    }

    /// <summary>
    /// 版本比较：若最新可用版本高于已安装版本，则视为有可用更新。
    /// 遵循 CKAN Version 比较语义，解析失败时优雅降级为简单字符串比较。
    /// </summary>
    public static bool CheckHasUpdate(string? installedVersion, string? latestVersion)
    {
        if (string.IsNullOrWhiteSpace(installedVersion) || string.IsNullOrWhiteSpace(latestVersion))
        {
            return false;
        }

        if (string.Equals(installedVersion, latestVersion, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            var instVer = new ModuleVersion(installedVersion);
            var latestVer = new ModuleVersion(latestVersion);
            return latestVer > instVer;
        }
        catch
        {
            return !string.Equals(installedVersion, latestVersion, StringComparison.OrdinalIgnoreCase);
        }
    }
}
