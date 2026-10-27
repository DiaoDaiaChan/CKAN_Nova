using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.ViewModels.Pages.Instances;

/// <summary>
/// 实例列表的纯投影函数（04 篇 §2.1 列表投影 + 06 篇 UI-05）：输入服务快照与当前实例名，输出不可变行集合。
/// </summary>
/// <remarks>
/// 这里只做「变换」，不碰 IO、不碰服务事件，所以能 headless 单测（07 篇）；视图模型的职责只剩订阅与整体替换。
/// </remarks>
public static class InstancesProjection
{
    /// <summary>
    /// 把 <see cref="InstanceInfo"/> 快照投影成行列表并排序：当前实例置顶 → 被锁定者靠前（需要用户注意）→ 名称升序。
    /// </summary>
    /// <param name="instances">服务层快照（空或 null 返回空列表）。</param>
    /// <param name="currentName">当前实例名；null 表示没有当前实例。</param>
    /// <param name="directoryExists">可选的路径探测器：VM 不碰 <c>Directory.Exists</c>，由宿主注入（headless 下传假实现）。</param>
    public static IReadOnlyList<InstanceRowViewModel> Project(
        IReadOnlyList<InstanceInfo>? instances,
        string? currentName,
        Func<string, bool>? directoryExists = null)
    {
        if (instances is null || instances.Count == 0)
        {
            return Array.Empty<InstanceRowViewModel>();
        }

        var rows = new List<InstanceRowViewModel>(instances.Count);
        foreach (var info in instances)
        {
            if (info is null || string.IsNullOrWhiteSpace(info.Name))
            {
                continue;
            }

            var row = InstanceRowViewModel.From(info, currentName);
            if (directoryExists is not null)
            {
                row = row with { IsPathMissing = !ProbeExists(directoryExists, info.GameDir) };
            }

            rows.Add(row);
        }

        rows.Sort(CompareRows);
        return rows;
    }

    /// <summary>快照里是否已有该目录的实例（添加/Steam 导入前的去重）。</summary>
    public static bool ContainsPath(IReadOnlyList<InstanceInfo>? instances, string? path)
    {
        if (instances is null || string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var normalized = Normalize(path);
        foreach (var info in instances)
        {
            if (info is null)
            {
                continue;
            }

            if (string.Equals(Normalize(info.GameDir), normalized, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>过滤掉已注册目录，返回真正可以导入的候选（Steam 库导入用）。</summary>
    public static IReadOnlyList<string> FilterNewPaths(IReadOnlyList<string>? candidates, IReadOnlyList<InstanceInfo>? registered)
    {
        if (candidates is null || candidates.Count == 0)
        {
            return Array.Empty<string>();
        }

        var result = new List<string>(candidates.Count);
        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate) || ContainsPath(registered, candidate))
            {
                continue;
            }

            var trimmed = candidate.Trim();
            if (!result.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
            {
                result.Add(trimmed);
            }
        }

        return result;
    }

    /// <summary>
    /// 克隆的目标目录：Core 语义是「源实例目录的同级目录 + 新名称」（见 <c>InstanceService.CloneAsync</c>）。
    /// 这里复算一次，好让界面在真正克隆前就能提示「目标目录已存在」。
    /// </summary>
    public static string CloneDestination(string? sourceGameDir, string newName)
    {
        if (string.IsNullOrWhiteSpace(sourceGameDir))
        {
            return newName;
        }

        var trimmed = Path.TrimEndingDirectorySeparator(sourceGameDir);
        var parent = Path.GetDirectoryName(trimmed);
        return string.IsNullOrEmpty(parent) ? newName : Path.Combine(parent, newName);
    }

    private static string Normalize(string path) => Path.TrimEndingDirectorySeparator(path.Trim());

    private static bool ProbeExists(Func<string, bool> probe, string path)
    {
        try
        {
            return probe(path);
        }
        catch (Exception)
        {
            // 路径探测失败按「未知」处理，不能因为一次 IO 异常让整张列表消失。
            return true;
        }
    }

    private static int CompareRows(InstanceRowViewModel left, InstanceRowViewModel right)
    {
        if (left.IsCurrent != right.IsCurrent)
        {
            return left.IsCurrent ? -1 : 1;
        }

        if (left.IsLocked != right.IsLocked)
        {
            return left.IsLocked ? -1 : 1;
        }

        return string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
    }
}
