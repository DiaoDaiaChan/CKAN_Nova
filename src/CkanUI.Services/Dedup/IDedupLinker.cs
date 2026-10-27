using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.Services.Dedup;

/// <summary>
/// 硬链接的底层接缝（工程约定 §12 #6 先例：后端保持 public，便于测试注入确定性的失败/不支持实现）。
/// </summary>
/// <remarks>
/// .NET 没有 <c>File.CreateHardLink</c>，真实实现走 P/Invoke（Windows <c>CreateHardLinkW</c>，
/// Unix <c>link(2)</c>）；测试用假实现模拟「平台不支持」与「某个文件创建失败」。
/// </remarks>
public interface IDedupLinker
{
    /// <summary>当前平台能否创建硬链接。</summary>
    DedupPlatformSupport Support { get; }

    /// <summary>读取文件实体标识；平台/文件系统不支持时返回 false（不抛）。</summary>
    bool TryGetIdentity(string path, out DedupFileIdentity identity);

    /// <summary>创建一个指向 <paramref name="existingPath"/> 的硬链接 <paramref name="linkPath"/>（不覆盖已有文件）。</summary>
    /// <exception cref="IOException">目标已存在、跨卷或文件系统不支持。</exception>
    /// <exception cref="UnauthorizedAccessException">权限不足。</exception>
    /// <exception cref="PlatformNotSupportedException">平台没有硬链接能力。</exception>
    void CreateLink(string linkPath, string existingPath);
}
