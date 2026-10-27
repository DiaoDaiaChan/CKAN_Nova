using System.Text.RegularExpressions;

namespace CkanUI.ViewModels.Pages.Settings;

/// <summary>版本文本呈现的纯格式规则（F-54）。</summary>
public static partial class VersionDisplayFormat
{
    /// <summary>按选项移除 CKAN 版本的 epoch 前缀与可选 v 前缀。</summary>
    public static string Format(string? version, bool hideEpoch, bool hideVPrefix)
    {
        if (string.IsNullOrEmpty(version))
        {
            return version ?? string.Empty;
        }

        var result = version;
        if (hideEpoch)
        {
            var match = EpochPattern().Match(result);
            if (match.Success)
            {
                result = result[match.Length..];
            }
        }

        if (hideVPrefix && result.Length > 1 && (result[0] == 'v' || result[0] == 'V')
            && char.IsAsciiDigit(result[1]))
        {
            result = result[1..];
        }

        return result;
    }

    [GeneratedRegex("^[0-9]+:", RegexOptions.CultureInvariant)]
    private static partial Regex EpochPattern();
}
