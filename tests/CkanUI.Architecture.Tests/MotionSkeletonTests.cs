using System;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace CkanUI.Architecture.Tests;

/// <summary>
/// 第二阶段动效骨骼的源码级约束（用户 m02458 的验收清单）。
/// 这一阶段刻意不写任何物理动画代码：只要求「会动的节点不参与布局」「显隐不用 IsVisible 硬切」
/// 这两条结构前提在源码里成立，第三阶段才可能只改样式不改树。
/// </summary>
[TestFixture]
public sealed class MotionSkeletonTests
{
    private static string Read(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { RepoRoot.Path }.Concat(parts).ToArray()));

    [Test]
    public void MainWindow_OverlaysUseStateClassesInsteadOfVisibility()
    {
        var xaml = Read("src", "CkanUI.App", "MainWindow.axaml");

        Assert.That(xaml, Does.Contain("Classes=\"overlay-drawer\""), "日志抽屉必须挂 overlay-drawer 状态类");
        Assert.That(xaml, Does.Contain("Classes.is-open=\"{Binding IsLogDrawerOpen}\""));
        Assert.That(xaml, Does.Contain("Classes=\"floating-pill-bar\""), "未保存浮条必须挂 floating-pill-bar 状态类");
        Assert.That(xaml, Does.Contain("Classes.is-open=\"{Binding HasUnsavedChanges}\""));
        Assert.That(xaml, Does.Contain("IsHitTestVisible=\"{Binding IsLogDrawerOpen}\""), "隐藏的抽屉不能被点到");
        Assert.That(xaml, Does.Contain("IsHitTestVisible=\"{Binding HasUnsavedChanges}\""));

        // 反向断言：一旦有人把显隐换回 IsVisible，退场动画会在第一帧被截断。
        Assert.That(xaml, Does.Not.Contain("IsVisible=\"{Binding IsLogDrawerOpen}\""));
        Assert.That(xaml, Does.Not.Contain("IsVisible=\"{Binding HasUnsavedChanges}\""));
        Assert.That(xaml, Does.Not.Contain("IsVisible=\"{Binding LogDrawer"), "抽屉常驻可视化树，不能绑 IsNotNull");
    }

    [Test]
    public void MainWindow_StatusBarMergesLogToggleAndVersion()
    {
        var xaml = Read("src", "CkanUI.App", "MainWindow.axaml");

        Assert.That(xaml, Does.Contain("ToggleLogDrawerCommand"), "状态栏中段是日志抽屉的开关入口");
        Assert.That(xaml, Does.Contain("LogStatusText"));
        Assert.That(xaml, Does.Contain("x:Name=\"StatusVersionText\""));
        Assert.That(xaml, Does.Contain("RowDefinitions=\"*,28\""), "三明治结构：内容 + 固定 28px 状态栏");
    }

    [Test]
    public void InstancesPage_StacksEmptyAndDataLayersInOneCell()
    {
        var xaml = Read("src", "CkanUI.App", "Views", "InstancesPageView.axaml");

        Assert.That(xaml, Does.Contain("Classes=\"view-layer\""));
        Assert.That(xaml, Does.Contain("Classes.is-active=\"{Binding ShowEmptyState}\""));
        Assert.That(xaml, Does.Contain("Classes.is-active=\"{Binding !ShowEmptyState}\""));
        Assert.That(xaml, Does.Contain("Classes.is-active=\"{Binding HasSelection}\""));
        Assert.That(xaml, Does.Contain("Classes.is-active=\"{Binding !HasSelection}\""));
        Assert.That(xaml, Does.Not.Contain("IsVisible=\"{Binding ShowEmptyState}\""), "空态与列表要同格叠放，不能硬切");
    }

    [Test]
    public void PrimaryCards_CarryStaggerAnchors()
    {
        var settings = Read("src", "CkanUI.App", "Views", "SettingsPageView.axaml");
        var editor = Read("src", "CkanUI.App", "Views", "ModPackEditorPageView.axaml");
        var instances = Read("src", "CkanUI.App", "Views", "InstancesPageView.axaml");

        Assert.That(settings, Does.Contain("Classes=\"stagger-card\""));
        Assert.That(CountOccurrences(editor, "stagger-card"), Is.GreaterThanOrEqualTo(3), "编辑器三个一级区块都要有锚点");
        Assert.That(CountOccurrences(instances, "stagger-card"), Is.GreaterThanOrEqualTo(2));
    }

    [Test]
    public void Styles_ProvideTheSkeletonClasses()
    {
        var styles = Read("src", "CkanUI.App", "Resources", "FluentStyles.axaml");

        foreach (var selector in new[]
                 {
                     "Border.overlay-drawer",
                     "Border.floating-pill-bar",
                     "Control.view-layer",
                     "Control.view-layer.is-active",
                     "Control.stagger-card",
                     "Control.is-invalid",
                 })
        {
            Assert.That(styles, Does.Contain("Selector=\"" + selector + "\""), "缺少动效骨骼状态类：" + selector);
        }

        // 状态类只允许动 Opacity / RenderTransform：一旦出现 Margin / Height / Width 就会触发重排。
        foreach (var property in new[] { "Margin", "Height", "Width", "Padding" })
        {
            Assert.That(
                ExtractSkeletonBlock(styles),
                Does.Not.Contain("Property=\"" + property + "\""),
                "动效骨骼不允许改 " + property + "（会触发 Measure/Arrange）");
        }
    }

    [Test]
    public void NoPhysicalAnimationCodeRemains()
    {
        var theming = Path.Combine(RepoRoot.Path, "src", "CkanUI.App", "Theming");

        // 第二阶段删掉的旧动效实现：抽屉/提示条滑入、页面错峰入场、校验抖动。
        // 它们的替代品是 XAML 状态类 + 第三阶段的过渡，所以这些文件不该再出现。
        Assert.That(File.Exists(Path.Combine(theming, "MotionPrimitives.cs")), Is.False, "MotionPrimitives 应已删除");
        Assert.That(File.Exists(Path.Combine(theming, "ShakeBehavior.cs")), Is.False, "ShakeBehavior 应已删除");

        var offenders = Directory.EnumerateFiles(Path.Combine(RepoRoot.Path, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(file =>
            {
                var text = File.ReadAllText(file);
                return text.Contains("SlideInAsync", StringComparison.Ordinal)
                       || text.Contains("StaggerEntranceAsync", StringComparison.Ordinal);
            })
            .Select(file => Path.GetRelativePath(RepoRoot.Path, file))
            .ToArray();

        Assert.That(offenders, Is.Empty, "第二阶段不许有物理动画代码；第三阶段再统一引入");

        // 注：Theming/SkeletonPulse.cs 里的骨架屏脉动不算物理动画——它只循环 Opacity，
        // 不位移、不参与布局，是加载态的静态表现，所以这条断言不拦它。
    }

    /// <summary>把「动效骨骼」注释段之间的样式块切出来，供属性白名单断言使用。</summary>
    private static string ExtractSkeletonBlock(string styles)
    {
        const string start = "<!-- 动效骨骼（第二阶段：只有状态类，没有动画代码） -->";
        const string end = "<!-- 表格 (DataGrid) 现代流畅微交互与视觉优化";
        var from = styles.IndexOf(start, StringComparison.Ordinal);
        var to = styles.IndexOf(end, StringComparison.Ordinal);
        Assert.That(from, Is.GreaterThanOrEqualTo(0), "找不到动效骨骼段");
        Assert.That(to, Is.GreaterThan(from), "动效骨骼段没有结束标记");
        return styles.Substring(from, to - from);
    }

    private static int CountOccurrences(string text, string needle)
    {
        var count = 0;
        var index = text.IndexOf(needle, StringComparison.Ordinal);
        while (index >= 0)
        {
            count++;
            index = text.IndexOf(needle, index + needle.Length, StringComparison.Ordinal);
        }

        return count;
    }
}
