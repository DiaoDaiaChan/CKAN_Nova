using System.Collections.Generic;

namespace CkanUI.Services.Abstractions.ModConfig;

/// <summary>
/// 通用配置文档里的一行键值（无损编辑的单位）。
/// </summary>
/// <param name="LineNumber">值所在行号（1 基，写回时按它定位，不靠键名做字符串搜索）。</param>
/// <param name="Depth">节点深度（0 是文件根层）。</param>
/// <param name="NodePath">所在节点路径，形如 <c>SETTINGS/Physics</c>；根层为空串。</param>
/// <param name="Key">键名。</param>
/// <param name="Value">当前值（原文，未做类型转换）。</param>
/// <param name="Comment">同一行末尾的注释（含 <c>//</c>），没有则为 null。</param>
public sealed record GenericConfigRow(
    int LineNumber,
    int Depth,
    string NodePath,
    string Key,
    string Value,
    string? Comment);

/// <summary>
/// 通用配置文档的只读快照：页面拿它铺行，改完把改动交回 <see cref="IGenericConfigEditService.SaveAsync"/>。
/// <para>
/// 刻意不暴露 ConfigNode 文档类型：它属于 CkanUI.Services 工程，而消费方是插件工程
/// （插件不引用宿主服务实现，只引用 Abstractions）。
/// </para>
/// </summary>
/// <param name="FilePath">绝对路径（已归一化）。</param>
/// <param name="RelativePath">相对 GameData（不在 GameData 内时相对游戏目录）的路径，<c>/</c> 分隔。</param>
/// <param name="CanWrite">当前是否允许写回。</param>
/// <param name="ReadonlyReason">不可写原因（人话）。</param>
/// <param name="Rows">键值行，按文件出现顺序。</param>
/// <param name="ParseErrorCount">ConfigNode 解析错误数（大于 0 时只读）。</param>
public sealed record GenericConfigDocumentView(
    string FilePath,
    string RelativePath,
    bool CanWrite,
    string? ReadonlyReason,
    IReadOnlyList<GenericConfigRow> Rows,
    int ParseErrorCount);

/// <summary>一条待写回的改动：不变量 = 只改这一行的值片段。</summary>
/// <param name="LineNumber">行号（取自 <see cref="GenericConfigRow.LineNumber"/>）。</param>
/// <param name="NewValue">新的值文本；null 表示把值清空（保留键名）。</param>
public sealed record GenericConfigChange(int LineNumber, string? NewValue);
