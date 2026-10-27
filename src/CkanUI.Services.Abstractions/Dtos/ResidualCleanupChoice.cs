namespace CkanUI.Services.Abstractions.Dtos;

/// <summary>
/// 卸载残留清理对话框的出口（12 篇 F-43 / 06 篇 UI-22）：删掉勾选的，还是全部保留。
/// <para>
/// 放在 Abstractions 而不是 ViewModels：对话框服务的契约（<see cref="IResidualCleanupDialogService"/>）
/// 要返回它，而抽象层不能反向引用视图模型层（21 篇 §5 依赖方向）。
/// </para>
/// </summary>
public enum ResidualCleanupChoice
{
    /// <summary>已删除勾选的残留目录。</summary>
    Delete,

    /// <summary>全部保留，什么都没删（默认出口，直接关窗也是它）。</summary>
    Keep,
}
