using System;
using Avalonia.Controls;
using NUnit.Framework;

namespace CkanUI.App.Tests;

/// <summary>
/// 底栏 AI 助手的开合逻辑：面板要么给出页面，要么给出人话解释，关掉时一定释放页面实例。
/// 全部用假页面与假控件——不加载插件 dll，也不驱动渲染（headless 下驱帧会挂死，见 docs/design/plugins.md §11.7）。
/// </summary>
[TestFixture]
public class AiAssistantLauncherTests
{
    [Test]
    public void Open_ResolvesThePageOnceAndHandsBackTheSameView()
    {
        var page = new FakePage();
        var view = new Border();
        var resolved = 0;
        var launcher = new Ai.AiAssistantLauncher(
            key =>
            {
                resolved++;
                Assert.That(key, Is.EqualTo(Ai.AiAssistantLauncher.PageKey));
                return page;
            },
            built =>
            {
                Assert.That(built, Is.SameAs(page));
                return view;
            });

        Assert.That(launcher.Open(out var error), Is.True);
        Assert.That(error, Is.Null);
        Assert.That(launcher.IsOpen, Is.True);
        Assert.That(launcher.View, Is.SameAs(view));
        Assert.That(launcher.ViewModel, Is.SameAs(page));

        // 第二次点按钮应该由调用方收起；Open 本身不再解析页面（解析一次就够，否则会多造一个页面实例而漏释放）。
        Assert.That(launcher.Open(out _), Is.True);
        Assert.That(resolved, Is.EqualTo(1));
    }

    [Test]
    public void Open_WhenPluginIsDisabled_ExplainsHowToEnableIt()
    {
        var launcher = new Ai.AiAssistantLauncher(_ => null, _ => new Border());

        Assert.That(launcher.Open(out var error), Is.False);
        Assert.That(error, Does.Contain("没有启用"));
        Assert.That(error, Does.Contain("插件"));
        Assert.That(launcher.IsOpen, Is.False);
        Assert.That(launcher.View, Is.Null);
    }

    [Test]
    public void Open_WhenViewIsMissing_DisposesThePageAndSaysSo()
    {
        var page = new FakePage();
        var launcher = new Ai.AiAssistantLauncher(_ => page, _ => null);

        Assert.That(launcher.Open(out var error), Is.False);
        Assert.That(error, Does.Contain("找不到对应视图"));
        Assert.That(page.DisposeCount, Is.EqualTo(1), "视图没建起来，页面实例不能留在内存里");
    }

    [Test]
    public void Open_WhenBuilderThrows_DisposesThePageAndKeepsTheMessage()
    {
        var page = new FakePage();
        var launcher = new Ai.AiAssistantLauncher(
            _ => page,
            _ => throw new InvalidOperationException("渲染器炸了"));

        Assert.That(launcher.Open(out var error), Is.False);
        Assert.That(error, Does.Contain("渲染器炸了"));
        Assert.That(page.DisposeCount, Is.EqualTo(1));
        Assert.That(launcher.IsOpen, Is.False);
    }

    [Test]
    public void Open_WhenResolverThrows_ReportsInsteadOfCrashingTheButton()
    {
        var launcher = new Ai.AiAssistantLauncher(
            _ => throw new InvalidOperationException("插件目录不可读"),
            _ => new Border());

        Assert.That(launcher.Open(out var error), Is.False);
        Assert.That(error, Does.Contain("插件目录不可读"));
        Assert.That(launcher.IsOpen, Is.False);
    }

    [Test]
    public void Close_DisposesThePageAndIsIdempotent()
    {
        var page = new FakePage();
        var launcher = new Ai.AiAssistantLauncher(_ => page, _ => new Border());
        Assert.That(launcher.Open(out _), Is.True);

        launcher.Close();
        launcher.Close();

        Assert.That(page.DisposeCount, Is.EqualTo(1));
        Assert.That(launcher.IsOpen, Is.False);
        Assert.That(launcher.View, Is.Null);
        Assert.That(launcher.ViewModel, Is.Null);
    }

    [Test]
    public void Close_WhenPageThrowsOnDispose_StillClosesThePanel()
    {
        var page = new FakePage { ThrowOnDispose = true };
        var launcher = new Ai.AiAssistantLauncher(_ => page, _ => new Border());
        Assert.That(launcher.Open(out _), Is.True);

        Assert.DoesNotThrow(launcher.Close);
        Assert.That(page.DisposeCount, Is.EqualTo(1));
        Assert.That(launcher.IsOpen, Is.False);
    }

    [Test]
    public void OpenAfterClose_ResolvesAFreshPage()
    {
        var resolved = 0;
        var launcher = new Ai.AiAssistantLauncher(
            _ => { resolved++; return new FakePage(); },
            _ => new Border());

        Assert.That(launcher.Open(out _), Is.True);
        launcher.Close();
        Assert.That(launcher.Open(out _), Is.True);

        Assert.That(resolved, Is.EqualTo(2), "关掉后重新打开必须重造页面实例（旧的已经释放，不能复用）");
    }

    private sealed class FakePage : IDisposable
    {
        public int DisposeCount { get; private set; }

        public bool ThrowOnDispose { get; init; }

        public void Dispose()
        {
            DisposeCount++;
            if (ThrowOnDispose)
            {
                throw new InvalidOperationException("释放时炸了");
            }
        }
    }
}
