namespace CkanUI.Services.Abstractions.Mods;

/// <summary>
/// 模组可用版本目录（F-30）：给定标识符，列出仓库索引里该模组的所有版本及兼容性。
/// </summary>
/// <remarks>
/// 之所以单独立契约而不塞进 <see cref="IRegistryService"/>：注册表快照是「当前实例装了什么」，
/// 版本目录是「仓库里有哪些可选版本」，两者数据来源不同（前者 registry.json，后者仓库索引缓存）。
/// 视图模型只依赖本契约，不接触 Core 的 <c>IRegistryQuerier</c>。
/// </remarks>
public interface IModVersionCatalog
{
    /// <summary>
    /// 读取某模组在仓库索引里的全部版本，按版本号降序。
    /// 没有选择实例、标识符为空、或索引里查不到该模组时返回空列表（不是失败）。
    /// </summary>
    Task<OperationResult<IReadOnlyList<ModVersionOption>>> GetVersionsAsync(
        string identifier,
        CancellationToken cancellationToken = default);
}
