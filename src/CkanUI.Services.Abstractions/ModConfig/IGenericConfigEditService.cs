using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CkanUI.Services.Abstractions.ModConfig;

/// <summary>
/// 不依赖 adapter 包的通用 cfg 读写：给「Mod 配置」页在没有适配器时兜底。
/// <para>
/// 这一层是给插件用的窄接口（只出现 Abstractions 里的类型）。实现方负责：路径逃逸校验、写前备份、
/// 同目录临时文件 + 原子替换、游戏运行未确认不写、以及"只改值片段"的无损写回。
/// </para>
/// </summary>
public interface IGenericConfigEditService
{
    /// <summary>扫描 <c>&lt;gameDir&gt;/GameData</c> 下的候选 cfg；gameDir 为空时按 instanceName 找实例目录。</summary>
    Task<OperationResult<GenericConfigCatalog>> DiscoverAsync(
        string gameDir,
        string instanceName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 按指定范围扫描。默认实现退回保守模式，保证第三方/测试替身不必立刻实现新重载；
    /// 宿主实现会真正把 <paramref name="options"/> 交给扫描器。
    /// </summary>
    Task<OperationResult<GenericConfigCatalog>> DiscoverAsync(
        string gameDir,
        string instanceName,
        GenericConfigScanOptions options,
        CancellationToken cancellationToken = default)
        => DiscoverAsync(gameDir, instanceName, cancellationToken);

    /// <summary>加载一个 cfg 的只读快照；路径必须落在 gameDir 之内。</summary>
    Task<OperationResult<GenericConfigDocumentView>> LoadAsync(
        string gameDir,
        string filePath,
        CancellationToken cancellationToken = default);

    /// <summary>按行号把改动写回同一个 cfg（先备份再原子替换）。</summary>
    Task<OperationResult<GenericConfigSaveResult>> SaveAsync(
        string gameDir,
        string instanceName,
        string filePath,
        IReadOnlyList<GenericConfigChange> changes,
        bool confirmedGameRunning,
        CancellationToken cancellationToken = default);
}
