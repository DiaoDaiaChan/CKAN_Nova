using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.Services.Abstractions;

/// <summary>
/// SpaceDock 公共 API 的只读客户端（https://spacedock.info/api）。
/// </summary>
/// <remarks>
/// 浏览、搜索、查看详情与下载全部匿名可用；发布/更新模组才需要 SpaceDock 登录，
/// 因此本服务不提供登录，也不保存任何凭据。
/// </remarks>
public interface ISpaceDockService
{
    /// <summary>按游戏分页浏览模组。</summary>
    Task<OperationResult<SpaceDockBrowsePage>> BrowseAsync(
        int gameId,
        int page,
        int pageSize,
        SpaceDockSortOrder order,
        bool ascending,
        CancellationToken ct = default);

    /// <summary>按关键词搜索模组；SpaceDock 搜索接口不分页。</summary>
    Task<OperationResult<IReadOnlyList<SpaceDockModInfo>>> SearchAsync(
        string query,
        CancellationToken ct = default);

    /// <summary>取模组详情（含完整描述、版本与背景图）。</summary>
    Task<OperationResult<SpaceDockModInfo>> GetModAsync(long modId, CancellationToken ct = default);

    /// <summary>列出 SpaceDock 支持的游戏；用于把「KSP」解析成动态游戏 ID。</summary>
    Task<OperationResult<IReadOnlyList<SpaceDockGameInfo>>> GetGamesAsync(CancellationToken ct = default);
}
