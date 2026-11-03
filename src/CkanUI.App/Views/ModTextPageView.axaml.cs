using System;
using Avalonia.Controls;
using Avalonia.Threading;

namespace CkanUI.App.Views;

public partial class ModTextPageView : UserControl
{
    private int _realizationHealAttempts;

    public ModTextPageView()
    {
        InitializeComponent();
        ModTextEntryList.LayoutUpdated += OnEntryListLayoutUpdated;
    }

    /// <summary>
    /// 兜底：真实窗口里 ListBox 偶尔在第一帧拿到零高度视口，虚拟化面板于是不实现任何行，
    /// 表现就是「表头在、页脚说 4807 条、中间一片空白」。这里在布局后检查一次：
    /// 有词条、列表有高度、却一个容器都没实现 → reinterpret 重新测量，最多试 3 次，避免抖动。
    /// </summary>
    private void OnEntryListLayoutUpdated(object? sender, EventArgs e)
    {
        if (_realizationHealAttempts >= 3)
        {
            return;
        }

        var list = ModTextEntryList;
        if (list.Bounds.Height <= 1 || list.ItemCount == 0 || list.ContainerFromIndex(0) is not null)
        {
            return;
        }

        _realizationHealAttempts++;
        Dispatcher.UIThread.Post(
            () =>
            {
                list.InvalidateMeasure();
                list.UpdateLayout();
            },
            DispatcherPriority.Background);
    }
}
