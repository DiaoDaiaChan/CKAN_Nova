namespace CkanUI.ViewModels.Pages.Mods;

/// <summary>
/// 版本号比较（VM-01）。优先按 .NET `Version` 语义比较数字段，解析失败时退化为忽略大小写的字符串比较；
/// 不引用 Core 的 `ModuleVersion`（架构铁律：ViewModels 不得引用 CKAN 命名空间）。
/// </summary>
public static class ModsVersionComparer
{
    /// <summary>比较两个版本号字符串；null/空串视为最小值。</summary>
    public static int Compare(string? left, string? right)
    {
        var a = left?.Trim() ?? string.Empty;
        var b = right?.Trim() ?? string.Empty;

        if (a.Length == 0 || b.Length == 0)
        {
            return a.Length.CompareTo(b.Length);
        }

        if (Version.TryParse(a, out var va) && Version.TryParse(b, out var vb))
        {
            return va.CompareTo(vb);
        }

        return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>最新版本是否严格高于已装版本（已装为空时不算有更新）。</summary>
    public static bool IsNewer(string? latest, string? installed) =>
        !string.IsNullOrWhiteSpace(installed)
        && !string.IsNullOrWhiteSpace(latest)
        && Compare(latest, installed) > 0;
}
