using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Avalonia.Controls;
using CkanUI.App.Tests.Automation;
using CkanUI.ViewModels;
using NUnit.Framework;
using ShapeEllipse = Avalonia.Controls.Shapes.Ellipse;

namespace CkanUI.App.Tests;

/// <summary>
/// 底栏默认简洁化与悬停呼出测试：
/// 1. 默认仅显示小图标（状态圆点、AI图标、任务图标），文字与缩放控件隐藏；
/// 2. 悬停/展开后完整呼出文案与缩放控件；
/// 3. 移出后防抖平滑收起。
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class StatusBarCompactHoverTests
{
    [SetUp]
    public void SetUp() => HeadlessAppHost.EnsureInitialized();

    [Test]
    public void StatusBar_DefaultIsCompact()
    {
        var (window, _) = CreateWindow();

        Assert.That(window.IsStatusBarExpanded, Is.False, "底栏默认必须处于简洁收起状态");

        var surface = window.FindControl<Border>("StatusBarSurface");
        Assert.That(surface, Is.Not.Null, "底栏主表面必须存在");
        Assert.That(surface!.Classes.Contains("is-expanded"), Is.False, "默认不应含有 is-expanded 类");

        var dot = window.FindControl<ShapeEllipse>("ShellStatusDot");
        Assert.That(dot, Is.Not.Null, "状态小图标必须存在");

        var text = window.FindControl<TextBlock>("ShellStatusText");
        Assert.That(text, Is.Not.Null, "状态文本控件必须存在");

        var zoom = window.FindControl<StackPanel>("PageZoomCluster");
        Assert.That(zoom, Is.Not.Null, "缩放控件组必须存在");

        var aiText = window.FindControl<TextBlock>("StatusAiText");
        Assert.That(aiText, Is.Not.Null, "AI 助手文案控件必须存在");

        var taskDetails = window.FindControl<StackPanel>("StatusBarTaskDetails");
        Assert.That(taskDetails, Is.Not.Null, "后台任务详情必须存在");

        var versionBorder = window.FindControl<Border>("StatusVersionBorder");
        Assert.That(versionBorder, Is.Not.Null, "版本号边框必须存在");
    }

    [Test]
    public void StatusBar_SetExpanded_TogglesIsStatusBarExpandedAndClasses()
    {
        var (window, _) = CreateWindow();
        var surface = window.FindControl<Border>("StatusBarSurface")!;

        window.SetStatusBarExpanded(true);
        Assert.That(window.IsStatusBarExpanded, Is.True);
        Assert.That(surface.Classes.Contains("is-expanded"), Is.True);

        window.SetStatusBarExpanded(false);
        Assert.That(window.IsStatusBarExpanded, Is.False);
        Assert.That(surface.Classes.Contains("is-expanded"), Is.False);
    }

    [Test]
    public void StatusBar_StyleContract_EnforcesCompactAndExpandedRules()
    {
        var axamlPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..", "src", "CkanUI.App", "MainWindow.axaml");
        if (!File.Exists(axamlPath))
        {
            axamlPath = Path.GetFullPath("src/CkanUI.App/MainWindow.axaml");
        }

        Assert.That(File.Exists(axamlPath), Is.True, $"找不到 MainWindow.axaml: {axamlPath}");

        var doc = XDocument.Load(axamlPath);
        var styles = doc.Descendants().Where(e => e.Name.LocalName == "Style").ToList();

        string[] requiredTargets =
        {
            "ShellStatusText",
            "PageZoomCluster",
            "StatusAiText",
            "StatusBarTaskDetails",
            "StatusVersionBorder"
        };

        foreach (var target in requiredTargets)
        {
            var hasCompactStyle = styles.Any(s =>
            {
                var sel = (string?)s.Attribute("Selector") ?? "";
                return sel.Contains("StatusBarSurface") && sel.Contains(target) && !sel.Contains("pointerover") && !sel.Contains("is-expanded");
            });

            var hasExpandedStyle = styles.Any(s =>
            {
                var sel = (string?)s.Attribute("Selector") ?? "";
                return sel.Contains("StatusBarSurface") && sel.Contains(target) && (sel.Contains("pointerover") || sel.Contains("is-expanded"));
            });

            Assert.That(hasCompactStyle, Is.True, $"必须存在针对 {target} 的默认简洁样式定义");
            Assert.That(hasExpandedStyle, Is.True, $"必须存在针对 {target} 的悬停/展开呼出样式定义");
        }

        // 验证动画过渡 Transitions 声明存在，避免 IsVisible 突变生硬
        var transitions = doc.Descendants()
            .Where(e => e.Name.LocalName == "Style" && ((string?)e.Attribute("Selector") ?? "").Contains("StatusBarSurface"))
            .SelectMany(s => s.Descendants().Where(d => d.Name.LocalName == "Transitions"))
            .ToList();

        Assert.That(transitions.Count, Is.GreaterThanOrEqualTo(5), "底栏动画过渡必须包含 MaxWidth/Opacity/RenderTransform 等平滑过渡声明");
    }

    private static (MainWindow Window, ShellViewModel Shell) CreateWindow()
    {
        var shell = new ShellViewModel();
        var window = new MainWindow { DataContext = shell };
        return (window, shell);
    }
}
