using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.VisualTree;
using CkanUI.App.Tests.Automation;
using CkanUI.App.Views.Operations;
using CkanUI.ViewModels;
using CkanUI.ViewModels.Pages.Logs;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System.Linq;

namespace CkanUI.App.Tests;

/// <summary>
/// 右下角后台任务窗口微件嵌入底栏与整洁布局自动化测试：
/// 1. 验证后台任务胶囊微件直接嵌入底栏右侧（StatusBarTaskPillButton），与版本号整齐并列；
/// 2. 验证日志与 FPS 小标记落在顶栏标题右侧的独立列，底栏保持纯净整洁；
/// 3. 验证点击底栏后台任务微件后，向上弹出详细任务进度窗口（ExpandedTaskCard），完全不遮挡底栏。
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class BackgroundTaskWidgetTests
{
    [SetUp]
    public void SetUp() => HeadlessAppHost.EnsureInitialized();

    [Test]
    public void TopBar_ShowsLogAndFpsMarkers_WhileBottomBarStaysClean()
    {
        MainWindow? window = null;
        var services = ServiceRegistration.Build(() => window!);
        try
        {
            var vm = services.GetRequiredService<ShellViewModel>();
            window = new MainWindow { DataContext = vm };
            window.Show();

            HeadlessAppHost.PumpJobs();

            // 顶栏：日志与 FPS 两枚小标记常驻在标题右侧的独立列。
            var quickStatus = window.FindControl<StackPanel>("TopQuickStatusHost");
            Assert.That(quickStatus, Is.Not.Null, "顶栏必须常驻日志与 FPS 小标记");

            var logBtn = window.FindControl<Button>("TopLogStatusButton");
            Assert.That(logBtn, Is.Not.Null, "顶栏必须有日志抽屉开关");
            Assert.That(logBtn!.Command, Is.SameAs(vm.ToggleLogDrawerCommand), "日志标记必须挂到 ShellViewModel 的抽屉命令上");
            Assert.That(window.FindControl<Border>("TopLogBadge"), Is.Not.Null, "日志标记必须带未读计数徽标");

            var fpsBtn = window.FindControl<Button>("TopFpsStatusButton");
            Assert.That(fpsBtn, Is.Not.Null, "顶栏必须有 FPS 读数标记");
            Assert.That(window.FindControl<TextBlock>("TopFpsStatusText"), Is.Not.Null, "FPS 标记必须有读数文本");
            Assert.That(window.FindControl<Ellipse>("TopFpsStatusDot"), Is.Not.Null, "FPS 标记必须有色调圆点");

            // 底栏：旧的 FPS 按钮不得回流，底栏只留后台任务胶囊与版本号。
            Assert.That(window.FindControl<Button>("FpsStatusButton"), Is.Null, "底栏不得再叠加 FPS 按钮");
            Assert.That(window.FindControl<TextBlock>("FpsStatusText"), Is.Null, "底栏不得再叠加 FPS 读数");

            // 端到端：抽屉就绪后标记可点、点一下真的开合；未读时徽标才点亮。
            var drawer = new LogDrawerViewModel();
            var badge = window.FindControl<Border>("TopLogBadge")!;
            vm.LogDrawer = drawer;
            HeadlessAppHost.PumpJobs();
            Assert.That(badge.IsVisible, Is.False, "没有未读时顶栏徽标不该点亮");

            drawer.NewErrorCount = 2;
            HeadlessAppHost.PumpJobs();
            Assert.That(badge.IsVisible, Is.True, "有未读错误时顶栏徽标必须点亮");

            Assert.That(logBtn.Command!.CanExecute(null), Is.True, "抽屉就绪后日志标记必须可点");
            logBtn.Command.Execute(null);
            HeadlessAppHost.PumpJobs();
            Assert.That(drawer.IsOpen, Is.True, "点顶栏日志标记应展开抽屉");
            Assert.That(badge.IsVisible, Is.False, "展开即已读，徽标应熄灯");
        }
        finally
        {
            window?.Close();
            HeadlessAppHost.PumpJobs();
            services.Dispose();
        }
    }

    [Test]
    public void BackgroundTaskWidget_EmbeddedInStatusBar_AndOpensDetailAbove()
    {
        MainWindow? window = null;
        var services = ServiceRegistration.Build(() => window!);
        try
        {
            var vm = services.GetRequiredService<ShellViewModel>();
            window = new MainWindow { DataContext = vm };
            window.Show();

            HeadlessAppHost.PumpJobs();

            Assert.That(vm.HasOperationQueue, Is.True, "ShellViewModel 必须挂载 OperationQueue");
            var queueVm = vm.OperationQueue;
            Assert.That(queueVm, Is.Not.Null);

            // 1. 底栏中嵌入后台任务紧凑胶囊按钮
            var statusBarTaskBtn = window.FindControl<Button>("StatusBarTaskPillButton");
            Assert.That(statusBarTaskBtn, Is.Not.Null, "底栏右侧必须嵌入后台任务胶囊按钮");

            // 2. 底栏上方挂载详细卡片
            var overlay = window.GetVisualDescendants()
                .OfType<OperationQueueOverlayView>()
                .FirstOrDefault();
            Assert.That(overlay, Is.Not.Null, "界面右下角必须挂载 OperationQueueOverlayView");
            if (overlay is null) return;

            var expandedCard = overlay.FindControl<Border>("ExpandedTaskCard");
            Assert.That(expandedCard, Is.Not.Null, "必须包含展开详细任务卡片");
            if (expandedCard is null) return;

            // 3. 默认收起状态：底栏上方不显示大卡片，不遮挡底栏
            Assert.That(queueVm!.IsExpanded, Is.False, "默认应为紧凑收起模式");
            Assert.That(expandedCard.IsVisible, Is.False, "未点开时，详细大卡片不应显示，防止遮挡底栏");

            // 4. 点击底栏微件：向上弹开详细任务窗口
            statusBarTaskBtn!.Command?.Execute(null);
            HeadlessAppHost.PumpJobs();

            Assert.That(queueVm.IsExpanded, Is.True, "点击后应变为展开模式");
            Assert.That(expandedCard.IsVisible, Is.True, "展开后详细卡片显示在底栏上方");
            Assert.That(expandedCard.Margin.Bottom, Is.GreaterThanOrEqualTo(30), "详细卡片必须位于底栏之上，底端不压底栏文本");

            // 5. 点击收起：详细卡片隐去，底栏恢复常态
            queueVm.ToggleExpandCommand.Execute(null);
            HeadlessAppHost.PumpJobs();

            Assert.That(queueVm.IsExpanded, Is.False, "点击收起后恢复");
            Assert.That(expandedCard.IsVisible, Is.False, "收起后详细大卡片隐去");
        }
        finally
        {
            window?.Close();
            HeadlessAppHost.PumpJobs();
            services.Dispose();
        }
    }
}
