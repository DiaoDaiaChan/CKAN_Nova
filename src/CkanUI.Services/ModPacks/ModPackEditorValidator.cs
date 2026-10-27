using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using CKAN;
using CKAN.Versioning;
using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.Services.ModPacks;

/// <summary>
/// <c>.ckan</c> 编辑器的纯函数校验（UI-24）。
/// </summary>
/// <remarks>
/// 铁律：不读盘、不写盘、不抛异常、不依赖任何服务。判定口径尽量贴上游：
/// <list type="bullet">
/// <item>标识按 <c>CKAN.Identifier.ValidIdentifierPattern</c>（首字符字母/数字、至少 2 个字符、只允许字母数字与连字符）再叠加"必须全小写"。</item>
/// <item>版本号按 <c>Spec.md</c> 的写法（<c>[epoch:]mod_version</c>，且 mod_version 只允许 ASCII 字母、数字与 <c>. + - _</c>）做字符集校验；游戏版本交给 <c>GameVersion.TryParse</c>。
/// 上游 <c>ModuleVersion</c> 对任意字符串都不抛异常（完全按字符串比较），所以不能靠它判断"能否解析"。</item>
/// <item>许可证必须是 <c>CKAN.License.valid_licenses</c> 里的值，否则上游 <c>new License(...)</c> 会抛 <c>BadMetadataKraken</c>。</item>
/// <item>关系行的名字按上游 <c>ModuleRelationshipDescriptor.name</c> 的用法校验，语义模式决定读 <c>name</c> 还是 <c>any_of</c>。</item>
/// </list>
/// 只有 Error 级问题会阻止写盘；Warning 只是提醒。
/// </remarks>
public static class ModPackEditorValidator
{
    /// <summary>
    /// <c>Spec.md</c> 的版本写法：可选 <c>epoch:</c> 前缀 + 只含 ASCII 字母、数字与 <c>. + - _</c>。
    /// </summary>
    private static readonly Regex VersionPattern = new(@"^([0-9]+:)?[0-9A-Za-z.+\-_]+$", RegexOptions.Compiled);

    /// <summary>发布状态白名单（上游 <c>CKAN.ReleaseStatus</c> 只有这三个值）。</summary>
    private static readonly HashSet<string> ReleaseStatuses = new(StringComparer.Ordinal)
    {
        "stable", "testing", "development",
    };

    /// <summary>包类型白名单（上游 <c>CKAN.ModuleKind</c>）。</summary>
    private static readonly HashSet<string> Kinds = new(StringComparer.Ordinal)
    {
        "package", "metapackage", "dlc",
    };

    /// <summary>校验整份文档，返回全部问题（Error 与 Warning 混排，Error 在前）。</summary>
    public static IReadOnlyList<ModPackEditorIssue> Validate(ModPackEditorDocument? document)
    {
        if (document is null)
        {
            return new[] { Error("document.null", string.Empty, "没有可校验的文档。") };
        }

        var issues = new List<ModPackEditorIssue>();

        ValidatePath(document, issues);
        ValidateIdentity(document, issues);
        ValidateVersions(document, issues);
        ValidateLicenses(document, issues);
        ValidateEnums(document, issues);
        ValidateDownload(document, issues);
        ValidateRelations(document, issues);

        return issues
            .OrderBy(issue => issue.Severity == ModPackEditorIssueSeverity.Error ? 0 : 1)
            .ToList();
    }

    /// <summary>是否存在 Error 级问题（写盘前的闸门）。</summary>
    public static bool HasError(IReadOnlyList<ModPackEditorIssue> issues)
        => issues.Any(issue => issue.Severity == ModPackEditorIssueSeverity.Error);

    private static void ValidatePath(ModPackEditorDocument document, List<ModPackEditorIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(document.FilePath))
        {
            issues.Add(Error("path.empty", nameof(document.FilePath), "请填写 .ckan 文件的保存路径。"));
        }
        else if (!document.FilePath.Trim().EndsWith(".ckan", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(Warning("path.extension", nameof(document.FilePath), "保存路径没有 .ckan 后缀，CKAN 只认 .ckan 元数据包。"));
        }
    }

    private static void ValidateIdentity(ModPackEditorDocument document, List<ModPackEditorIssue> issues)
    {
        var identifier = (document.Identifier ?? string.Empty).Trim();
        if (identifier.Length == 0)
        {
            issues.Add(Error("identifier.empty", nameof(document.Identifier), "模组标识不能为空。"));
        }
        else if (!string.Equals(identifier, identifier.ToLowerInvariant(), StringComparison.Ordinal))
        {
            issues.Add(Error("identifier.lowercase", nameof(document.Identifier), "模组标识必须全部小写（CKAN 标识区分大小写，大写会导致安装时找不到依赖）。"));
        }
        else if (!Identifier.ValidIdentifierPattern.IsMatch(identifier))
        {
            issues.Add(Error("identifier.charset", nameof(document.Identifier), "模组标识只能包含小写字母、数字与连字符，不能以连字符开头，至少 2 个字符。"));
        }

        if (string.IsNullOrWhiteSpace(document.Name))
        {
            issues.Add(Error("name.empty", nameof(document.Name), "显示名不能为空。"));
        }

        if (string.IsNullOrWhiteSpace(document.Abstract))
        {
            issues.Add(Error("abstract.empty", nameof(document.Abstract), "摘要不能为空（CKAN 的 abstract 是必填字段）。"));
        }

        if (document.Authors is null || document.Authors.All(string.IsNullOrWhiteSpace))
        {
            issues.Add(Warning("author.empty", nameof(document.Authors), "没有填写作者；CKAN 允许，但发布前建议补上。"));
        }
    }

    private static void ValidateVersions(ModPackEditorDocument document, List<ModPackEditorIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(document.Version))
        {
            issues.Add(Error("version.empty", nameof(document.Version), "版本号不能为空。"));
        }
        else if (!IsParseableVersion(document.Version))
        {
            issues.Add(Error("version.invalid", nameof(document.Version), $"版本号 \"{document.Version}\" 含有 CKAN 不接受的字符：只允许 ASCII 字母、数字与 . + - _，可选 \"epoch:\" 前缀（示例：1.0.0、2.1、1:1.4）。"));
        }

        if (!string.IsNullOrWhiteSpace(document.SpecVersion) && !IsParseableVersion(document.SpecVersion))
        {
            issues.Add(Error("spec_version.invalid", nameof(document.SpecVersion), $"元数据规范版本 \"{document.SpecVersion}\" 含有 CKAN 不接受的字符（示例：v1.6、v1.36）。"));
        }

        var hasMin = ValidateGameVersion(document.KspVersionMin, nameof(document.KspVersionMin), "游戏版本下限", issues);
        var hasMax = ValidateGameVersion(document.KspVersionMax, nameof(document.KspVersionMax), "游戏版本上限", issues);
        ValidateGameVersion(document.KspVersion, nameof(document.KspVersion), "目标游戏版本", issues);

        // 上游 CkanModule.CheckHealth（external/CKAN/Core/Types/CkanModule.cs）在 ksp_version 与
        // ksp_version_(min|max) 同时存在时抛 InvalidModuleAttributesKraken，文件根本打不开，所以这里判 Error。
        if (!string.IsNullOrWhiteSpace(document.KspVersion) && (hasMin || hasMax))
        {
            issues.Add(Error(
                "ksp_version.conflict",
                nameof(document.KspVersion),
                "ksp_version 不能与 ksp_version_min / ksp_version_max 同时填写：上游 CKAN 会拒绝加载这种文件。请只保留精确版本，或只保留上下限。"));
        }

        if (hasMin && hasMax)
        {
            var min = GameVersion.Parse(document.KspVersionMin.Trim());
            var max = GameVersion.Parse(document.KspVersionMax.Trim());
            if (min.CompareTo(max) > 0)
            {
                issues.Add(Warning("ksp_version.range", nameof(document.KspVersionMin), $"游戏版本下限 {min} 大于上限 {max}，CKAN 会认为该包不支持任何版本。"));
            }
        }
    }

    private static bool ValidateGameVersion(string? text, string field, string label, List<ModPackEditorIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (!GameVersion.TryParse(text.Trim(), out var parsed) || parsed is null)
        {
            issues.Add(Error($"{field}.invalid", field, $"{label} \"{text}\" 无法被 CKAN 解析（示例：1.12.5、1.12）。"));
            return false;
        }

        return true;
    }

    private static void ValidateLicenses(ModPackEditorDocument document, List<ModPackEditorIssue> issues)
    {
        if (document.Licenses is null || document.Licenses.Count == 0)
        {
            issues.Add(Error("license.empty", nameof(document.Licenses), "至少要选一个许可证（CKAN 的 license 是必填字段）。"));
            return;
        }

        foreach (var license in document.Licenses)
        {
            if (string.IsNullOrWhiteSpace(license))
            {
                issues.Add(Error("license.empty_item", nameof(document.Licenses), "许可证列表里有空值。"));
                continue;
            }

            if (!License.valid_licenses.Contains(license.Trim()))
            {
                issues.Add(Error("license.invalid", nameof(document.Licenses), $"许可证 \"{license}\" 不在 CKAN 许可证白名单里（例如：MIT、GPL-3.0、CC-BY-4.0、unknown）。"));
            }
        }
    }

    private static void ValidateEnums(ModPackEditorDocument document, List<ModPackEditorIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(document.ReleaseStatus))
        {
            issues.Add(Error("release_status.empty", nameof(document.ReleaseStatus), "发布状态不能为空（stable / testing / development）。"));
        }
        else if (!ReleaseStatuses.Contains(document.ReleaseStatus.Trim()))
        {
            issues.Add(Error("release_status.invalid", nameof(document.ReleaseStatus), $"发布状态 \"{document.ReleaseStatus}\" 不合法，只能是 stable / testing / development。"));
        }

        if (string.IsNullOrWhiteSpace(document.Kind))
        {
            issues.Add(Error("kind.empty", nameof(document.Kind), "包类型不能为空（metapackage / package / dlc）。"));
        }
        else if (!Kinds.Contains(document.Kind.Trim()))
        {
            issues.Add(Error("kind.invalid", nameof(document.Kind), $"包类型 \"{document.Kind}\" 不合法，只能是 metapackage / package / dlc。"));
        }
    }

    private static void ValidateDownload(ModPackEditorDocument document, List<ModPackEditorIssue> issues)
    {
        var kind = (document.Kind ?? string.Empty).Trim();
        var download = (document.DownloadUrl ?? string.Empty).Trim();

        if (kind == "package" && download.Length == 0)
        {
            issues.Add(Error("download.required", nameof(document.DownloadUrl), "包类型是 package 时必须填写下载地址（只有 metapackage 可以不填）。"));
            return;
        }

        if (download.Length == 0)
        {
            return;
        }

        if (!Uri.TryCreate(download, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            issues.Add(Error("download.invalid", nameof(document.DownloadUrl), $"下载地址 \"{download}\" 不是合法的 http / https 绝对地址。"));
        }
    }

    private static void ValidateRelations(ModPackEditorDocument document, List<ModPackEditorIssue> issues)
    {
        if (document.Relations is null || document.Relations.Count == 0)
        {
            return;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < document.Relations.Count; index++)
        {
            var relation = document.Relations[index];
            if (relation is null)
            {
                issues.Add(Error("relation.null", nameof(document.Relations), $"第 {index + 1} 行关系是空的。", index));
                continue;
            }

            var type = (relation.RelationType ?? string.Empty).Trim();
            if (!ModPackRelationTypes.All.Contains(type))
            {
                issues.Add(Error("relation.type", nameof(ModPackRelation.RelationType), $"第 {index + 1} 行的关系类型 \"{relation.RelationType}\" 不合法，只能是 depends / recommends / suggests / conflicts / supports。", index));
                continue;
            }

            var mode = (relation.Mode ?? string.Empty).Trim();
            switch (mode)
            {
                case ModPackRelationModes.Single:
                    ValidateSingleRelation(relation, type, index, issues, seen);
                    break;
                case ModPackRelationModes.Multi:
                case ModPackRelationModes.AnyOf:
                    ValidateCandidateRelation(relation, type, mode, index, issues, seen);
                    break;
                default:
                    issues.Add(Error("relation.mode", nameof(ModPackRelation.Mode), $"第 {index + 1} 行的语义模式 \"{relation.Mode}\" 不合法，只能是 single / multi / anyOf。", index));
                    break;
            }
        }
    }

    private static void ValidateSingleRelation(
        ModPackRelation relation,
        string type,
        int index,
        List<ModPackEditorIssue> issues,
        HashSet<string> seen)
    {
        var name = (relation.Name ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            issues.Add(Error("relation.name.empty", nameof(ModPackRelation.Name), $"第 {index + 1} 行没有填写模组标识。", index));
            return;
        }

        ValidateRelationName(name, index, issues);

        if (!seen.Add($"{type}|{ModPackRelationModes.Single}|{name}"))
        {
            issues.Add(Warning("relation.duplicate", nameof(ModPackRelation.Name), $"第 {index + 1} 行的 {type} 里重复出现 \"{name}\"。", index));
        }

        var hasMin = ValidateRelationVersion(relation.MinVersion, "版本下限", index, issues);
        var hasMax = ValidateRelationVersion(relation.MaxVersion, "版本上限", index, issues);
        if (hasMin && hasMax && ModPackVersions.Compare(relation.MinVersion, relation.MaxVersion) > 0)
        {
            issues.Add(Warning("relation.range", nameof(ModPackRelation.MinVersion), $"第 {index + 1} 行的版本下限 {relation.MinVersion} 大于上限 {relation.MaxVersion}。", index));
        }
    }

    private static void ValidateCandidateRelation(
        ModPackRelation relation,
        string type,
        string mode,
        int index,
        List<ModPackEditorIssue> issues,
        HashSet<string> seen)
    {
        var candidates = relation.Candidates ?? Array.Empty<string>();
        var cleaned = candidates.Where(candidate => !string.IsNullOrWhiteSpace(candidate)).Select(candidate => candidate.Trim()).ToList();
        if (cleaned.Count == 0)
        {
            issues.Add(Error("relation.candidates.empty", nameof(ModPackRelation.Candidates), $"第 {index + 1} 行没有填写候选模组标识。", index));
            return;
        }

        foreach (var candidate in cleaned)
        {
            ValidateRelationName(candidate, index, issues);
        }

        if (mode == ModPackRelationModes.AnyOf && cleaned.Count < 2)
        {
            issues.Add(Warning("relation.any_of.single", nameof(ModPackRelation.Candidates), $"第 {index + 1} 行是\"任意一个\"但只有 1 个候选，等价于单选，建议改用单选模式。", index));
        }

        var key = $"{type}|{mode}|{string.Join(",", cleaned.OrderBy(item => item, StringComparer.Ordinal))}";
        if (!seen.Add(key))
        {
            issues.Add(Warning("relation.duplicate", nameof(ModPackRelation.Candidates), $"第 {index + 1} 行的 {type} 与前面的候选集合重复。", index));
        }
    }

    private static void ValidateRelationName(string name, int index, List<ModPackEditorIssue> issues)
    {
        if (!string.Equals(name, name.ToLowerInvariant(), StringComparison.Ordinal))
        {
            issues.Add(Error("relation.name.lowercase", nameof(ModPackRelation.Name), $"第 {index + 1} 行的模组标识 \"{name}\" 必须全部小写。", index));
        }
        else if (!Identifier.ValidIdentifierPattern.IsMatch(name))
        {
            issues.Add(Error("relation.name.charset", nameof(ModPackRelation.Name), $"第 {index + 1} 行的模组标识 \"{name}\" 不合法：只能是小写字母、数字与连字符，至少 2 个字符。", index));
        }
    }

    private static bool ValidateRelationVersion(string? text, string label, int index, List<ModPackEditorIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (!IsParseableVersion(text))
        {
            issues.Add(Error("relation.version.invalid", nameof(ModPackRelation.MinVersion), $"第 {index + 1} 行的{label} \"{text}\" 含有 CKAN 不接受的字符（只允许 ASCII 字母、数字与 . + - _）。", index));
            return false;
        }

        return true;
    }

    /// <summary>
    /// 按 <c>Spec.md</c> 的版本写法判断：可选 <c>epoch:</c> 前缀 + 只含 ASCII 字母、数字与 <c>. + - _</c> 的主体。
    /// 上游 <c>ModuleVersion</c> 对任何字符串都不报错，故必须在此自己把关。
    /// </summary>
    private static bool IsParseableVersion(string? text)
        => !string.IsNullOrWhiteSpace(text) && VersionPattern.IsMatch(text.Trim());

    private static ModPackEditorIssue Error(string code, string field, string message, int rowIndex = -1)
        => new()
        {
            Code = code,
            Field = field,
            Message = message,
            Severity = ModPackEditorIssueSeverity.Error,
            RowIndex = rowIndex,
        };

    private static ModPackEditorIssue Warning(string code, string field, string message, int rowIndex = -1)
        => new()
        {
            Code = code,
            Field = field,
            Message = message,
            Severity = ModPackEditorIssueSeverity.Warning,
            RowIndex = rowIndex,
        };
}
