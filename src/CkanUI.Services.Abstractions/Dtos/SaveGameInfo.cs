namespace CkanUI.Services.Abstractions.Dtos;

/// <summary>
/// 存档槽信息（13 篇 §7.1 索引 / 03 篇 §14.2 列表列）。
/// 列表只读 <c>.loadmeta</c>（379 字节）与 <c>.sfs</c> 头部 200 行，绝不加载全文。
/// </summary>
/// <param name="SlotName">槽位名：子目录名 / 文件名（备份文件带 <c>.sfs.bak</c> 后缀已剥离）。</param>
/// <param name="FilePath">存档文件绝对路径（<c>*.sfs</c> 或 <c>*.sfs.bak</c>）。</param>
/// <param name="SizeBytes">文件字节数。</param>
/// <param name="LastWriteTime">文件修改时间（UTC）。</param>
/// <param name="Title"><c>GAME.Title</c>；取不到为 null。</param>
/// <param name="Mode">游戏模式字面（<c>SANDBOX</c>/<c>CAREER</c>/<c>SCIENCE</c>）。</param>
/// <param name="GameVersion"><c>kspVersion</c> / <c>GAME.version</c>。</param>
/// <param name="Scene"><c>GAME.scene</c> 字面。</param>
/// <param name="UniversalTime"><c>UT</c> 原字面（禁止回写，13 篇 L1）。</param>
/// <param name="VesselCount"><c>.loadmeta.vesselCount</c>。</param>
/// <param name="Funds"><c>.loadmeta.funds</c> 原字面。</param>
/// <param name="SnapshotCount">同目录下的写前快照数（<c>*.pre-edit-*.sfs</c> + <c>Backup/*.sfs</c>）。</param>
/// <param name="IsBackup">是否为 <c>*.sfs.bak</c> 备份文件。</param>
/// <param name="HasLoadMeta">是否成功读到 <c>.loadmeta</c>（false 表示走头部降级路径）。</param>
public sealed record SaveGameInfo(
    string SlotName,
    string FilePath,
    long SizeBytes,
    DateTimeOffset LastWriteTime,
    string? Title,
    string? Mode,
    string? GameVersion,
    string? Scene,
    string? UniversalTime,
    int? VesselCount,
    string? Funds,
    int SnapshotCount,
    bool IsBackup,
    bool HasLoadMeta);
