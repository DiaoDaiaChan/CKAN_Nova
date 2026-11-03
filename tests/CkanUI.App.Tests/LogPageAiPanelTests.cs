using System.Linq;

using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;

using CkanUI.App.Tests.Automation;
using CkanUI.App.Views.Logs;
using CkanUI.Services.Abstractions.Logs;
using CkanUI.Services.Logs;
using CkanUI.ViewModels.Pages.Logs;

using NUnit.Framework;

namespace CkanUI.App.Tests;

/// <summary>
/// 日志页的 AI 面板（14 篇 §6）：默认收起、点「请 AI 分析」展开并填好预览。
/// 只测「面板真的接上了」，模型调用本身在 ViewModels 层用假客户端测。
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class LogPageAiPanelTests
{
    [SetUp]
    public void SetUp() => HeadlessAppHost.EnsureInitialized();

    [Test]
    public void AiPanel_IsClosedUntilRequestedThenShowsPreview()
    {
        var store = new LogStore(null);
        store.Append(LogEvent.Create(LogSourceKind.Log4Net, LogLevel.Error, "log4net", "KSP 启动失败"));
        var page = new LogPageViewModel(store, new LogQueryService(store));

        var view = new LogPageView { DataContext = page };
        var window = new Window { Width = 1000, Height = 700, Content = view };
        window.Show();
        HeadlessAppHost.PumpJobs();

        var panel = view.FindControl<Border>("AiDiagnosisPanel");
        Assert.That(panel, Is.Not.Null);
        Assert.That(panel!.IsVisible, Is.False, "面板默认必须收起，不能一进页面就盖住日志");

        page.RefreshCommand.Execute(null);
        page.RequestAiAnalysisCommand.Execute(null);
        HeadlessAppHost.PumpJobs();

        Assert.That(panel.IsVisible, Is.True);
        var preview = view.FindControl<TextBox>("AiPreviewBox");
        Assert.That(preview, Is.Not.Null);
        Assert.That(preview!.Text, Does.Contain("#"));
        Assert.That(preview.Text, Does.Contain("KSP 启动失败"));
        Assert.That(panel.Background, Is.Not.Null, "浮层必须有不透明背景，否则会透出下面的日志行");
        Assert.That(ScrollViewer.GetVerticalScrollBarVisibility(preview), Is.EqualTo(ScrollBarVisibility.Auto),
            "预览是整段提示词，必须能滚动看全");
        Assert.That(panel.Bounds.Width, Is.EqualTo(420).Within(1));

        page.Ai.CloseCommand.Execute(null);
        HeadlessAppHost.PumpJobs();
        Assert.That(panel.IsVisible, Is.False);
    }
}
