namespace CkanUI.Services.Abstractions.Dtos;

/// <summary>
/// 一个卸载残留目录（12 篇 F-43 / 06 篇 UI-22）。
/// <para>
/// 路径一律相对 <c>&lt;GameDir&gt;/GameData</c>（正斜杠），与 <see cref="UnmanagedFileInfo"/> 同一套口径，
/// 这样 UI 与测试都不必关心盘符与大小写差异；绝对路径由调用方按实例拼出来。
/// </para>
/// </summary>
/// <param name="RelativePath">相对 GameData 的目录路径。</param>
/// <param name="FileCount">目录（含子目录）里的文件数。</param>
/// <param name="SizeBytes">目录（含子目录）的字节数。</param>
/// <param name="SampleFiles">若干示例文件名（相对该目录），供用户判断这是不是他认识的残留。</param>
public sealed record ResidualDirectoryInfo(
    string RelativePath,
    int FileCount,
    long SizeBytes,
    IReadOnlyList<string> SampleFiles)
{
    /// <summary>不可删除的原因（中文，可直接展示）；为 <c>null</c> 表示可以删除。</summary>
    public string? BlockReason { get; init; }

    /// <summary>是否可以删除（受管内容、符号链接、越权路径、目录已不存在都会被标记为不可删除）。</summary>
    public bool IsDeletable => BlockReason is null;
}
