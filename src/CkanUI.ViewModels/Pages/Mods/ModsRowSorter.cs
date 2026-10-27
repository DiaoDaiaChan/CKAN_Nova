using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.ViewModels.Pages.Mods;

/// <summary>行排序（纯函数，VM-01）。稳定排序：相同键保持原顺序，避免每次重建列表时行序抖动。</summary>
public static class ModsRowSorter
{
    /// <summary>按指定列排序；不修改入参，返回新列表。</summary>
    public static IReadOnlyList<ModRowData> Sort(
        IEnumerable<ModRowData> rows,
        ModsSortColumn column,
        bool ascending)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var ordered = column switch
        {
            ModsSortColumn.Identifier => Order(rows, row => row.Identifier, StringComparer.OrdinalIgnoreCase),
            ModsSortColumn.InstalledVersion => OrderByVersion(rows, row => row.InstalledVersion),
            ModsSortColumn.LatestVersion => OrderByVersion(rows, row => row.LatestVersion),
            ModsSortColumn.Author => Order(rows, row => row.Author ?? string.Empty, StringComparer.OrdinalIgnoreCase),
            ModsSortColumn.DownloadCount => rows.OrderBy(row => row.DownloadCount),
            ModsSortColumn.SizeBytes => rows.OrderBy(row => row.SizeBytes ?? -1),
            ModsSortColumn.IsInstalled => rows.OrderBy(row => row.IsInstalled),
            _ => Order(rows, row => row.Name, StringComparer.OrdinalIgnoreCase),
        };

        var list = ordered.ToList();
        if (!ascending)
        {
            list.Reverse();
        }

        return list;
    }

    private static IOrderedEnumerable<ModRowData> Order(
        IEnumerable<ModRowData> rows,
        Func<ModRowData, string> selector,
        StringComparer comparer) => rows.OrderBy(selector, comparer);

    // 版本列按版本语义排序（1.10 > 1.9），不能按字符串排。
    private static IOrderedEnumerable<ModRowData> OrderByVersion(
        IEnumerable<ModRowData> rows,
        Func<ModRowData, string?> selector) =>
        rows.OrderBy(row => selector(row) ?? string.Empty, ModsVersionStringComparer.Instance);

    private sealed class ModsVersionStringComparer : IComparer<string>
    {
        public static readonly ModsVersionStringComparer Instance = new();

        public int Compare(string? x, string? y) => ModsVersionComparer.Compare(x, y);
    }
}
