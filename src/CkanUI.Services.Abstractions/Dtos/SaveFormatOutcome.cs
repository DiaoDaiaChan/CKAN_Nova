namespace CkanUI.Services.Abstractions.Dtos;

/// <summary>
/// 格式化结果。被拒绝时 <see cref="WroteFile"/> 恒为 false —— 调用方据此保证"拒绝即不写盘"。
/// </summary>
/// <param name="SourcePath">源文件路径。</param>
/// <param name="Mode">落盘方式。</param>
/// <param name="Refused">是否被拒绝（解析错误 / 自校验未通过 / 快照建不出来）。</param>
/// <param name="RefusalReason">拒绝原因（中文，可直接展示）。</param>
/// <param name="WroteFile">是否真的写了文件。</param>
/// <param name="WrittenPath">写入的路径（未写则为 null）。</param>
/// <param name="BackupPath">写前快照路径（<see cref="SaveFormatMode.Overwrite"/> 成功时非 null）。</param>
/// <param name="Changed">格式化结果与源文本是否不同。</param>
/// <param name="SemanticsPreserved">内核自校验通过（值索引一致）。</param>
/// <param name="BytesBefore">源文件字节数。</param>
/// <param name="BytesAfter">格式化后字节数。</param>
/// <param name="Stats">格式化统计。</param>
/// <param name="Preview">预览片段（仅 <see cref="SaveFormatMode.Preview"/> 填充，已截断）。</param>
public sealed record SaveFormatOutcome(
    string SourcePath,
    SaveFormatMode Mode,
    bool Refused,
    string? RefusalReason,
    bool WroteFile,
    string? WrittenPath,
    string? BackupPath,
    bool Changed,
    bool SemanticsPreserved,
    long BytesBefore,
    long BytesAfter,
    SaveFormatStats Stats,
    string? Preview)
{
    /// <summary>是否可以安全落盘（未被拒绝且语义自校验通过）。</summary>
    public bool CanWrite => !Refused && SemanticsPreserved;
}
