using CkanUI.Services.Abstractions;

namespace CkanUI.Services.Unmanaged;

/// <summary>一次接管的判定结果：逐项结局 + 待写入注册表的映射（标识符 → GameDir 相对路径）。</summary>
/// <param name="Items">逐项结局，顺序与选中项一致（目录展开后按路径排序）。</param>
/// <param name="Additions">需要登记进 <c>installed_dlls</c> 的条目；为空表示没有可写的内容。</param>
public sealed record UnmanagedAdoptPlan(
    IReadOnlyList<UnmanagedAdoptItem> Items,
    IReadOnlyDictionary<string, string> Additions);

/// <summary>
/// 接管的判定规则（12 篇 F-42 / INT-09）。纯文件系统 + 纯数据：不引用 CKAN，注册表由
/// <see cref="IManagedFileRegistrar"/> 负责读写，因此可以在 headless 测试里用临时目录完整覆盖。
/// </summary>
/// <remarks>
/// <para>安全边界：只接管 <c>.dll</c>；路径必须是 GameData 内的相对路径（拒绝绝对路径、盘符与 <c>..</c>）；
/// 已是受管的文件跳过（幂等）；同批标识符重复、或标识符已被别的路径占用时**显式拒绝**——
/// 上游 <c>ScanUnmanagedFiles</c> 会静默丢掉冲突项，结果就是「接管了却没生效」。</para>
/// <para>目录项会展开成其中的 <c>.dll</c>（递归），因此选中一个残留目录即可整批接管。</para>
/// </remarks>
public static class UnmanagedAdopter
{
    /// <summary>判定选中项能否接管；不做任何写入。</summary>
    public static OperationResult<UnmanagedAdoptPlan> Plan(
        string gameDataDir,
        IReadOnlySet<string> managedPaths,
        IReadOnlyDictionary<string, string> installedDlls,
        IReadOnlyCollection<string> relativePaths,
        Func<string, string>? normalizeIdentifier = null)
    {
        ArgumentNullException.ThrowIfNull(managedPaths);
        ArgumentNullException.ThrowIfNull(installedDlls);
        if (string.IsNullOrWhiteSpace(gameDataDir))
        {
            return OperationResult<UnmanagedAdoptPlan>.Fail("实例没有有效的游戏目录。", null, retryable: false);
        }

        if (relativePaths is null || relativePaths.Count == 0)
        {
            return OperationResult<UnmanagedAdoptPlan>.Fail("没有选中要接管的内容。", null, retryable: false);
        }

        var normalize = normalizeIdentifier ?? (static raw => raw);
        var items = new List<UnmanagedAdoptItem>(relativePaths.Count);
        var additions = new Dictionary<string, string>(StringComparer.Ordinal);
        var claimed = new HashSet<string>(StringComparer.Ordinal);

        var normalized = new List<string>(relativePaths.Count);
        foreach (var raw in relativePaths)
        {
            var relative = UnmanagedScanner.NormalizeRelative(raw);
            if (relative is null)
            {
                return OperationResult<UnmanagedAdoptPlan>.Fail(
                    $"路径不合法：{raw}",
                    "只接受 GameData 内的相对路径（不允许绝对路径、盘符或 ..）。",
                    retryable: false);
            }

            if (!normalized.Contains(relative, StringComparer.OrdinalIgnoreCase))
            {
                normalized.Add(relative);
            }
        }

        foreach (var relative in normalized)
        {
            var absolute = Path.Combine(gameDataDir, relative.Replace('/', Path.DirectorySeparatorChar));
            if (Directory.Exists(absolute))
            {
                List<string> dlls;
                try
                {
                    dlls = EnumerateDlls(absolute, relative);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    items.Add(Refuse(relative, null, $"目录读取失败：{ex.Message}"));
                    continue;
                }

                if (dlls.Count == 0)
                {
                    items.Add(Refuse(relative, null, "目录里没有 .dll 文件可以接管。"));
                    continue;
                }

                foreach (var dll in dlls)
                {
                    Add(dll);
                }

                continue;
            }

            if (!File.Exists(absolute))
            {
                items.Add(Refuse(relative, null, "文件已不存在，请刷新后重试。"));
                continue;
            }

            Add(relative);
        }

        return OperationResult<UnmanagedAdoptPlan>.Ok(new UnmanagedAdoptPlan(items, additions));

        void Add(string relative)
        {
            if (!relative.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                items.Add(Refuse(relative, null, "只有 .dll 文件可以接管为受管安装。"));
                return;
            }

            var guessed = UnmanagedScanner.GuessIdentifier(relative);
            var identifier = guessed is null ? null : normalize(guessed);
            if (string.IsNullOrWhiteSpace(identifier))
            {
                items.Add(Refuse(relative, guessed, "无法从文件名推断模块标识符。"));
                return;
            }

            if (managedPaths.Contains(relative))
            {
                items.Add(new UnmanagedAdoptItem(
                    relative, identifier, UnmanagedAdoptOutcome.Skipped, "该文件已由注册表登记为受管。"));
                return;
            }

            if (!claimed.Add(identifier))
            {
                items.Add(Refuse(relative, identifier, $"标识符“{identifier}”在本批里重复，只接管先出现的一个。"));
                return;
            }

            var target = UnmanagedScanner.GameDataFolder + "/" + relative;
            if (installedDlls.TryGetValue(identifier, out var existing)
                && !string.IsNullOrWhiteSpace(existing)
                && !string.Equals(existing.Replace('\\', '/').TrimStart('/'), target, StringComparison.OrdinalIgnoreCase))
            {
                items.Add(Refuse(relative, identifier, $"标识符“{identifier}”已登记给 {existing}，请先移除那条登记。"));
                return;
            }

            additions[identifier] = target;
            items.Add(new UnmanagedAdoptItem(relative, identifier, UnmanagedAdoptOutcome.Adopted));
        }
    }

    private static UnmanagedAdoptItem Refuse(string relative, string? identifier, string reason)
        => new(relative, identifier, UnmanagedAdoptOutcome.Refused, reason);

    /// <summary>递归列出目录下的 DLL，返回 GameData 相对路径（正斜杠，排序稳定便于断言）。</summary>
    private static List<string> EnumerateDlls(string absoluteDir, string relativeDir)
    {
        var result = new List<string>();
        foreach (var file in Directory.EnumerateFiles(absoluteDir, "*", SearchOption.AllDirectories))
        {
            if (!file.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) continue;
            var inner = Path.GetRelativePath(absoluteDir, file).Replace('\\', '/');
            result.Add(relativeDir + "/" + inner);
        }

        result.Sort(StringComparer.OrdinalIgnoreCase);
        return result;
    }
}
