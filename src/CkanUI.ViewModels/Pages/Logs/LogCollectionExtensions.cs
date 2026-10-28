using System.Collections.ObjectModel;

namespace CkanUI.ViewModels.Pages.Logs;

/// <summary>聚合视图与时间轴共用的只读集合容器，避免页面对两个集合各写一遍清空逻辑。</summary>
internal static class LogCollectionExtensions
{
    /// <summary>用新内容整批替换集合（先清空再填充，虚拟化列表能正确重建）。</summary>
    public static void Replace<T>(this ObservableCollection<T> target, IEnumerable<T> items)
    {
        if (target is null)
        {
            throw new ArgumentNullException(nameof(target));
        }

        target.Clear();
        foreach (var item in items)
        {
            target.Add(item);
        }
    }
}
