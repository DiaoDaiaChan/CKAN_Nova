using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using CkanUI.App.Tests.Automation;
using CkanUI.ViewModels;
using NUnit.Framework;

namespace CkanUI.App.Tests;

/// <summary>
/// 底栏 AI 助手入口：和后台任务胶囊同一栏、点开有内容（页面或解释）、再点/关闭/Esc 都能收起。
/// 无头建窗口但不 Show、不驱帧——渲染队列在 headless 下排不空（docs/design/plugins.md §11.7）。
/// </summary>
[TestFixture]
[NonParallelizable]
public class AiStatusBarTests
{
    [SetUp]
    public void SetUp() => HeadlessAppHost.EnsureInitialized();

    [Test]
    public void AiButton_SitsInTheSameStatusBarRow_AsTheBackgroundTaskPill()
    {
        var (window, _) = CreateWindow();
        var ai = window.FindControl<Button>("StatusAiButton");
        var pill = window.FindControl<Button>("StatusBarTaskPillButton");
        var overlay = window.FindControl<Grid>("AiAssistantOverlay");

        Assert.That(ai, Is.Not.Null);
        Assert.That(pill, Is.Not.Null);
        Assert.That(overlay, Is.Not.Null);

        // 按钮外面套着主题色 Border（视觉容器），所以按「最近的一层 StackPanel」比，而不是直接比 Parent。
        var aiRow = Ancestor<StackPanel>(ai!);
        var pillRow = Ancestor<StackPanel>(pill!);
        Assert.That(aiRow, Is.Not.Null);
        Assert.That(aiRow, Is.SameAs(pillRow), "AI 入口必须和后台任务胶囊在同一栏里");
        Assert.That(overlay!.IsVisible, Is.False, "抽屉默认收起");
    }

    [Test]
    public void ClickingTheAiButton_OpensThePanelWithSomethingInIt()
    {
        var (window, _) = CreateWindow();
        var ai = window.FindControl<Button>("StatusAiButton")!;
        var overlay = window.FindControl<Grid>("AiAssistantOverlay")!;
        var content = window.FindControl<ContentControl>("AiAssistantContent");
        var errorBar = window.FindControl<Border>("AiAssistantErrorBar");

        ai.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.That(overlay.IsVisible, Is.True);
        // 空面板不解释原因，用户只会以为按钮坏了：要么给页面，要么给一条人话错误。
        var hasPage = content?.Content is not null;
        var hasError = errorBar is { IsVisible: true };
        Assert.That(hasPage || hasError, Is.True, "打开后面板不能是空白");
    }

    [Test]
    public void ClickingTheAiButtonTwice_ClosesThePanel()
    {
        var (window, _) = CreateWindow();
        var ai = window.FindControl<Button>("StatusAiButton")!;
        var overlay = window.FindControl<Grid>("AiAssistantOverlay")!;

        ai.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.That(overlay.IsVisible, Is.True);

        ai.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.That(overlay.IsVisible, Is.False);
    }

    [Test]
    public void CloseButton_HidesThePanelAgain()
    {
        var (window, _) = CreateWindow();
        var ai = window.FindControl<Button>("StatusAiButton")!;
        var close = window.FindControl<Button>("AiAssistantCloseButton")!;
        var overlay = window.FindControl<Grid>("AiAssistantOverlay")!;

        ai.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        close.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.That(overlay.IsVisible, Is.False);
    }

    [Test]
    public void Escape_ClosesThePanel()
    {
        var (window, _) = CreateWindow();
        var ai = window.FindControl<Button>("StatusAiButton")!;
        var overlay = window.FindControl<Grid>("AiAssistantOverlay")!;

        ai.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.That(overlay.IsVisible, Is.True);

        window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });

        Assert.That(overlay.IsVisible, Is.False);
    }

    private static T? Ancestor<T>(Visual child)
        where T : class
    {
        for (Visual? node = child; node is not null; node = node.GetVisualParent())
        {
            if (node is T match)
            {
                return match;
            }
        }

        return null;
    }

    private static (MainWindow Window, ShellViewModel Shell) CreateWindow()
    {
        var shell = new ShellViewModel(new SilentNavigation());
        var window = new MainWindow { DataContext = shell };
        return (window, shell);
    }
}
