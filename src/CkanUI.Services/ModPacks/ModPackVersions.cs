namespace CkanUI.Services.ModPacks;

/// <summary>
/// 版本号的近似比较（只用于导入预览的"会升 / 会降 / 已同版本"分类）。
/// </summary>
/// <remarks>
/// 真正的版本语义（版本区间、epoch、预发布）由 Core 的 <c>ModuleVersion</c> / <c>RelationshipResolver</c> 负责，
/// 这里刻意只做点分段数字比较：预览是纯离线逻辑，输入还可能是替身给的任意字符串，
/// 用 <c>ModuleVersion</c> 解析未知格式会抛异常，而预览不该因为一个奇怪版本号整体失败。
/// </remarks>
internal static class ModPackVersions
{
    /// <summary>比较两个版本号：返回 -1 / 0 / 1。</summary>
    public static int Compare(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) && string.IsNullOrWhiteSpace(right))
        {
            return 0;
        }

        if (string.IsNullOrWhiteSpace(left))
        {
            return -1;
        }

        if (string.IsNullOrWhiteSpace(right))
        {
            return 1;
        }

        var leftParts = Split(left);
        var rightParts = Split(right);
        var count = Math.Max(leftParts.Count, rightParts.Count);
        for (var index = 0; index < count; index++)
        {
            var leftPart = index < leftParts.Count ? leftParts[index] : "0";
            var rightPart = index < rightParts.Count ? rightParts[index] : "0";
            int comparison;
            if (long.TryParse(leftPart, out var leftNumber) && long.TryParse(rightPart, out var rightNumber))
            {
                comparison = leftNumber.CompareTo(rightNumber);
            }
            else
            {
                comparison = string.Compare(leftPart, rightPart, StringComparison.OrdinalIgnoreCase);
            }

            if (comparison != 0)
            {
                return comparison < 0 ? -1 : 1;
            }
        }

        return 0;
    }

    /// <summary>两个版本号是否等价（用于判断"同一模组被不同来源要求了不同版本"）。</summary>
    public static bool AreSame(string? left, string? right) => Compare(left, right) == 0;

    private static List<string> Split(string version)
    {
        var parts = new List<string>();
        foreach (var part in version.Trim().Split('.', '-', '_', '+'))
        {
            parts.Add(part);
        }

        return parts;
    }
}
