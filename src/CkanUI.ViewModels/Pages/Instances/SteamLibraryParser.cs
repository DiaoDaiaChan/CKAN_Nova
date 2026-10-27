namespace CkanUI.ViewModels.Pages.Instances;

/// <summary>
/// <c>steamapps/libraryfolders.vdf</c> 的纯解析函数（06 篇 UI-32 Steam 导入）。
/// </summary>
/// <remarks>
/// 解析与 IO 分离：本类只吃文本，任何"读磁盘"的动作留在宿主实现里，
/// 于是 VDF 的两种格式（键值对写法与 <c>"path"</c> 行写法）都能用测试夹具覆盖。
/// </remarks>
public static class SteamLibraryParser
{
    /// <summary>Steam 库里 KSP 的相对位置：<c>&lt;库根&gt;/steamapps/common/Kerbal Space Program</c>。</summary>
    public const string GameFolderName = "Kerbal Space Program";

    /// <summary>从 VDF 文本里抽出全部库根目录（按出现顺序、已反转义、已去重）。</summary>
    public static IReadOnlyList<string> ParseLibraryRoots(string? vdfContent)
    {
        if (string.IsNullOrWhiteSpace(vdfContent))
        {
            return Array.Empty<string>();
        }

        var roots = new List<string>();
        foreach (var rawLine in vdfContent.Split('\n', '\r'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || !line.Contains("path", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = ExtractQuotedValue(line);
            if (value is null)
            {
                continue;
            }

            var root = value.Replace("\\\\", "\\", StringComparison.Ordinal).Trim();
            if (root.Length == 0 || roots.Contains(root, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            roots.Add(root);
        }

        return roots;
    }

    /// <summary>由库根目录推出候选 KSP 目录（不做存在性判断，存在性由宿主探测器决定）。</summary>
    public static IReadOnlyList<string> CandidateGameDirs(IEnumerable<string>? libraryRoots)
    {
        if (libraryRoots is null)
        {
            return Array.Empty<string>();
        }

        var result = new List<string>();
        foreach (var root in libraryRoots)
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                continue;
            }

            var candidate = Path.Combine(root.Trim(), "steamapps", "common", GameFolderName);
            if (!result.Contains(candidate, StringComparer.OrdinalIgnoreCase))
            {
                result.Add(candidate);
            }
        }

        return result;
    }

    private static string? ExtractQuotedValue(string line)
    {
        var firstQuote = line.IndexOf('"');
        if (firstQuote < 0)
        {
            return null;
        }

        var secondQuote = line.IndexOf('"', firstQuote + 1);
        if (secondQuote < 0)
        {
            return null;
        }

        var valueStart = line.IndexOf('"', secondQuote + 1);
        if (valueStart < 0)
        {
            return null;
        }

        var valueEnd = line.IndexOf('"', valueStart + 1);
        if (valueEnd < 0)
        {
            return null;
        }

        return line.Substring(valueStart + 1, valueEnd - valueStart - 1);
    }
}
