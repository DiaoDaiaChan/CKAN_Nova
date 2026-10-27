using CkanUI.Services.Abstractions.Dtos;
using CKAN;

namespace CkanUI.Services.Models;

/// <summary>
/// 把 CKAN 元数据投影成 <see cref="ModSearchFacts"/>（12 篇 F-20/F-21）。
/// 高级搜索里"只有服务层才知道"的字段（描述、许可证、语言、关系、标签组、缓存态）在这里统一取值，
/// 视图模型不再自己解析 CKAN 类型（01 篇 §5 依赖方向铁律）。
/// </summary>
public static class ModSearchFactsFactory
{
    /// <summary>从 <see cref="CkanModule"/> 取一份搜索事实；为 null 时返回空事实。</summary>
    /// <param name="mod">仓库里的模组元数据（可为 null，表示只知道标识符的已装模组）。</param>
    /// <param name="labels">该模组所属的标签组名称（来自 <see cref="ModuleLabelList"/>）。</param>
    /// <param name="isCached">安装包是否已在下载缓存里。</param>
    /// <param name="isNew">是否新近变为兼容/新近入库（镜像上游 `GUIMod.IsNew`）。</param>
    /// <param name="isReplaceable">已安装且存在替代模组。</param>
    public static ModSearchFacts From(CkanModule? mod,
                                      IReadOnlyList<string>? labels = null,
                                      bool isCached = false,
                                      bool isNew = false,
                                      bool isReplaceable = false)
    {
        if (mod is null)
        {
            return new ModSearchFacts
            {
                Labels = labels is { Count: > 0 } ? labels : null,
                IsCached = isCached,
                IsNew = isNew,
                IsReplaceable = isReplaceable,
            };
        }

        return new ModSearchFacts
        {
            Abstract = mod.@abstract,
            Description = mod.description,
            Licenses = Names(mod.license, static lic => lic.ToString()),
            Localizations = Names(mod.localizations, static loc => loc),
            DependsOn = RelationshipNames(mod.depends),
            Recommends = RelationshipNames(mod.recommends),
            Suggests = RelationshipNames(mod.suggests),
            ConflictsWith = RelationshipNames(mod.conflicts),
            Supports = RelationshipNames(mod.supports),
            Labels = labels is { Count: > 0 } ? labels : null,
            IsCached = isCached,
            IsNew = isNew,
            IsReplaceable = isReplaceable,
        };
    }

    /// <summary>
    /// 展开关系描述符里的模组名（`any_of` 递归展开），供 `dep:`/`rec:`/`sug:`/`conf:`/`sup:` 前缀匹配。
    /// 上游 <c>ModSearch.Matches</c> 用 `RelationshipDescriptor.StartsWith`，这里把名字摊平后由视图模型做同样的前缀比较。
    /// </summary>
    public static IReadOnlyList<string> RelationshipNames(IEnumerable<RelationshipDescriptor>? relationships)
    {
        if (relationships is null) return Array.Empty<string>();

        var names = new List<string>();
        foreach (var relationship in relationships) Collect(relationship, names);
        return names.Count == 0 ? Array.Empty<string>() : names.Distinct(StringComparer.Ordinal).ToArray();
    }

    /// <summary>
    /// 计算实例的标签组成员关系：标识符 → 标签组名称列表（默认含 Favourites/Hidden/Held 三组，
    /// 对齐 <c>ModuleLabelList.GetDefaultLabels()</c>）。任何异常都降级成空表（搜索不因标签文件损坏而失败）。
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> LabelMembership(GameInstance? instance)
    {
        if (instance is null) return EmptyMembership;

        try
        {
            var membership = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            // 与上游 ModSearch 取值一致：labels.json 里适用于本实例的标签组；
            // 缺文件时 ModuleLabels 就是 Favourites/Hidden/Held 三组默认值。
            foreach (var label in ModuleLabelList.ModuleLabels.LabelsFor(instance.Name))
            {
                if (label is null) continue;
                foreach (var identifier in label.IdentifiersFor(instance.Game))
                {
                    if (string.IsNullOrEmpty(identifier)) continue;
                    if (!membership.TryGetValue(identifier, out var names))
                    {
                        names = new List<string>();
                        membership[identifier] = names;
                    }
                    if (!names.Contains(label.Name, StringComparer.Ordinal)) names.Add(label.Name);
                }
            }

            var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in membership) result[pair.Key] = pair.Value;
            return result;
        }
        catch (Exception)
        {
            return EmptyMembership;
        }
    }

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> EmptyMembership =
        new Dictionary<string, IReadOnlyList<string>>();

    /// <summary>取一组名称（去掉空值）；空集合返回 null，表示"没有这类事实"。</summary>
    private static IReadOnlyList<string>? Names<T>(IEnumerable<T>? source, Func<T, string?> selector)
    {
        if (source is null) return null;

        List<string>? names = null;
        foreach (var item in source)
        {
            var name = item is null ? null : selector(item);
            if (string.IsNullOrEmpty(name)) continue;
            (names ??= new List<string>()).Add(name);
        }
        return names;
    }

    private static void Collect(RelationshipDescriptor? relationship, List<string> names)
    {
        switch (relationship)
        {
            case null:
                return;
            case ModuleRelationshipDescriptor module:
                if (!string.IsNullOrEmpty(module.name)) names.Add(module.name);
                return;
            case AnyOfRelationshipDescriptor anyOf:
                if (anyOf.any_of is null) return;
                foreach (var inner in anyOf.any_of) Collect(inner, names);
                return;
            default:
                // 上游可能新增关系类型：退回 ToString() 前缀匹配，至少不会静默丢条件。
                var text = relationship.ToString();
                if (!string.IsNullOrEmpty(text)) names.Add(text);
                return;
        }
    }
}
