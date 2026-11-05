namespace CkanUI.Services.Abstractions.Assets;

/// <summary>载具库：<c>Ships/VAB</c>、<c>Ships/SPH</c>、<c>Ships/Subassemblies</c> 下的 <c>.craft</c>。</summary>
/// <remarks>
/// 改名 / 复制 / 删除必须连同伴生 <c>.loadmeta</c> 与 <c>@thumbs</c> 缩略图一起处理，
/// 否则游戏里会丢缩略图或版本号。
/// </remarks>
public interface ICraftLibraryService
{
    /// <summary>取分类对应的相对路径（正斜杠），例如 <c>Ships/VAB</c>。</summary>
    /// <param name="kind">分类。</param>
    string GetKindRelativePath(CraftKind kind);

    /// <summary>扫描三类载具。</summary>
    /// <param name="gameDir">游戏根目录。</param>
    /// <param name="cancellationToken">取消标记。</param>
    Task<OperationResult<IReadOnlyList<CraftAsset>>> ScanAsync(string gameDir, CancellationToken cancellationToken = default);

    /// <summary>把一个外部 .craft 导入到指定分类。</summary>
    /// <param name="gameDir">游戏根目录。</param>
    /// <param name="sourceFile">源文件绝对路径。</param>
    /// <param name="kind">目标分类。</param>
    /// <param name="cancellationToken">取消标记。</param>
    Task<OperationResult<CraftAsset>> ImportAsync(string gameDir, string sourceFile, CraftKind kind, CancellationToken cancellationToken = default);

    /// <summary>复制一个载具（含 .loadmeta 与缩略图）。</summary>
    /// <param name="gameDir">游戏根目录。</param>
    /// <param name="craft">源载具。</param>
    /// <param name="newName">新名字（不带扩展名）。</param>
    /// <param name="cancellationToken">取消标记。</param>
    Task<OperationResult<CraftAsset>> DuplicateAsync(string gameDir, CraftAsset craft, string newName, CancellationToken cancellationToken = default);

    /// <summary>重命名一个载具。</summary>
    /// <param name="gameDir">游戏根目录。</param>
    /// <param name="craft">目标载具。</param>
    /// <param name="newName">新名字（不带扩展名）。</param>
    /// <param name="cancellationToken">取消标记。</param>
    Task<OperationResult<CraftAsset>> RenameAsync(string gameDir, CraftAsset craft, string newName, CancellationToken cancellationToken = default);

    /// <summary>删除一个载具及其伴生文件。</summary>
    /// <param name="gameDir">游戏根目录。</param>
    /// <param name="craft">目标载具。</param>
    /// <param name="cancellationToken">取消标记。</param>
    Task<OperationResult> DeleteAsync(string gameDir, CraftAsset craft, CancellationToken cancellationToken = default);

    /// <summary>扫描一个载具库：根目录的 <c>&lt;gameDir&gt;/Ships</c>，或某个存档的 <c>&lt;gameDir&gt;/saves/&lt;存档&gt;/Ships</c>。</summary>
    /// <param name="shipsRoot">载具库根目录（就是 <c>Ships</c> 这一层）。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <remarks>存档库没有 <c>@thumbs</c> 是常见情况，缩略图缺失返回 null，不算错误。</remarks>
    Task<OperationResult<IReadOnlyList<CraftAsset>>> ScanLibraryAsync(string shipsRoot, CancellationToken cancellationToken = default);

    /// <summary>把一个外部 .craft 导入指定载具库的某个分类。</summary>
    /// <param name="shipsRoot">目标载具库根目录（<c>Ships</c> 这一层）。</param>
    /// <param name="sourceFile">源文件绝对路径。</param>
    /// <param name="kind">目标分类。</param>
    /// <param name="cancellationToken">取消标记。</param>
    Task<OperationResult<CraftAsset>> ImportToLibraryAsync(string shipsRoot, string sourceFile, CraftKind kind, CancellationToken cancellationToken = default);

    /// <summary>把载具复制 / 搬运到另一个载具库，连同伴生 <c>.loadmeta</c> 与缩略图。</summary>
    /// <param name="craft">源载具。</param>
    /// <param name="targetShipsRoot">目标载具库根目录（<c>Ships</c> 这一层）。</param>
    /// <param name="targetKind">目标分类（决定落在 VAB / SPH / Subassemblies）。</param>
    /// <param name="move">true 搬运（源消失），false 复制。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <remarks>目标已有同名载具时自动加 <c>(2)</c> 后缀，绝不覆盖；源与目标是同一个目录直接失败。</remarks>
    Task<OperationResult<CraftAsset>> TransferAsync(CraftAsset craft, string targetShipsRoot, CraftKind targetKind, bool move, CancellationToken cancellationToken = default);
}

/// <summary>旗帜库：GameData 下所有名为 <c>Flags</c> 的目录。</summary>
public interface IFlagLibraryService
{
    /// <summary>扫描全部旗帜。</summary>
    /// <param name="gameDir">游戏根目录。</param>
    /// <param name="cancellationToken">取消标记。</param>
    Task<OperationResult<IReadOnlyList<FlagAsset>>> ScanAsync(string gameDir, CancellationToken cancellationToken = default);

    /// <summary>读 <c>settings.cfg</c> 里的当前默认旗帜配置值。</summary>
    /// <param name="gameDir">游戏根目录。</param>
    OperationResult<string?> ReadDefaultFlag(string gameDir);

    /// <summary>把一张旗帜设为默认。</summary>
    /// <param name="gameDir">游戏根目录。</param>
    /// <param name="flag">目标旗帜。</param>
    /// <param name="cancellationToken">取消标记。</param>
    Task<OperationResult> SetDefaultFlagAsync(string gameDir, FlagAsset flag, CancellationToken cancellationToken = default);

    /// <summary>导入一张旗帜到 <c>GameData/CkanUI/Flags</c>。</summary>
    /// <param name="gameDir">游戏根目录。</param>
    /// <param name="sourceFile">源图片绝对路径。</param>
    /// <param name="cancellationToken">取消标记。</param>
    Task<OperationResult<FlagAsset>> ImportAsync(string gameDir, string sourceFile, CancellationToken cancellationToken = default);

    /// <summary>重命名一张旗帜。</summary>
    /// <param name="gameDir">游戏根目录。</param>
    /// <param name="flag">目标旗帜。</param>
    /// <param name="newName">新名字（不带扩展名）。</param>
    /// <param name="cancellationToken">取消标记。</param>
    Task<OperationResult<FlagAsset>> RenameAsync(string gameDir, FlagAsset flag, string newName, CancellationToken cancellationToken = default);

    /// <summary>删除一张旗帜。</summary>
    /// <param name="gameDir">游戏根目录。</param>
    /// <param name="flag">目标旗帜。</param>
    /// <param name="cancellationToken">取消标记。</param>
    Task<OperationResult> DeleteAsync(string gameDir, FlagAsset flag, CancellationToken cancellationToken = default);
}

/// <summary>加载图库：目录清单 + 目录里的图片。</summary>
/// <remarks>原版加载图内嵌在游戏资源里，可管理的只有模组 / 自定义目录，所以这里管的是目录清单。</remarks>
public interface ILoadingImageService
{
    /// <summary>默认目录清单（只含存在的目录）。</summary>
    /// <param name="gameDir">游戏根目录。</param>
    IReadOnlyList<string> DefaultFolders(string gameDir);

    /// <summary>读用户维护的目录清单；没有清单时回落到默认目录。</summary>
    /// <param name="gameDir">游戏根目录。</param>
    OperationResult<IReadOnlyList<string>> LoadFolders(string gameDir);

    /// <summary>保存目录清单。</summary>
    /// <param name="gameDir">游戏根目录。</param>
    /// <param name="folders">目录清单。</param>
    OperationResult SaveFolders(string gameDir, IReadOnlyList<string> folders);

    /// <summary>目录 → 来源信息（只列能认出主人的目录）。</summary>
    /// <param name="gameDir">游戏根目录。</param>
    IReadOnlyDictionary<string, LoadingImageFolderInfo> DescribeFolders(string gameDir);

    /// <summary>扫描若干目录里的图片（默认只扫第一层）。</summary>
    /// <param name="folders">目录清单。</param>
    /// <param name="recursiveFolders">需要连子目录一起扫的目录（模组要求递归时）。</param>
    /// <param name="cancellationToken">取消标记。</param>
    Task<OperationResult<IReadOnlyList<LoadingImageAsset>>> ScanAsync(
        IReadOnlyList<string> folders,
        IReadOnlyCollection<string>? recursiveFolders = null,
        CancellationToken cancellationToken = default);

    /// <summary>把外部图片导入到目标目录。</summary>
    /// <param name="targetFolder">目标目录。</param>
    /// <param name="sourceFile">源图片绝对路径。</param>
    /// <param name="cancellationToken">取消标记。</param>
    Task<OperationResult<LoadingImageAsset>> ImportAsync(string targetFolder, string sourceFile, CancellationToken cancellationToken = default);

    /// <summary>启用 / 停用一张加载图。</summary>
    /// <param name="image">目标图片。</param>
    /// <param name="enabled">true 启用，false 停用。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <remarks>停用靠给文件加 <c>.disabled</c> 后缀：ROLoadingImages 与 LoadingScreenManager 都只认原本的扩展名，
    /// 改后缀后游戏自然跳过，而且随时能原样改回来。</remarks>
    Task<OperationResult<LoadingImageAsset>> SetEnabledAsync(LoadingImageAsset image, bool enabled, CancellationToken cancellationToken = default);

    /// <summary>删除一张加载图。</summary>
    /// <param name="image">目标图片。</param>
    /// <param name="cancellationToken">取消标记。</param>
    Task<OperationResult> DeleteAsync(LoadingImageAsset image, CancellationToken cancellationToken = default);

    /// <summary>判断文件是否是支持的图片格式。</summary>
    /// <param name="path">文件路径。</param>
    bool IsSupportedImage(string path);
}
