using System.Collections.Generic;

using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.Services.Installers;

/// <summary>
/// 依赖求解产出的可执行计划。
/// 刻意不携带 CKAN 类型（CkanModule 等）：编排层只按标识符发号施令，
/// 具体版本选择留在后端内部，测试替身也就能直接构造它。
/// </summary>
/// <param name="Items">原始变更项（用于区分「用户点名」与「依赖带入」，决定 autoInstalled）。</param>
/// <param name="ToRemove">先卸载的标识符。</param>
/// <param name="ToInstall">解析后需要安装/升级的标识符（含依赖与推荐）。</param>
/// <param name="Warnings">求解阶段的警告（例如版本与请求不一致）。</param>
/// <param name="DownloadBytes">预计下载体积（字节，0 表示未知）。</param>
public sealed record InstallerPlan(
    IReadOnlyList<ChangeItemDto> Items,
    IReadOnlyList<string> ToRemove,
    IReadOnlyList<string> ToInstall,
    IReadOnlyList<string> Warnings,
    long DownloadBytes);
