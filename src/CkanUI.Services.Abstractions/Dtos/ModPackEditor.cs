using System;
using System.Collections.Generic;

namespace CkanUI.Services.Abstractions.Dtos;

/// <summary>
/// 关系行的语义模式：单选一个模组 / 多个模组都要 / 任意一个候选（上游 <c>any_of</c>）。
/// </summary>
public static class ModPackRelationModes
{
    /// <summary>一个模组标识（可带版本下限 / 上限）。上游形状：<c>{"name": "..."}</c>。</summary>
    public const string Single = "single";

    /// <summary>多个模组都要；写盘时展开成多条 <see cref="Single"/> 行，因此不会引入上游没有的字段。</summary>
    public const string Multi = "multi";

    /// <summary>任意一个候选（上游 <c>{"any_of": [{"name": "..."}]}</c>）。</summary>
    public const string AnyOf = "anyOf";

    /// <summary>全部模式（界面下拉用）。</summary>
    public static IReadOnlyList<string> All { get; } = new[] { Single, Multi, AnyOf };
}

/// <summary>关系行所属的上游字段名：<c>depends</c> / <c>recommends</c> / <c>suggests</c> / <c>conflicts</c> / <c>supports</c>。</summary>
public static class ModPackRelationTypes
{
    /// <summary>前置依赖。</summary>
    public const string Depends = "depends";

    /// <summary>推荐安装。</summary>
    public const string Recommends = "recommends";

    /// <summary>建议安装。</summary>
    public const string Suggests = "suggests";

    /// <summary>冲突（不可共存）。</summary>
    public const string Conflicts = "conflicts";

    /// <summary>支持的游戏版本（<c>supports</c>）。</summary>
    public const string Supports = "supports";

    /// <summary>全部关系类型（界面下拉 / 校验用）。</summary>
    public static IReadOnlyList<string> All { get; } = new[] { Depends, Recommends, Suggests, Conflicts, Supports };
}

/// <summary>
/// 编辑器里的一行关系。<see cref="Mode"/> 决定 <see cref="Name"/> 还是 <see cref="Candidates"/> 生效。
/// </summary>
public sealed record ModPackRelation
{
    /// <summary>关系类型，取值见 <see cref="ModPackRelationTypes"/>。</summary>
    public string RelationType { get; init; } = ModPackRelationTypes.Depends;

    /// <summary>语义模式，取值见 <see cref="ModPackRelationModes"/>。</summary>
    public string Mode { get; init; } = ModPackRelationModes.Single;

    /// <summary>单选模式下的模组标识；多选 / 任意模式留空。</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>版本下限（可空，仅单选模式生效）。</summary>
    public string MinVersion { get; init; } = string.Empty;

    /// <summary>版本上限（可空，仅单选模式生效）。</summary>
    public string MaxVersion { get; init; } = string.Empty;

    /// <summary>多选 / 任意模式下的候选标识列表；单选模式忽略。</summary>
    public IReadOnlyList<string> Candidates { get; init; } = Array.Empty<string>();
}

/// <summary>校验问题级别。</summary>
public enum ModPackEditorIssueSeverity
{
    /// <summary>致命：存在任意一条就拒绝写盘。</summary>
    Error = 0,

    /// <summary>提醒：照样写盘，只在界面上提示。</summary>
    Warning = 1,
}

/// <summary>一条校验问题（中文原因 + 出错字段 + 出错行）。</summary>
public sealed record ModPackEditorIssue
{
    /// <summary>稳定的问题代码（如 <c>identifier.empty</c>），方便测试与界面分组。</summary>
    public string Code { get; init; } = string.Empty;

    /// <summary>出错字段名（与 <see cref="ModPackEditorDocument"/> 的属性名一致），整文档级问题用空串。</summary>
    public string Field { get; init; } = string.Empty;

    /// <summary>中文原因，直接展示给用户。</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>级别。</summary>
    public ModPackEditorIssueSeverity Severity { get; init; } = ModPackEditorIssueSeverity.Error;

    /// <summary>出错的关系行下标；非关系行问题为 -1。</summary>
    public int RowIndex { get; init; } = -1;
}

/// <summary>
/// 编辑中的 <c>.ckan</c> 元数据包文档。
/// </summary>
/// <remarks>
/// 字段名与上游 <c>CKAN.CkanModule</c> 的 JSON 属性一一对应（<c>identifier</c> / <c>name</c> / <c>abstract</c> /
/// <c>description</c> / <c>author</c> / <c>version</c> / <c>license</c> / <c>release_status</c> / <c>kind</c> /
/// <c>ksp_version</c> / <c>ksp_version_min</c> / <c>ksp_version_max</c> / <c>download</c> / <c>spec_version</c> 与五个关系数组），
/// 序列化由服务层直接交给上游 <c>CkanModule.ToJson()</c>，本 DTO 不发明任何字段名。
/// </remarks>
public sealed record ModPackEditorDocument
{
    /// <summary>目标 <c>.ckan</c> 文件路径（保存的落点；加载时回填）。</summary>
    public string FilePath { get; init; } = string.Empty;

    /// <summary>模组标识（<c>identifier</c>，小写字母 / 数字 / 连字符）。</summary>
    public string Identifier { get; init; } = string.Empty;

    /// <summary>显示名（<c>name</c>）。</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>一句话摘要（<c>abstract</c>）。</summary>
    public string Abstract { get; init; } = string.Empty;

    /// <summary>详细描述（<c>description</c>，可空）。</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>作者列表（<c>author</c>，上游是字符串数组）。</summary>
    public IReadOnlyList<string> Authors { get; init; } = Array.Empty<string>();

    /// <summary>版本号（<c>version</c>，必须能被 <c>ModuleVersion</c> 解析）。</summary>
    public string Version { get; init; } = string.Empty;

    /// <summary>许可证列表（<c>license</c>，必须是 CKAN 许可证枚举里的值）。</summary>
    public IReadOnlyList<string> Licenses { get; init; } = Array.Empty<string>();

    /// <summary>发布状态（<c>release_status</c>）：<c>stable</c> / <c>testing</c> / <c>development</c>。</summary>
    public string ReleaseStatus { get; init; } = "stable";

    /// <summary>包类型（<c>kind</c>）：<c>metapackage</c> / <c>package</c> / <c>dlc</c>。</summary>
    public string Kind { get; init; } = "metapackage";

    /// <summary>目标游戏版本（<c>ksp_version</c>，可空）。</summary>
    public string KspVersion { get; init; } = string.Empty;

    /// <summary>目标游戏版本下限（<c>ksp_version_min</c>，可空）。</summary>
    public string KspVersionMin { get; init; } = string.Empty;

    /// <summary>目标游戏版本上限（<c>ksp_version_max</c>，可空）。</summary>
    public string KspVersionMax { get; init; } = string.Empty;

    /// <summary>元数据规范版本（<c>spec_version</c>）；空表示保存时按上游 <c>SpecVersionAnalyzer</c> 自动推导。</summary>
    public string SpecVersion { get; init; } = string.Empty;

    /// <summary>下载地址（<c>download</c>，仅 <c>kind=package</c> 必填）。</summary>
    public string DownloadUrl { get; init; } = string.Empty;

    /// <summary>全部关系行（按 <see cref="ModPackRelation.RelationType"/> 分派到五个关系数组）。</summary>
    public IReadOnlyList<ModPackRelation> Relations { get; init; } = Array.Empty<ModPackRelation>();
}
