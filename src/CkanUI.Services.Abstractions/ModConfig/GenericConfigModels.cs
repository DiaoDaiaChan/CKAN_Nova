namespace CkanUI.Services.Abstractions.ModConfig;

/// <summary>
/// 一个可被通用 ConfigNode 编辑器打开的 cfg 文件（扫描阶段产出的轻量描述，不含文档正文）。
/// <para>
/// 与 <see cref="ModConfigFileSnapshot"/> 的区别：这里不依赖任何 adapter 包——只要游戏目录里
/// 存在疑似 mod 自己的 cfg，就能列出来读/改，用于「没有适配器」的兜底路径。
/// </para>
/// </summary>
/// <param name="FilePath">绝对路径（已归一化）。</param>
/// <param name="RelativePath">相对 <c>&lt;gameDir&gt;/GameData</c> 的路径，统一用 <c>/</c> 分隔。</param>
/// <param name="ModName">GameData 下的第一段目录名（即 mod 目录）。</param>
/// <param name="DisplayName">给人看的名字，形如 <c>ModName · 相对路径</c>。</param>
/// <param name="RootNode">文件里第一个 ConfigNode 节点名；无法确定（扁平键值文件/空文件）时为 <c>null</c>。</param>
/// <param name="PairCount">扫描时解析出的键值对数量；未解析时为 0（见 <see cref="GenericConfigScanner"/> 注释）。</param>
/// <param name="SizeBytes">文件字节数。</param>
/// <param name="LastWriteUtc">最后写入时间（UTC）。</param>
/// <param name="IsWritable">当前是否允许写回（只读属性、解析错误等都会让它变 false）。</param>
/// <param name="NotWritableReason">不可写的原因（人话，直接可展示）。</param>
public sealed record GenericConfigFile(
    string FilePath,
    string RelativePath,
    string ModName,
    string DisplayName,
    string? RootNode,
    int PairCount,
    long SizeBytes,
    DateTime LastWriteUtc,
    bool IsWritable,
    string? NotWritableReason);

/// <summary>一次目录扫描的结果。</summary>
/// <param name="GameDir">归一化后的游戏目录（绝对路径，结尾不带分隔符）。</param>
/// <param name="Files">命中的 cfg 文件，按 <see cref="GenericConfigFile.RelativePath"/> 升序。</param>
/// <param name="SkippedFileCount">因为是原版定义/ModuleManager 补丁/备份文件等原因被跳过的文件数。</param>
/// <param name="Diagnostics">截断、跳过、无法读取等可展示的说明，按发生顺序。</param>
public sealed record GenericConfigCatalog(
    string GameDir,
    IReadOnlyList<GenericConfigFile> Files,
    int SkippedFileCount,
    IReadOnlyList<string> Diagnostics);

/// <summary>写回结果。</summary>
/// <param name="FilePath">实际写入的目标文件绝对路径。</param>
/// <param name="BackupPath">写前备份的绝对路径；目标文件原本不存在（新建）时为空串。</param>
/// <param name="ChangedLineCount">本次写回的行数，等于 <c>document.PendingEdits.Count</c>。</param>
/// <param name="FileWasCreated">目标文件是否是本次新建的（原本不存在）。</param>
public sealed record GenericConfigSaveResult(
    string FilePath,
    string BackupPath,
    int ChangedLineCount,
    bool FileWasCreated);
