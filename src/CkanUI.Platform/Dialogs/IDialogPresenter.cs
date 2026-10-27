using System.Collections.Generic;
using System.Threading.Tasks;

namespace CkanUI.Platform.Dialogs;

/// <summary>
/// 对话框呈现者接缝（03 篇 §8 / 05 篇 §3.2）：把"用哪套视图弹窗"从 <see cref="DialogService"/> 里剥出来。
/// </summary>
/// <remarks>
/// 契约在 Platform（Avalonia 可用），实现在 App（视图只能在 App）。这样 Services→App 的反向依赖不会出现，
/// 而 <see cref="DialogService"/> 仍是唯一的 <c>IDialogService</c> 实现。
/// 每个方法都返回可空值：<c>null</c> 表示"这一种对话框我不接"，由 <see cref="DialogService"/> 退回内置代码建窗实现
/// （headless 宿主、启动早期没有视图程序集时依然能用）。
/// 实现方必须假定自己已在 UI 线程被调用（<see cref="DialogService"/> 负责封送）。
/// </remarks>
public interface IDialogPresenter
{
    /// <summary>确认对话框；返回 null 表示不接管。true = 主按钮，false = 取消/关闭。</summary>
    Task<bool?> ConfirmAsync(string title, string message, string? detail);

    /// <summary>单选对话框；返回 0 基索引，取消返回 -1；返回 null 表示不接管。</summary>
    Task<int?> SelectAsync(string title, string message, IReadOnlyList<string> options, int defaultIndex);

    /// <summary>通知类对话框（错误/提示）；返回 null 表示不接管，true 表示已呈现并等用户关闭。</summary>
    Task<bool?> ShowNoticeAsync(bool isError, string title, string message, string? detail);
}
