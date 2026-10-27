using CkanUI.Services.Abstractions;

namespace CkanUI.ViewModels.Pages.Instances;

/// <summary>
/// Steam 库探测来源（03 篇 §5「添加实例 → 自动查找」，06 篇 UI-32「Steam 导入」）。
/// </summary>
/// <remarks>
/// Core 的探测能力（扫描 Steam 库与常见路径）尚未通过契约暴露，因此这里先定义纯接口：
/// VM 只依赖接口，探测实现（读 <c>libraryfolders.vdf</c>、查常见安装路径）放在 App/服务层。
/// 实现在服务层落地后由 Lead 在 DI 里注册即可，VM 侧零改动。
/// </remarks>
public interface ISteamLibrarySource
{
    /// <summary>返回 Steam 库中疑似 KSP 的安装目录（未与实例注册表去重）。</summary>
    Task<OperationResult<IReadOnlyList<string>>> DiscoverGameDirsAsync(CancellationToken ct = default);
}
