using System.Text.RegularExpressions;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CKAN;

namespace CkanUI.Services.Unmanaged;

/// <summary>
/// 从 Core 注册表取受管文件集合（12 篇 F-42）：语义与 <c>GameInstance.UnmanagedFiles</c> 一致
/// （<c>registry.FileOwner(path) != null</c> 即受管），另外把「纯 DLL 安装」也算作受管，
/// 避免把已经登记的 DLL 当成残留删掉。
/// </summary>
public sealed class CkanManagedPathProvider : IManagedPathProvider
{
    private const string GameDataFolder = "GameData";

    /// <summary>与 Core <c>GameInstance.dllPattern</c> 相同的 DLL 标识符提取规则。</summary>
    private static readonly Regex DllPattern = new(
        @"^(?:.*/)?(?<identifier>[^.]+).*\.dll$",
        RegexOptions.IgnoreCase | RegexOptions.IgnorePatternWhitespace | RegexOptions.Compiled);

    private readonly GameInstanceManager _instances;
    private readonly RepositoryDataManager _repositoryData;

    public CkanManagedPathProvider(GameInstanceManager instances, RepositoryDataManager repositoryData)
    {
        _instances = instances ?? throw new ArgumentNullException(nameof(instances));
        _repositoryData = repositoryData ?? throw new ArgumentNullException(nameof(repositoryData));
    }

    public OperationResult<IReadOnlySet<string>> GetManagedPaths(InstanceInfo instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        try
        {
            if (!_instances.Instances.TryGetValue(instance.Name, out var gameInstance))
            {
                return OperationResult<IReadOnlySet<string>>.Fail($"找不到游戏实例“{instance.Name}”。");
            }

            var registry = RegistryManager.Instance(gameInstance, _repositoryData).registry;
            var managed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 已登记归属的文件（installed_files 的键是 GameDir 相对路径）。
            foreach (var (relPath, _) in registry.InstalledFileInfo())
            {
                if (TryToGameDataRelative(relPath, out var relative)) managed.Add(relative);
            }

            // 纯 DLL 安装只在 installed_dlls 里登记标识符，必须按文件名反查实际 DLL，否则会被当成未管理。
            if (registry.InstalledDlls.Count > 0)
            {
                var gameData = Path.Combine(gameInstance.GameDir, GameDataFolder);
                if (Directory.Exists(gameData))
                {
                    foreach (var file in GameDataWalker.EnumerateFiles(gameData, "*.dll"))
                    {
                        var relative = Path.GetRelativePath(gameData, file).Replace('\\', '/');
                        var match = DllPattern.Match(relative);
                        if (match.Success && registry.InstalledDlls.Contains(match.Groups["identifier"].Value))
                        {
                            managed.Add(relative);
                        }
                    }
                }
            }

            return OperationResult<IReadOnlySet<string>>.Ok(managed);
        }
        catch (Exception ex)
        {
            return OperationResult<IReadOnlySet<string>>.Fail(
                "读取注册表失败，无法判断哪些内容受管。", ex.ToString(), retryable: true);
        }
    }

    /// <summary>把 GameDir 相对路径裁成 GameData 相对路径；不在 GameData 下的返回 <c>false</c>。</summary>
    private static bool TryToGameDataRelative(string gameDirRelativePath, out string relative)
    {
        relative = string.Empty;
        if (string.IsNullOrWhiteSpace(gameDirRelativePath)) return false;
        var normalized = gameDirRelativePath.Replace('\\', '/').TrimStart('/');
        var prefix = GameDataFolder + "/";
        if (!normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
        relative = normalized[prefix.Length..];
        return relative.Length > 0;
    }
}
