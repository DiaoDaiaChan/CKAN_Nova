namespace CkanUI.ViewModels.Pages.Instances;

/// <summary>
/// 单行文本输入通道（F-06「增/改一条启动命令」用）。
/// </summary>
/// <remarks>
/// <c>IDialogService</c> 只有确认 / 单选 / 提示 / 进度四种对话，没有文本输入；
/// 该契约在 Abstractions 里缺席，本任务不允许改 Abstractions，所以在页面这一层新开一个宿主能力接口，
/// 由 App 层用 <c>TextInputDialogView</c>（<see cref="CkanUI.ViewModels.Dialogs.TextInputDialogViewModel"/> 的视图）实现，
/// 与目录选择 / 外壳启动同样的 <c>??=</c> 注入方式。
/// </remarks>
public interface IInstanceTextPrompt
{
    /// <summary>弹出单行文本输入框。</summary>
    /// <param name="title">标题。</param>
    /// <param name="message">说明文案。</param>
    /// <param name="initialValue">初始值（编辑已有条目时传入）。</param>
    /// <param name="placeholder">输入框占位文案。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>用户输入的文本（已去首尾空白）；取消返回 null。</returns>
    Task<string?> PromptAsync(
        string title,
        string message,
        string? initialValue = null,
        string? placeholder = null,
        CancellationToken cancellationToken = default);
}
