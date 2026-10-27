using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.ViewModels.Pages.Mods;

/// <summary>
/// 变更集预览的本地估算（03 篇 §3.6 / 04 篇 §6）：只把用户勾选映射成 <see cref="ChangeSetPreview"/>，
/// 不含依赖解析与推荐项；权威结果由 <c>IChangeSetService</c>（INT-07）给出。
/// </summary>
/// <remarks>
/// 勾选语义与老 GUI 一致：未装 → 安装；已装且有更新 → 升级；已装且最新 → 卸载（复选框在此表示"移出"）。
/// </remarks>
public static class ModsChangePreviewBuilder
{
    /// <summary>按勾选集合生成预览。未知标识符被忽略。</summary>
    public static ChangeSetPreview Build(IReadOnlyList<ModRowData> rows, IReadOnlyCollection<string> selected)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(selected);

        if (selected.Count == 0)
        {
            return Empty;
        }

        var lookup = new Dictionary<string, ModRowData>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            lookup[row.Identifier] = row;
        }

        var items = new List<ChangeItemDto>(selected.Count);
        var warnings = new List<string>();
        var conflicts = new List<string>();
        long bytes = 0;

        foreach (var identifier in selected)
        {
            if (!lookup.TryGetValue(identifier, out var row))
            {
                continue;
            }

            var action = ResolveAction(row);
            var downloadBytes = action is ChangeAction.Install or ChangeAction.Upgrade ? row.SizeBytes : null;
            bytes += downloadBytes ?? 0;

            items.Add(new ChangeItemDto(
                Identifier: row.Identifier,
                Name: row.Name,
                Action: action,
                FromVersion: row.IsInstalled ? row.InstalledVersion : null,
                ToVersion: action == ChangeAction.Remove ? null : row.LatestVersion,
                Origin: row.IsImplicit ? ChangeOrigin.Dependency : ChangeOrigin.UserRequested,
                DownloadBytes: downloadBytes));

            if (!row.IsCompatible)
            {
                conflicts.Add($"{row.Name} 与当前游戏版本不兼容");
            }

            if (action == ChangeAction.Remove && row.IsAutoInstalled)
            {
                warnings.Add($"{row.Name} 是自动安装的依赖，卸载可能影响其它模组");
            }
        }

        return new ChangeSetPreview(items, conflicts, bytes, warnings);
    }

    /// <summary>空预览。</summary>
    public static ChangeSetPreview Empty { get; } = new(
        Array.Empty<ChangeItemDto>(),
        Array.Empty<string>(),
        0,
        Array.Empty<string>());

    /// <summary>勾选语义 → 变更动作。</summary>
    public static ChangeAction ResolveAction(ModRowData row)
    {
        if (!row.IsInstalled)
        {
            return ChangeAction.Install;
        }

        return row.HasUpdate ? ChangeAction.Upgrade : ChangeAction.Remove;
    }
}
