namespace CkanUI.ViewModels;

/// <summary>
/// 页面级「有未落盘改动」的能力声明。外壳靠它决定底部提示条要不要出现——
/// 具体页面的脏判定各不相同（设置项编辑中、模组包表单脏、配置草稿、待应用的变更清单），
/// 所以只暴露一个布尔量，判定逻辑留在各自页面里。
/// </summary>
public interface IUnsavedChangesAware
{
    /// <summary>当前是否还有未落盘的改动。</summary>
    bool HasUnsavedChanges { get; }
}
