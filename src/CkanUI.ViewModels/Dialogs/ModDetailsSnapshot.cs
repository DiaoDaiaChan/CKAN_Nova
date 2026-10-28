namespace CkanUI.ViewModels.Dialogs;

/// <summary>
/// 详情面板的富字段投影（03 篇 §3.1 / 04 篇 §2 的 <c>ModDetailsViewModel</c>）。
/// <para>
/// 说明：<c>Abstractions</c> 里冻结的 <c>ModRowData</c> 只有列表列所需的字段，没有描述/依赖/推荐/冲突/许可证/主页。
/// 这些字段需要由 Core 的 <c>CkanModule</c> 适配出来，但服务契约已冻结（本任务不得改 Abstractions），
/// 因此先在本项目内定义纯记录承载，等 Lead 决定是否提升为 <c>Abstractions.Dtos</c> 的 DTO。
/// 所有字段可空：缺失时详情面板走空状态文案，而不是显示空白。
/// </para>
/// </summary>
public sealed record ModDetailsSnapshot(
    string Identifier,
    string Name,
    string? Author = null,
    string? Abstract = null,
    string? Description = null,
    string? License = null,
    string? Homepage = null,
    string? LatestVersion = null,
    string? InstalledVersion = null,
    long? SizeBytes = null,
    IReadOnlyList<string>? Tags = null,
    IReadOnlyList<ModRelationItem>? Dependencies = null,
    IReadOnlyList<ModRelationItem>? Recommendations = null,
    IReadOnlyList<ModRelationItem>? Conflicts = null,
    bool IsCompatible = true,
    IReadOnlyList<ModVersionItemDto>? Versions = null,
    IReadOnlyList<string>? InstalledFiles = null);

/// <summary>
/// 已装文件行（详情面板「文件与内容」页签的列表项）。
/// 快照里存的是相对实例根目录的路径字符串，这里拆出文件名/所在目录供列表显示，避免视图里做字符串处理。
/// </summary>
/// <param name="Path">相对游戏实例根目录的文件路径（原样保留，用于完整展示）。</param>
public sealed record InstalledFileItem(string Path)
{
    /// <summary>文件名（最后一个路径分隔符之后的部分）。</summary>
    public string FileName
    {
        get
        {
            var index = LastSeparatorIndex();
            return index < 0 ? Path : Path[(index + 1)..];
        }
    }

    /// <summary>是否位于子目录（根目录文件不显示目录行）。</summary>
    public bool HasDirectory => DirectoryText.Length > 0;

    /// <summary>所在目录（不含文件名）；位于根目录时为空串。</summary>
    public string DirectoryText
    {
        get
        {
            var index = LastSeparatorIndex();
            return index <= 0 ? string.Empty : Path[..index];
        }
    }

    private int LastSeparatorIndex() =>
        string.IsNullOrEmpty(Path) ? -1 : Path.LastIndexOfAny(['/', '\\']);
}

