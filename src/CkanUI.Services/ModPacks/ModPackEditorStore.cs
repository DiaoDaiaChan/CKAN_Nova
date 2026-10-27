using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using CKAN;
using CKAN.Versioning;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.Services.ModPacks;

/// <summary>
/// <c>.ckan</c> 编辑器的读写层（UI-24）。
/// </summary>
/// <remarks>
/// 序列化完全交给上游：读用 <c>new CkanModule(json, comparator)</c>，写用 <c>CkanModule.ToJson()</c>。
/// 因此字段名、字段顺序、<c>license</c> 的单值/数组两种写法、关系行的 <c>name</c> / <c>any_of</c> 形状
/// 都与 CKAN 自己产出的文件一致，不引入本仓库发明的任何字段。
/// 写盘用"同目录 .tmp + <c>File.Move(overwrite: true)</c>"，避免半个文件覆盖掉用户原有的 .ckan。
/// </remarks>
public sealed class ModPackEditorStore
{
    /// <summary>
    /// 显式注入 <see cref="StrictGameComparator"/>：<c>CkanModule</c> 的构造函数在 comparator 为 null 时
    /// 会去 <c>ServiceLocator.Container</c>（Autofac）里解析，测试环境没有容器会直接抛。
    /// </summary>
    private static readonly StrictGameComparator Comparator = new();

    private const string DefaultSpecVersion = "v1.6";

    /// <summary>读取一个 <c>.ckan</c> 文件并投影成编辑器文档。</summary>
    public OperationResult<ModPackEditorDocument> Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return OperationResult<ModPackEditorDocument>.Fail("请填写要打开的 .ckan 文件路径。");
        }

        var fullPath = Path.GetFullPath(path.Trim());
        if (!File.Exists(fullPath))
        {
            return OperationResult<ModPackEditorDocument>.Fail($"找不到文件：{fullPath}");
        }

        string json;
        try
        {
            json = File.ReadAllText(fullPath);
        }
        catch (Exception exception)
        {
            return OperationResult<ModPackEditorDocument>.Fail("读取 .ckan 文件失败。", exception.Message);
        }

        try
        {
            var module = new CkanModule(json, Comparator);
            return OperationResult<ModPackEditorDocument>.Ok(Project(module, fullPath));
        }
        catch (Exception exception)
        {
            return OperationResult<ModPackEditorDocument>.Fail(
                "这个文件不是合法的 CKAN 元数据包（.ckan），无法打开。",
                exception.Message);
        }
    }

    /// <summary>校验并写回 <c>.ckan</c> 文件；有 Error 级问题时一个字节都不写。</summary>
    public OperationResult<string> Save(ModPackEditorDocument? document)
    {
        if (document is null)
        {
            return OperationResult<string>.Fail("没有可保存的文档。");
        }

        var issues = ModPackEditorValidator.Validate(document);
        if (ModPackEditorValidator.HasError(issues))
        {
            var reasons = issues
                .Where(issue => issue.Severity == ModPackEditorIssueSeverity.Error)
                .Select(issue => "· " + issue.Message);
            return OperationResult<string>.Fail(
                "校验未通过，没有写入文件：",
                string.Join(Environment.NewLine, reasons));
        }

        if (string.IsNullOrWhiteSpace(document.FilePath))
        {
            return OperationResult<string>.Fail("请填写 .ckan 文件的保存路径。");
        }

        var fullPath = NormalizeTargetPath(document.FilePath);

        string json;
        try
        {
            json = BuildModule(document).ToJson();
        }
        catch (Exception exception)
        {
            return OperationResult<string>.Fail("生成 .ckan 内容失败。", exception.Message);
        }

        try
        {
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var tempPath = fullPath + ".tmp";
            File.WriteAllText(tempPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(tempPath, fullPath, overwrite: true);
            return OperationResult<string>.Ok(fullPath);
        }
        catch (Exception exception)
        {
            return OperationResult<string>.Fail("写入 .ckan 文件失败。", exception.Message);
        }
    }

    /// <summary>把保存路径规整成绝对路径，并补齐 <c>.ckan</c> 后缀。</summary>
    public static string NormalizeTargetPath(string path)
    {
        var trimmed = path.Trim();
        if (!trimmed.EndsWith(".ckan", StringComparison.OrdinalIgnoreCase))
        {
            trimmed += ".ckan";
        }

        return Path.GetFullPath(trimmed);
    }

    /// <summary>把编辑器文档装配成上游 <c>CkanModule</c>（只做装配，不落盘）。</summary>
    public static CkanModule BuildModule(ModPackEditorDocument document)
    {
        var kind = ParseKind(document.Kind);
        var download = string.IsNullOrWhiteSpace(document.DownloadUrl)
            ? null
            : new List<Uri> { new Uri(document.DownloadUrl.Trim(), UriKind.Absolute) };

        var module = new CkanModule(
            new ModuleVersion(string.IsNullOrWhiteSpace(document.SpecVersion) ? DefaultSpecVersion : document.SpecVersion.Trim()),
            document.Identifier.Trim(),
            document.Name.Trim(),
            document.Abstract.Trim(),
            string.IsNullOrWhiteSpace(document.Description) ? null : document.Description,
            document.Authors.Select(author => author.Trim()).ToList(),
            document.Licenses.Select(license => new License(license.Trim())).ToList(),
            new ModuleVersion(document.Version.Trim()),
            download,
            kind,
            Comparator);

        module.depends = BuildRelations(document, ModPackRelationTypes.Depends);
        module.recommends = BuildRelations(document, ModPackRelationTypes.Recommends);
        module.suggests = BuildRelations(document, ModPackRelationTypes.Suggests);
        module.conflicts = BuildRelations(document, ModPackRelationTypes.Conflicts);
        module.supports = BuildRelations(document, ModPackRelationTypes.Supports);

        module.release_status = ParseReleaseStatus(document.ReleaseStatus);
        // 上游 CkanModule 在 ksp_version 与 ksp_version_(min|max) 同时存在时直接抛
        // InvalidModuleAttributesKraken（文件写得出、读不回），所以有上下限时绝不再写精确版本。
        var kspMin = ParseGameVersion(document.KspVersionMin);
        var kspMax = ParseGameVersion(document.KspVersionMax);
        module.ksp_version = kspMin is null && kspMax is null ? ParseGameVersion(document.KspVersion) : null;
        module.ksp_version_min = kspMin;
        module.ksp_version_max = kspMax;

        // 与 RegistryManager.GenerateModpack 一致：spec_version 由上游按实际用到的特性反推。
        module.spec_version = SpecVersionAnalyzer.MinimumSpecVersion(module);
        return module;
    }

    /// <summary>把关系行投影回文档；<c>multi</c> 在读侧不会出现（写侧已展开成多条 single）。</summary>
    private static ModPackEditorDocument Project(CkanModule module, string fullPath) => new()
    {
        FilePath = fullPath,
        Identifier = module.identifier ?? string.Empty,
        Name = module.name ?? string.Empty,
        Abstract = module.@abstract ?? string.Empty,
        Description = module.description ?? string.Empty,
        Authors = (module.author ?? new List<string>()).Where(author => !string.IsNullOrWhiteSpace(author)).ToList(),
        Version = module.version?.ToString() ?? string.Empty,
        Licenses = (module.license ?? new List<License>()).Select(license => license.ToString()).ToList(),
        ReleaseStatus = module.release_status?.ToString() ?? "stable",
        Kind = module.kind.ToString(),
        KspVersion = module.ksp_version?.ToString() ?? string.Empty,
        KspVersionMin = module.ksp_version_min?.ToString() ?? string.Empty,
        KspVersionMax = module.ksp_version_max?.ToString() ?? string.Empty,
        SpecVersion = module.spec_version?.ToString() ?? string.Empty,
        DownloadUrl = module.download?.FirstOrDefault()?.ToString() ?? string.Empty,
        Relations = ProjectRelations(module),
    };

    private static IReadOnlyList<ModPackRelation> ProjectRelations(CkanModule module)
    {
        var relations = new List<ModPackRelation>();
        Collect(module.depends, ModPackRelationTypes.Depends, relations);
        Collect(module.recommends, ModPackRelationTypes.Recommends, relations);
        Collect(module.suggests, ModPackRelationTypes.Suggests, relations);
        Collect(module.conflicts, ModPackRelationTypes.Conflicts, relations);
        Collect(module.supports, ModPackRelationTypes.Supports, relations);
        return relations;
    }

    private static void Collect(List<RelationshipDescriptor>? descriptors, string relationType, List<ModPackRelation> relations)
    {
        if (descriptors is null)
        {
            return;
        }

        foreach (var descriptor in descriptors)
        {
            switch (descriptor)
            {
                case AnyOfRelationshipDescriptor anyOf:
                    relations.Add(new ModPackRelation
                    {
                        RelationType = relationType,
                        Mode = ModPackRelationModes.AnyOf,
                        Candidates = (anyOf.any_of ?? new List<RelationshipDescriptor>())
                            .OfType<ModuleRelationshipDescriptor>()
                            .Where(item => !string.IsNullOrWhiteSpace(item.name))
                            .Select(item => item.name.Trim())
                            .ToList(),
                    });
                    break;

                case ModuleRelationshipDescriptor single:
                    relations.Add(new ModPackRelation
                    {
                        RelationType = relationType,
                        Mode = ModPackRelationModes.Single,
                        Name = single.name ?? string.Empty,
                        MinVersion = ExactVersionOr(single, single.min_version),
                        MaxVersion = ExactVersionOr(single, single.max_version),
                    });
                    break;
            }
        }
    }

    /// <summary>
    /// 上游 <c>ModuleRelationshipDescriptor</c> 有 <c>version</c>（精确版本）与 <c>min_version</c> / <c>max_version</c> 三种写法。
    /// 编辑器只暴露上下限：读到精确版本时把上下限都填成该版本（语义等价），写回时再折叠回 <c>version</c>。
    /// </summary>
    private static string ExactVersionOr(ModuleRelationshipDescriptor descriptor, ModuleVersion? own)
        => descriptor.version?.ToString() ?? own?.ToString() ?? string.Empty;

    private static List<RelationshipDescriptor>? BuildRelations(ModPackEditorDocument document, string relationType)
    {
        var rows = document.Relations
            .Where(relation => relation is not null
                               && string.Equals((relation.RelationType ?? string.Empty).Trim(), relationType, StringComparison.Ordinal))
            .ToList();

        if (rows.Count == 0)
        {
            // 空数组会让 CKAN 在 spec < v1.26 的文件里多出无意义的键，保持 null 让上游按 NullValueHandling.Ignore 跳过。
            return null;
        }

        var descriptors = new List<RelationshipDescriptor>();
        foreach (var row in rows)
        {
            var mode = (row.Mode ?? string.Empty).Trim();
            if (mode == ModPackRelationModes.AnyOf)
            {
                var anyOf = new AnyOfRelationshipDescriptor
                {
                    any_of = (row.Candidates ?? Array.Empty<string>())
                        .Where(candidate => !string.IsNullOrWhiteSpace(candidate))
                        .Select< string, RelationshipDescriptor>(candidate => new ModuleRelationshipDescriptor { name = candidate.Trim() })
                        .ToList(),
                };
                descriptors.Add(anyOf);
            }
            else if (mode == ModPackRelationModes.Multi)
            {
                // 多选在写盘时展开成多条单选，避免引入上游没有的字段。
                foreach (var candidate in (row.Candidates ?? Array.Empty<string>()).Where(candidate => !string.IsNullOrWhiteSpace(candidate)))
                {
                    descriptors.Add(new ModuleRelationshipDescriptor { name = candidate.Trim() });
                }
            }
            else
            {
                descriptors.Add(BuildSingleDescriptor(row));
            }
        }

        return descriptors.Count == 0 ? null : descriptors;
    }

    private static ModuleRelationshipDescriptor BuildSingleDescriptor(ModPackRelation row)
    {
        var descriptor = new ModuleRelationshipDescriptor { name = (row.Name ?? string.Empty).Trim() };
        var min = ParseModuleVersion(row.MinVersion);
        var max = ParseModuleVersion(row.MaxVersion);

        if (min is not null && max is not null && min.Equals(max))
        {
            descriptor.version = min;
        }
        else
        {
            descriptor.min_version = min;
            descriptor.max_version = max;
        }

        return descriptor;
    }

    private static ModuleVersion? ParseModuleVersion(string? text)
        => string.IsNullOrWhiteSpace(text) ? null : new ModuleVersion(text.Trim());

    private static GameVersion? ParseGameVersion(string? text)
        => string.IsNullOrWhiteSpace(text) ? null : GameVersion.Parse(text.Trim());

    private static ModuleKind ParseKind(string? text)
        => (text ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "package" => ModuleKind.package,
            "dlc" => ModuleKind.dlc,
            _ => ModuleKind.metapackage,
        };

    private static ReleaseStatus ParseReleaseStatus(string? text)
        => (text ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "testing" => ReleaseStatus.testing,
            "development" => ReleaseStatus.development,
            _ => ReleaseStatus.stable,
        };
}
