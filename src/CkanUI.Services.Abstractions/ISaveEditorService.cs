using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.Services.Abstractions;

/// <summary>存档语义读取 + 结构化写入（SFS-05/06/07 的服务层门面）。</summary>
public interface ISaveEditorService
{
    /// <summary>解析存档并投影成“常用内容优先”的分区大纲。</summary>
    Task<OperationResult<SaveOutlineDto>> LoadOutlineAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>按关键字搜键名/标签/值/路径，空格分词全命中。</summary>
    Task<OperationResult<IReadOnlyList<SaveOutlineMatchDto>>> SearchAsync(
        string filePath,
        string query,
        int limit = 200,
        CancellationToken cancellationToken = default);

    /// <summary>懒展开任意块（只读，不写盘）。</summary>
    Task<OperationResult<SaveNodeDetailDto>> DescribeNodeAsync(
        string filePath,
        int line,
        CancellationToken cancellationToken = default);

    /// <summary>结构化编辑：默认只出候选文本，<c>Write</c> 为真才落盘（写前快照 + 原子写）。</summary>
    Task<OperationResult<SaveEditOutcomeDto>> EditAsync(SaveEditRequest request, CancellationToken cancellationToken = default);
}

/// <summary>编辑动作种类。</summary>
public enum SaveEditKind
{
    SetValue,
    RenameKey,
    DeleteElement,
    DuplicateElement,
    InsertPair,
}

/// <summary>一次结构化编辑请求。</summary>
public sealed record SaveEditRequest
{
    public string FilePath { get; init; } = string.Empty;

    public SaveEditKind Kind { get; init; } = SaveEditKind.SetValue;

    /// <summary>目标行（1 基）。插入时是父块行。</summary>
    public int Line { get; init; }

    public string Key { get; init; } = string.Empty;

    public string Value { get; init; } = string.Empty;

    /// <summary>false 时只返回候选文本，不碰磁盘。</summary>
    public bool Write { get; init; }
}

/// <summary>编辑结果：是否通过自校验、改了几行、快照路径、问题清单。</summary>
public sealed record SaveEditOutcomeDto
{
    public bool Succeeded { get; init; }

    public bool Changed { get; init; }

    public bool Verified { get; init; }

    public string Summary { get; init; } = string.Empty;

    public IReadOnlyList<string> Issues { get; init; } = [];

    public string? CandidateText { get; init; }

    public string? BackupPath { get; init; }

    public bool Written { get; init; }
}
