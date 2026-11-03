using CkanUI.Services.Abstractions;

namespace CkanUI.Plugin.Kos.Scripts;

/// <summary>Archive 卷里的一个脚本文件（Ships/Script/*.ks）。</summary>
/// <param name="Name">文件名（含 .ks）。</param>
/// <param name="FullPath">磁盘全路径。</param>
/// <param name="SizeBytes">字节数。</param>
/// <param name="LastWriteUtc">最后写入时间（UTC）。</param>
/// <param name="LineCount">行数；读不到时为 null。</param>
/// <param name="IsBoot">是否是 kOS 的启动脚本 boot.ks。</param>
public sealed record KosScriptFile(
    string Name,
    string FullPath,
    long SizeBytes,
    DateTime LastWriteUtc,
    int? LineCount,
    bool IsBoot);

/// <summary>Archive 目录的一次扫描结果。</summary>
/// <param name="GameDir">实例的游戏目录。</param>
/// <param name="ScriptDirectory">Ships/Script 的全路径（目录不存在也给路径，页面要显示它）。</param>
/// <param name="DirectoryExists">Ships/Script 是否存在。</param>
/// <param name="ModInstalled">GameData/kOS 是否存在（决定空态是“没装 kOS”还是“装了但还没有脚本”）。</param>
/// <param name="Scripts">按文件名排序的脚本。</param>
/// <param name="Diagnostics">扫描期遇到的问题（读目录失败、单个文件读不到等）。</param>
public sealed record KosScriptCatalog(
    string GameDir,
    string ScriptDirectory,
    bool DirectoryExists,
    bool ModInstalled,
    IReadOnlyList<KosScriptFile> Scripts,
    IReadOnlyList<string> Diagnostics);

/// <summary>编辑器打开的一份脚本。</summary>
/// <param name="Name">文件名（含 .ks）。</param>
/// <param name="FullPath">磁盘全路径。</param>
/// <param name="Content">脚本正文（换行已统一成 \n，写回时按 <paramref name="NewLine"/> 还原）。</param>
/// <param name="NewLine">文件原本的换行风格（\r\n 或 \n）。</param>
/// <param name="HadBom">原文件是否带 UTF-8 BOM。</param>
/// <param name="SizeBytes">原文件字节数。</param>
/// <param name="LastWriteUtc">读取时的最后写入时间（UTC），保存时用来发现外部改动。</param>
/// <param name="IsBoot">是否是 boot.ks。</param>
public sealed record KosScriptDocument(
    string Name,
    string FullPath,
    string Content,
    string NewLine,
    bool HadBom,
    long SizeBytes,
    DateTime LastWriteUtc,
    bool IsBoot);

/// <summary>保存/新建/重命名/删除成功后的结果。</summary>
/// <param name="Name">最终文件名。</param>
/// <param name="FullPath">最终全路径。</param>
/// <param name="BackupPath">备份文件路径；新建时没有备份为 null。</param>
/// <param name="LineCount">写回后的行数。</param>
/// <param name="SizeBytes">写回后的字节数。</param>
public sealed record KosScriptSaveOutcome(
    string Name,
    string FullPath,
    string? BackupPath,
    int LineCount,
    long SizeBytes);

/// <summary>kOS Archive 卷的读写服务：只碰 &lt;gameDir&gt;/Ships/Script 下的 .ks 文件。</summary>
public interface IKosScriptService
{
    /// <summary>扫描 Ships/Script；目录不存在不是失败（空态要显示期望路径）。</summary>
    KosScriptCatalog LoadCatalog(string? gameDir);

    /// <summary>读取一个脚本；换行统一成 \n，同时返回原换行风格。</summary>
    OperationResult<KosScriptDocument> Load(string gameDir, string name);

    /// <summary>
    /// 保存已有脚本：先备份 &lt;名&gt;.bak-yyyyMMdd-HHmmss，再写临时文件并原子替换。
    /// </summary>
    /// <param name="expectedLastWriteUtc">打开时的最后写入时间；磁盘上变了就拒绝覆盖（防外部编辑器同时改）。</param>
    OperationResult<KosScriptSaveOutcome> Save(
        string gameDir,
        string name,
        string content,
        DateTime? expectedLastWriteUtc = null);

    /// <summary>新建脚本；同名文件已存在时失败，不覆盖。</summary>
    OperationResult<KosScriptSaveOutcome> Create(string gameDir, string name, string content);

    /// <summary>重命名；目标已存在时失败，不覆盖。</summary>
    OperationResult<string> Rename(string gameDir, string oldName, string newName);

    /// <summary>删除；先备份到同目录 .bak-yyyyMMdd-HHmmss，再删除原文件。</summary>
    OperationResult<string> Delete(string gameDir, string name);
}
