using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.Services.Abstractions;

/// <summary>
/// 游戏截图服务：列出 <c>&lt;GameDir&gt;/Screenshots</c> 里的截图。
/// </summary>
/// <remarks>
/// 与 <c>BackgroundImageLocator</c> 认同一批扩展名和同一个目录，避免背景层和主菜单对「什么算截图」各说各话。
/// 目录不存在不算错误（多数实例还没截过图），读目录被拒才算失败并交给调用方决定要不要提示。
/// </remarks>
public interface IGameScreenshotService
{
    /// <summary>取实例的截图目录（不保证存在）。</summary>
    /// <param name="gameDir">游戏根目录。</param>
    string GetScreenshotsDirectory(string gameDir);

    /// <summary>按拍摄时间倒序列出截图。</summary>
    /// <param name="gameDir">游戏根目录；为空时返回空列表。</param>
    /// <param name="limit">最多返回多少张（默认 12，上限 60）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<OperationResult<IReadOnlyList<GameScreenshotInfo>>> ListAsync(
        string gameDir,
        int limit = 12,
        CancellationToken cancellationToken = default);
}
