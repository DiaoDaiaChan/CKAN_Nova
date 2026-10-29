using System.Windows.Input;

namespace CkanUI.ViewModels;

/// <summary>
/// 页面级「有未落盘改动」的能力声明。外壳靠它决定底部悬浮提示条要不要出现——
/// 具体页面的脏判定各不相同（设置项编辑中、模组包表单脏、配置草稿、待应用的变更清单），
/// 所以只暴露一个布尔量，判定逻辑留在各自页面里。
/// </summary>
/// <remarks>
/// 提示条上的两个按钮是可选的（默认实现返回 <c>null</c>）：有明确「保存 / 放弃」语义的页面才提供命令，
/// 其余页面只用提示条做提醒。这样外壳不必知道任何一页的具体命令名，也不需要为「哪些页能保存」开条件分支。
/// </remarks>
public interface IUnsavedChangesAware
{
    /// <summary>当前是否还有未落盘的改动。</summary>
    bool HasUnsavedChanges { get; }

    /// <summary>提示条上的「保存更改」；返回 <c>null</c> 表示本页没有一键保存动作，提示条不显示该按钮。</summary>
    ICommand? SaveChangesCommand => null;

    /// <summary>提示条上的「放弃修改」；返回 <c>null</c> 表示本页没有一键放弃动作。</summary>
    ICommand? DiscardChangesCommand => null;
}
