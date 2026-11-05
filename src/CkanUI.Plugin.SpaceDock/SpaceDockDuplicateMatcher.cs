using System.Text;
using System.Text.RegularExpressions;
using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.Plugin.SpaceDock;

/// <summary>
/// 判断一个 SpaceDock 模组是否已经在 CKAN 里（仓库索引或已安装注册表）。
/// </summary>
/// <remarks>
/// <para>两层判据：SpaceDock 数字 ID 精确命中，或规范化名称命中。CKAN 仓库的
/// <c>resources.spacedock</c> 通常写成 <c>https://spacedock.info/mod/&lt;id&gt;</c>，
/// 这是最可靠的一层；名称层只用于兜住没写 resources 的模组，因此先把名称压成
/// 只含小写字母与数字的形态，再要求长度至少 4，避免把 "KSP" 这类短名误判成重复。</para>
/// <para>匹配只影响显示，不修改任何 CKAN 数据；用户可以随时用「隐藏 CKAN 已有模组」开关看全量。</para>
/// </remarks>
public sealed class SpaceDockDuplicateMatcher
{
    private static readonly Regex ModIdPattern = new(
        @"spacedock\.info/mod/(\d+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex NonAlphaNumeric = new(
        @"[^a-z0-9]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly HashSet<long> _modIds;
    private readonly HashSet<string> _names;

    private SpaceDockDuplicateMatcher(HashSet<long> modIds, HashSet<string> names)
    {
        _modIds = modIds;
        _names = names;
    }

    /// <summary>空匹配器：任何模组都不算重复。</summary>
    public static SpaceDockDuplicateMatcher Empty { get; } = new([], []);

    /// <summary>用当前仓库索引与注册表快照构建匹配器。</summary>
    public static SpaceDockDuplicateMatcher Create(RepositorySnapshot repository, RegistrySnapshot registry)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(registry);

        var modIds = new HashSet<long>();
        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (var mod in repository.Available)
        {
            AddSpacedockUrl(modIds, mod.Facts.SpacedockUrl);
            AddName(names, mod.Identifier);
            AddName(names, mod.Name);
        }

        foreach (var (identifier, installed) in registry.Installed)
        {
            AddName(names, identifier);
            AddName(names, installed.Identifier);
            AddSpacedockUrl(modIds, installed.Facts.SpacedockUrl);
        }

        return new SpaceDockDuplicateMatcher(modIds, names);
    }

    /// <summary>该 SpaceDock 模组是否已经在 CKAN 里。</summary>
    public bool IsDuplicate(SpaceDockModInfo mod)
    {
        ArgumentNullException.ThrowIfNull(mod);
        if (_modIds.Contains(mod.Id))
        {
            return true;
        }

        if (_names.Contains(Normalize(mod.Name)))
        {
            return true;
        }

        var slug = mod.Url.TrimEnd('/');
        var separator = slug.LastIndexOf('/');
        return separator >= 0 && _names.Contains(Normalize(slug[(separator + 1)..]));
    }

    private static void AddSpacedockUrl(HashSet<long> modIds, string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        var match = ModIdPattern.Match(url);
        if (match.Success && long.TryParse(match.Groups[1].Value, out var id))
        {
            modIds.Add(id);
        }
    }

    private static void AddName(HashSet<string> names, string? value)
    {
        var normalized = Normalize(value);
        if (normalized.Length >= 4)
        {
            names.Add(normalized);
        }
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(char.ToLowerInvariant(ch));
            }
        }

        return NonAlphaNumeric.Replace(builder.ToString(), string.Empty);
    }
}
