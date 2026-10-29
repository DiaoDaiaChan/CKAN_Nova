using System.IO;
using System.Linq;
using NUnit.Framework;

namespace CkanUI.Architecture.Tests;

/// <summary>
/// 外壳窗口的源码级约束（用户 m03124 报「窗口无法拖动」）。
/// 窗口用 <c>ExtendClientAreaToDecorationsHint</c> 把客户区铺到标题栏上，系统就不再替我们拖窗口，
/// 必须由顶栏自己调 <c>BeginMoveDrag</c>；这段一旦被重构删掉，整窗立刻又变得不可移动。
/// </summary>
[TestFixture]
public sealed class WindowChromeTests
{
    private static string Read(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { RepoRoot.Path }.Concat(parts).ToArray()));

    [Test]
    public void ExtendedClientArea_TitleBarHandlesItsOwnDragging()
    {
        var xaml = Read("src", "CkanUI.App", "MainWindow.axaml");

        Assert.That(
            xaml,
            Does.Contain("ExtendClientAreaToDecorationsHint=\"True\""),
            "前提：窗口把客户区铺到了标题栏上，系统不再负责拖动。");

        var handlers = xaml.Split("PointerPressed=\"OnTitleBarPointerPressed\"").Length - 1;
        Assert.That(handlers, Is.GreaterThanOrEqualTo(2), "顶部 34px 拖拽条与侧栏品牌区都要能拖窗口。");
        Assert.That(xaml, Does.Contain("Height=\"34\""), "拖拽条高度仍是 34px。");
    }

    [Test]
    public void DragHandler_MovesAndRestoresTheWindow()
    {
        var code = Read("src", "CkanUI.App", "MainWindow.axaml.cs");

        Assert.That(code, Does.Contain("private void OnTitleBarPointerPressed"), "处理器必须是这个签名（XAML 按名字绑定）。");
        Assert.That(code, Does.Contain("BeginMoveDrag(e)"), "没有 BeginMoveDrag 就完全拖不动窗口。");

        // 点在前进/后退按钮上不能当拖动，否则按钮按下即开始移动窗口。
        Assert.That(code, Does.Contain("FindAncestorOfType<Button>"), "必须排除按钮命中的指针事件。");

        // 系统不再处理标题栏双击，最大化/还原要自己实现。
        Assert.That(code, Does.Contain("e.ClickCount == 2"), "双击标题条要切最大化/还原。");
        Assert.That(code, Does.Contain("WindowState.Maximized"), "最大化状态下拖动应先还原。");
    }
}
