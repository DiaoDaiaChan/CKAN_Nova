using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.Services.Abstractions;

/// <summary>
/// 存档文件服务（13 篇 §7 / 03 篇 §14 / 06 篇 E1）：存档索引、诊断汇总与格式化落盘。
/// </summary>
/// <remarks>
/// <para>所有方法都不抛异常：读不到、被占用、结构损坏一律通过 <see cref="OperationResult"/> 或诊断回报
/// （13 篇 §7.2：文件被占用捕获 <see cref="System.IO.IOException"/> 后提示，不重试）。</para>
/// <para>格式化默认只写新文件；覆盖写必须先快照（铁律 L9），绝不静默改动原文件。</para>
/// <para>本接口只出现在 ViewModels 的构造函数里，实现细节（SFS 内核）不进契约（04 篇 §1）。</para>
/// </remarks>
public interface ISaveFileService
{
    /// <summary>实例的 <c>saves</c> 目录路径（纯字符串拼接，不访问磁盘）。</summary>
    string GetSavesDirectory(string gameDir);

    /// <summary>
    /// 枚举 <c>&lt;gameDir&gt;/saves</c> 下的存档（含 <c>*.sfs.bak</c>），按修改时间倒序。
    /// 目录不存在视为空列表，不是错误。
    /// </summary>
    Task<OperationResult<IReadOnlyList<SaveGameInfo>>> ListSavesAsync(
        string gameDir,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 解析统计 + 诊断汇总（= 校验，不写盘）。结构损坏的存档返回**成功**结果，
    /// 诊断里带 V-01..V-09，绝不抛异常。
    /// </summary>
    Task<OperationResult<SaveValidationReport>> ValidateAsync(
        string filePath,
        CancellationToken cancellationToken = default);

    /// <summary>格式化：预览 / 写新文件 / 先快照再覆盖（<see cref="SaveFormatMode"/>）。</summary>
    Task<OperationResult<SaveFormatOutcome>> FormatAsync(
        SaveFormatRequest request,
        CancellationToken cancellationToken = default);
}
