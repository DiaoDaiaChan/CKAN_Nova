using CkanUI.Services.Abstractions.Dtos;
using CkanUI.Services.Abstractions.Mods;
using CkanUI.ViewModels.Dialogs;

namespace CkanUI.ViewModels.Pages.Mods;

/// <summary>
/// 把列表行投影成详情面板的富字段快照（F-26 四页签的接线层）。
/// </summary>
/// <remarks>
/// <para>列表行 <see cref="ModRowData"/> 只带列表列所需字段，富字段（简介/描述/许可证/五类关系/主页）
/// 都在 <see cref="ModSearchFacts"/> 上；<see cref="ModsPageViewModel"/> 选中行变化时调这里拼快照，
/// 视图只负责渲染，不再各自查服务。</para>
/// <para>不做任何 IO：关系行的「已满足」按调用方给的已装标识集合判定，缺省一律判为缺失，
/// 避免详情面板凭空显示「已满足」。已装文件清单仍无契约来源（页签显示空态）；
/// 版本集合由 <see cref="BuildVersions"/> 从 <c>IModVersionCatalog</c> 的结果投影后回填（F-30）。</para>
/// </remarks>
public static class ModDetailsSnapshotBuilder
{
    /// <summary>拼一份详情快照。</summary>
    /// <param name="data">列表行数据。</param>
    /// <param name="installedIdentifiers">当前实例已装模组标识集合；为 <c>null</c> 时关系行一律判为缺失。</param>
    /// <returns>详情面板可直接渲染的快照。</returns>
    public static ModDetailsSnapshot Build(ModRowData data, IReadOnlyCollection<string>? installedIdentifiers = null)
    {
        var facts = data.Facts;
        var installed = installedIdentifiers is { Count: > 0 }
            ? new HashSet<string>(installedIdentifiers, StringComparer.OrdinalIgnoreCase)
            : null;

        bool IsSatisfied(string identifier) => installed?.Contains(identifier) ?? false;

        return new ModDetailsSnapshot(
            data.Identifier,
            data.Name,
            data.Author,
            facts.Abstract,
            facts.Description,
            FormatLicenses(facts.Licenses),
            facts.Homepage,
            data.LatestVersion,
            data.InstalledVersion,
            data.SizeBytes,
            data.Tags,
            ToRelations(facts.DependsOn, ModRelationKind.Dependency, IsSatisfied),
            ToRelations(facts.Recommends, ModRelationKind.Recommendation, IsSatisfied),
            ToRelations(facts.ConflictsWith, ModRelationKind.Conflict, IsSatisfied),
            data.IsCompatible);
    }

    /// <summary>
    /// 把服务层的版本选项投影成详情面板的版本行（F-30）。
    /// 服务契约用的是 Abstractions 的纯记录，视图模型用自己的展示 DTO，两者字段一一对应。
    /// </summary>
    /// <param name="options">版本目录返回的选项；<c>null</c> 或空集合返回空列表（面板走空态）。</param>
    public static IReadOnlyList<ModVersionItemDto> BuildVersions(IEnumerable<ModVersionOption>? options)
    {
        if (options is null)
        {
            return Array.Empty<ModVersionItemDto>();
        }

        var rows = new List<ModVersionItemDto>();
        foreach (var option in options)
        {
            if (option is null || string.IsNullOrWhiteSpace(option.Version))
            {
                continue;
            }

            rows.Add(new ModVersionItemDto(
                option.Version,
                option.GameCompatibility,
                option.ReleaseDate,
                option.DownloadSizeBytes,
                option.DownloadUrl,
                option.IsInstalled,
                option.IsCompatible,
                option.IsUnstable));
        }

        return rows;
    }

    /// <summary>许可证列表拼成一行（多个用顿号分隔）；没有时返回 <c>null</c> 让面板走「未知」。</summary>
    private static string? FormatLicenses(IReadOnlyList<string>? licenses) =>
        licenses is { Count: > 0 } ? string.Join('、', licenses) : null;

    /// <summary>标识符集合投影成关系行；空集合返回 <c>null</c>（面板据此显示空态）。</summary>
    private static IReadOnlyList<ModRelationItem>? ToRelations(
        IReadOnlyList<string>? identifiers,
        ModRelationKind kind,
        Func<string, bool> isSatisfied)
    {
        if (identifiers is not { Count: > 0 })
        {
            return null;
        }

        var items = new List<ModRelationItem>(identifiers.Count);
        foreach (var identifier in identifiers)
        {
            if (string.IsNullOrWhiteSpace(identifier))
            {
                continue;
            }

            items.Add(new ModRelationItem(identifier, null, kind, isSatisfied(identifier)));
        }

        return items.Count == 0 ? null : items;
    }
}
