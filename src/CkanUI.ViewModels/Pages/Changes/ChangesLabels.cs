namespace CkanUI.ViewModels.Pages.Changes;

/// <summary>
/// 变更集页的固定标签文案（03 篇 §11：所有用户可见字符串集中在 VM 侧，XAML 里不写字面串）。
/// </summary>
/// <remarks>
/// 只放"不随数据变化"的标签；带数字/状态的句子在 <see cref="ChangesFormatter"/> 里拼。
/// 视图通过 <c>ChangesPageViewModel</c> 上的同名只读属性绑定，XAML 不认识本类。
/// </remarks>
public static class ChangesLabels
{
    /// <summary>明细表列头：模组。</summary>
    public const string ColumnName = "模组";

    /// <summary>明细表列头：动作。</summary>
    public const string ColumnAction = "动作";

    /// <summary>明细表列头：版本变化。</summary>
    public const string ColumnVersion = "从版本 → 到版本";

    /// <summary>明细表列头：来源。</summary>
    public const string ColumnOrigin = "来源";

    /// <summary>明细表列头：体积。</summary>
    public const string ColumnSize = "体积";

    /// <summary>明细表列头：行操作。</summary>
    public const string ColumnOperations = "操作";

    /// <summary>主按钮：开始。</summary>
    public const string ApplyButton = "开始";

    /// <summary>次按钮：取消。</summary>
    public const string CancelButton = "取消";

    /// <summary>次按钮：重新计算。</summary>
    public const string RecomputeButton = "重新计算";

    /// <summary>行内按钮：移出变更集。</summary>
    public const string RemoveItemButton = "移除";

    /// <summary>执行结果区标题。</summary>
    public const string ResultTitle = "执行结果";

    /// <summary>错误区标题。</summary>
    public const string ErrorTitle = "错误";

    /// <summary>冲突区标题前缀（后接数量，由 VM 拼接）。</summary>
    public const string ConflictTitlePrefix = "冲突";

    /// <summary>执行阶段提示：下载阶段可取消。</summary>
    public const string CancelHint = "下载阶段取消会立即停止并删除临时文件；安装阶段取消会等当前模组完成后回滚整个事务。";

    /// <summary>冲突区红框无障碍名称（读屏用）。</summary>
    public const string ConflictRegionName = "冲突列表";

    /// <summary>状态栏无障碍名称。</summary>
    public const string StatusRegionName = "状态栏";
}
