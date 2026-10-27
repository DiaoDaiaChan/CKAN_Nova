using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.Services.Unmanaged;

namespace CkanUI.Services.Cleanup;

/// <summary>
/// 默认的残留来源：目录内容启发式（12 篇 F-43）。
/// </summary>
/// <remarks>
/// CKAN 卸载时会把模组的文件删干净，但常常留下 <c>GameData/SomeMod/</c> 这种"只剩 .cfg/.version"的目录。
/// 判定规则刻意保守：<b>目录树里一个 .dll 都没有</b>，并且<b>要么只剩配置文件、要么彻底空掉</b>才算候选。
/// 只要还能找到 DLL，说明目录仍承载可执行内容，一律不列入候选（宁漏不错）。
/// 候选只保留最浅的一层：选了 <c>A</c> 就不再列 <c>A/B</c>，避免界面上同一片内容出现两次。
/// </remarks>
public sealed class HeuristicResidualDirectorySource : IResidualDirectorySource
{
    /// <summary>「只剩配置」的扩展名集合（都是卸载后常见的残留文件）。</summary>
    private static readonly HashSet<string> ConfigExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cfg", ".version", ".txt", ".md", ".json", ".yml", ".yaml", ".readme",
    };

    private const string DllExtension = ".dll";

    /// <inheritdoc />
    public OperationResult<IReadOnlyList<string>> GetCandidates(InstanceInfo instance)
    {
        if (instance is null)
        {
            return OperationResult<IReadOnlyList<string>>.Fail("没有选定游戏实例。", null, retryable: false);
        }

        var gameData = UnmanagedScanner.ResolveGameData(instance.GameDir);
        if (!Directory.Exists(gameData))
        {
            return OperationResult<IReadOnlyList<string>>.Fail(
                $"找不到 GameData 目录：{gameData}",
                "实例路径可能已被移动或删除，请先到实例页校正。",
                retryable: false);
        }

        var stats = new Dictionary<string, DirStat>(StringComparer.OrdinalIgnoreCase);
        try
        {
            // 先把目录本身登记下来，空目录（连文件都不剩）也要能出现在候选里。
            foreach (var directory in Directory.EnumerateDirectories(gameData, "*", SearchOption.AllDirectories))
            {
                var relative = UnmanagedScanner.ToRelative(gameData, directory);
                if (relative.Length > 0)
                {
                    Get(stats, relative);
                }
            }

            foreach (var file in Directory.EnumerateFiles(gameData, "*", SearchOption.AllDirectories))
            {
                var relative = UnmanagedScanner.ToRelative(gameData, file);
                var slash = relative.LastIndexOf('/');
                if (slash <= 0)
                {
                    // GameData 根目录下的散文件不属于任何候选目录，本功能不处理它们。
                    continue;
                }

                Mark(stats, relative[..slash], relative[(slash + 1)..]);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationResult<IReadOnlyList<string>>.Fail("扫描 GameData 失败。", ex.Message, retryable: true);
        }

        var candidates = new HashSet<string>(
            stats.Where(pair => pair.Value.IsCandidate).Select(pair => pair.Key),
            StringComparer.OrdinalIgnoreCase);

        var result = candidates
            .Where(path => !HasCandidateAncestor(path, candidates))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return OperationResult<IReadOnlyList<string>>.Ok(result);
    }

    private static DirStat Get(Dictionary<string, DirStat> stats, string relative)
    {
        if (!stats.TryGetValue(relative, out var stat))
        {
            stat = new DirStat();
            stats[relative] = stat;
        }

        return stat;
    }

    /// <summary>把一个文件计入它自己和所有祖先目录（递归计数 + DLL 污染标记）。</summary>
    private static void Mark(Dictionary<string, DirStat> stats, string parentRelative, string fileName)
    {
        var isDll = fileName.EndsWith(DllExtension, StringComparison.OrdinalIgnoreCase);
        var isConfig = ConfigExtensions.Contains(Path.GetExtension(fileName));
        var current = parentRelative;
        var direct = true;
        while (true)
        {
            var stat = Get(stats, current);
            stat.FileCount++;
            if (isDll)
            {
                stat.HasDll = true;
            }

            if (direct && isConfig)
            {
                stat.ConfigFileCount++;
            }

            direct = false;
            var slash = current.LastIndexOf('/');
            if (slash < 0)
            {
                return;
            }

            current = current[..slash];
        }
    }

    private static bool HasCandidateAncestor(string relative, HashSet<string> candidates)
    {
        var current = relative;
        while (true)
        {
            var slash = current.LastIndexOf('/');
            if (slash < 0)
            {
                return false;
            }

            current = current[..slash];
            if (candidates.Contains(current))
            {
                return true;
            }
        }
    }

    /// <summary>一个目录的递归统计。</summary>
    private sealed class DirStat
    {
        /// <summary>目录树里的文件总数。</summary>
        public int FileCount { get; set; }

        /// <summary>直接躺在这个目录里的配置文件数。</summary>
        public int ConfigFileCount { get; set; }

        /// <summary>目录树里是否还有 DLL。</summary>
        public bool HasDll { get; set; }

        /// <summary>无 DLL，且要么只剩配置、要么彻底空掉。</summary>
        public bool IsCandidate => !HasDll && (FileCount == 0 || ConfigFileCount > 0);
    }
}
