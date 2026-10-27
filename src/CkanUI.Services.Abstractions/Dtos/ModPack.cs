namespace CkanUI.Services.Abstractions.Dtos;

/// <summary>
/// 导出请求（03 篇 §7 导出对话框 / 06 篇 UI-24）：把当前实例的已安装模组写成 <c>.ckan</c> 元数据包。
/// </summary>
/// <param name="TargetPath">目标文件路径；不以 <c>.ckan</c> 结尾时自动补上后缀。</param>
/// <param name="WithVersions">是否在依赖项里锁定精确版本（对应上游 <c>GenerateModpack(with_versions)</c>）。</param>
/// <param name="BundleDownloads">是否把缓存里已下载的模组 zip 一起打进同名 <c>.zip</c> 归档。</param>
/// <param name="UseRecommends">true 写 recommends（可选依赖），false 写 depends（对应上游 recommends 开关）。</param>
/// <summary>
/// 导出格式枚举（F-45 / UI-24）。
/// 支持 .ckan 元数据包（可回导）以及多种人类可读 / 外部交换格式（不可回导）。
/// </summary>
public enum ExportListFormat
{
    /// <summary>CKAN 元数据包（.ckan，依赖/推荐关系，完整可回导）。</summary>
    Ckan,

    /// <summary>纯文本列表（.txt，每行一个标识与版本）。</summary>
    PlainText,

    /// <summary>Markdown 表格/列表（.md，适用于文档与 GitHub Release）。</summary>
    Markdown,

    /// <summary>BBCode 列表（.txt / .bbcode，适用于 KSP 官方论坛贴）。</summary>
    BbCode,

    /// <summary>逗号分隔值（.csv，适用于 Excel / 电子表格）。</summary>
    Csv,

    /// <summary>制表符分隔值（.tsv，适用于文本表格分析）。</summary>
    Tsv,
}

/// <summary>
/// 导出模组列表（非 ckan 格式）的轻量请求（F-45 / UI-24）。
/// </summary>
/// <param name="TargetPath">目标文件完整路径。</param>
/// <param name="Format">导出格式（PlainText, Markdown, BbCode, Csv, Tsv）。</param>
public sealed record ExportInstalledListRequest(
    string TargetPath,
    ExportListFormat Format);

/// <summary>
/// 导出请求（03 篇 §7 导出对话框 / 06 篇 UI-24 / F-45）：把当前实例的已安装模组写成 <c>.ckan</c> 元数据包或文本列表。
/// </summary>
/// <param name="TargetPath">目标文件路径；省略后缀时自动补上对应格式的后缀。</param>
/// <param name="WithVersions">是否在依赖项里锁定精确版本（仅 .ckan 格式有效）。</param>
/// <param name="BundleDownloads">是否把缓存里已下载的模组 zip 一起打进同名 <c>.zip</c> 归档（仅 .ckan 格式有效）。</param>
/// <param name="UseRecommends">true 写 recommends，false 写 depends（仅 .ckan 格式有效）。</param>
/// <param name="Format">导出格式；默认 <see cref="ExportListFormat.Ckan"/>。</param>
public sealed record ModPackExportRequest(
    string TargetPath,
    bool WithVersions = true,
    bool BundleDownloads = false,
    bool UseRecommends = false,
    ExportListFormat Format = ExportListFormat.Ckan);

/// <summary>导入源里的一条模组请求：<c>.ckan</c> 的 depends / recommends 里的一项。</summary>
/// <param name="Identifier">模组标识。</param>
/// <param name="Version">要求的版本；null 表示"任意已安装版本都满足"。</param>
/// <param name="IsOptional">是否来自 recommends（可选依赖）。</param>
public sealed record ModPackReference(string Identifier, string? Version = null, bool IsOptional = false);

/// <summary>一个已解析的 <c>.ckan</c> 文件（由 Core 的 <c>CkanModule.FromFile</c> 投影而来）。</summary>
/// <param name="FilePath">文件绝对路径。</param>
/// <param name="Name">元数据包名。</param>
/// <param name="Version">元数据包自身版本。</param>
/// <param name="Entries">声明的模组条目（depends 在前、recommends 在后）。</param>
public sealed record ModPackFile(
    string FilePath,
    string Name,
    string Version,
    IReadOnlyList<ModPackReference> Entries)
{
    /// <summary>文件名（预览表的"来源"列）。</summary>
    public string FileName => System.IO.Path.GetFileName(FilePath);
}

/// <summary>仓库索引对某个模组的查询结果。</summary>
/// <param name="Exists">注册表里是否有这个模组。</param>
/// <param name="LatestVersion">最新可用版本；没有则为 null。</param>
/// <param name="RequestedVersionExists">请求的精确版本是否可用。</param>
public sealed record ModPackAvailability(bool Exists, string? LatestVersion, bool RequestedVersionExists)
{
    /// <summary>索引里完全不存在。</summary>
    public static ModPackAvailability NotFound { get; } = new(false, null, false);
}

/// <summary>导入预览中一行的处置方式。</summary>
public enum ModPackEntryState
{
    /// <summary>会安装。</summary>
    Install,

    /// <summary>会升级。</summary>
    Upgrade,

    /// <summary>会降级。</summary>
    Downgrade,

    /// <summary>已安装且版本相同，跳过。</summary>
    SameVersion,

    /// <summary>仓库索引里找不到这个模组。</summary>
    Missing,

    /// <summary>版本冲突或来源之间不一致，跳过。</summary>
    Conflict,
}

/// <summary>导入预览的一行（一个模组条目）。</summary>
/// <param name="Identifier">模组标识。</param>
/// <param name="SourceName">声明它的来源文件名。</param>
/// <param name="RequestedVersion">文件里要求的版本；null 表示任意版本。</param>
/// <param name="InstalledVersion">当前实例已安装的版本；未装为 null。</param>
/// <param name="TargetVersion">执行后会装上的版本；无法确定时为 null。</param>
/// <param name="State">处置方式。</param>
/// <param name="Reason">中文原因（问题行必填）。</param>
public sealed record ModPackEntry(
    string Identifier,
    string SourceName,
    string? RequestedVersion,
    string? InstalledVersion,
    string? TargetVersion,
    ModPackEntryState State,
    string? Reason)
{
    /// <summary>该行是否可以被勾选执行。</summary>
    public bool IsSelectable => State is ModPackEntryState.Install
        or ModPackEntryState.Upgrade
        or ModPackEntryState.Downgrade;

    /// <summary>问题行（缺失/冲突）：需要在界面上显式提示。</summary>
    public bool IsProblem => State is ModPackEntryState.Missing or ModPackEntryState.Conflict;
}

/// <summary>导入预览：列出会装 / 会升 / 会降 / 已装 / 找不到 / 冲突的条目，执行前不落盘。</summary>
/// <param name="Sources">参与预览的来源文件路径。</param>
/// <param name="Name">预览标题（单个文件用包名，多个文件用数量描述）。</param>
/// <param name="Entries">条目（问题行在前）。</param>
/// <param name="Conflicts">阻止安装的冲突说明。</param>
/// <param name="Warnings">被跳过的文件等非致命提示。</param>
public sealed record ModPackPreview(
    IReadOnlyList<string> Sources,
    string Name,
    IReadOnlyList<ModPackEntry> Entries,
    IReadOnlyList<string> Conflicts,
    IReadOnlyList<string> Warnings)
{
    /// <summary>没有任何条目。</summary>
    public bool IsEmpty => Entries.Count == 0;

    /// <summary>是否存在阻止执行的冲突。</summary>
    public bool HasConflicts => Conflicts.Count > 0;

    /// <summary>可以执行的条目数。</summary>
    public int SelectableCount
    {
        get
        {
            var count = 0;
            foreach (var entry in Entries)
            {
                if (entry.IsSelectable)
                {
                    count++;
                }
            }

            return count;
        }
    }
}

/// <summary>导出结果。</summary>
/// <param name="Path">写出的 <c>.ckan</c> 文件路径。</param>
/// <param name="ModuleCount">写进元数据包的模组条目数。</param>
/// <param name="ArchivePath">打包出的 <c>.zip</c> 路径；未打包为 null。</param>
/// <param name="BundledCount">打进归档的下载包数量。</param>
/// <param name="BundleBytes">打进归档的下载包总字节数。</param>
/// <param name="FileBytes">写出的 <c>.ckan</c> 文件字节数。</param>
/// <param name="Warnings">非致命提示（如缓存里缺少下载包）。</param>
public sealed record ModPackExportResult(
    string Path,
    int ModuleCount,
    string? ArchivePath,
    int BundledCount,
    long BundleBytes,
    long FileBytes,
    IReadOnlyList<string> Warnings);

/// <summary>导入执行结果（底层来自 <c>IInstallerService.ApplyAsync</c> 的安装报告）。</summary>
/// <param name="Installed">已安装/升级的模组标识。</param>
/// <param name="Failed">失败的模组标识。</param>
/// <param name="Warnings">过程警告。</param>
public sealed record ModPackImportReport(
    IReadOnlyList<string> Installed,
    IReadOnlyList<string> Failed,
    IReadOnlyList<string> Warnings)
{
    /// <summary>全部成功。</summary>
    public bool IsFullSuccess => Failed.Count == 0;
}

/// <summary>两个实例之间的一条差异。</summary>
public enum InstanceDiffKind
{
    /// <summary>只有左侧实例装了。</summary>
    OnlyInLeft,

    /// <summary>只有右侧实例装了。</summary>
    OnlyInRight,

    /// <summary>两边都装了但版本不同。</summary>
    VersionDiffers,
}

/// <summary>实例比对的一行。</summary>
/// <param name="Identifier">模组标识。</param>
/// <param name="Kind">差异种类。</param>
/// <param name="LeftVersion">左侧版本；没有则为 null。</param>
/// <param name="RightVersion">右侧版本；没有则为 null。</param>
public sealed record InstanceDiffItem(string Identifier, InstanceDiffKind Kind, string? LeftVersion, string? RightVersion);

/// <summary>两个实例的模组差异（仅 A 有 / 仅 B 有 / 版本不同）。</summary>
/// <param name="LeftName">左侧实例名。</param>
/// <param name="RightName">右侧实例名。</param>
/// <param name="Items">差异条目（按标识排序）。</param>
public sealed record InstanceComparison(string LeftName, string RightName, IReadOnlyList<InstanceDiffItem> Items)
{
    /// <summary>两个实例的模组集合完全一致。</summary>
    public bool IsIdentical => Items.Count == 0;
}
