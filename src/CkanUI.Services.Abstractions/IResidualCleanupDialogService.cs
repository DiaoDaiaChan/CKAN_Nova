using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.Services.Abstractions;

/// <summary>
/// 卸载残留清理对话框（12 篇 F-43 / 06 篇 UI-22）。<see cref="IDialogService"/> 的
/// Confirm/Select/ShowMessage 表达不了"目录列表 + 内容预览 + 勾选删除"，
/// 因此与 <see cref="IModDialogService"/> 同一做法：单开一个窄接口，
/// 实现负责在 UI 线程上建窗、注入 owner（03 篇 §7、11 篇 §11）。
/// </summary>
public interface IResidualCleanupDialogService
{
    /// <summary>
    /// 列出残留目录并让用户决定删哪些。用户点「删除勾选」返回 <see cref="ResidualCleanupChoice.Delete"/>，
    /// 点「全部保留」或直接关窗返回 <see cref="ResidualCleanupChoice.Keep"/>（默认不删，安全侧）。
    /// </summary>
    /// <param name="instance">刚完成卸载的实例。</param>
    /// <param name="reportedByUninstall">卸载后端报上来的残留目录（相对 GameData），可为空集合。</param>
    /// <param name="ct">取消令牌。</param>
    Task<ResidualCleanupChoice> ShowAsync(
        InstanceInfo instance,
        IReadOnlyCollection<string> reportedByUninstall,
        CancellationToken ct = default);
}
