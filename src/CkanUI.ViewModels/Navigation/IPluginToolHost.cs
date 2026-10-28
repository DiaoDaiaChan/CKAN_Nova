namespace CkanUI.ViewModels.Navigation;

/// <summary>
/// 「我的工具条愿意接收插件按钮」的页面契约（12 篇 F-17 工具项）。
/// </summary>
/// <remarks>
/// <para>由宿主页面视图模型实现（当前只有模组管理页）；App 层的工具项宿主按
/// <see cref="ToolHostKey"/> 把 <see cref="PluginToolButtonViewModel"/> 推进来。</para>
/// <para>做成接口而不是给基类加成员：只有确实想接收按钮的页面才实现，其余页面零成本，
/// 也不会因为基类新增抽象成员而被迫写空实现。</para>
/// </remarks>
public interface IPluginToolHost
{
    /// <summary>与导航键一致的工具条键（如 <c>mods</c>），与插件的目标页键比对时忽略大小写。</summary>
    string ToolHostKey { get; }

    /// <summary>
    /// 整体替换工具条上的插件按钮（没有插件工具项时传空列表）。
    /// </summary>
    /// <param name="tools">按显示顺序排列的按钮；空列表表示清空。</param>
    void SetPluginTools(IReadOnlyList<PluginToolButtonViewModel> tools);
}
