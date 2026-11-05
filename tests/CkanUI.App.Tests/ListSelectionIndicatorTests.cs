using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.VisualTree;
using CkanUI.App.Tests.Automation;
using CkanUI.ViewModels;
using CkanUI.ViewModels.Shell;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Path = System.IO.Path;

namespace CkanUI.App.Tests;

/// <summary>
/// 列表选中指示条的唯一性契约。
/// 背景：Avalonia 12 的 ListBoxItem 模板自带 Rectangle#SelectionIndicator（默认吃系统强调色），
/// 而 7 个页面又各自贴了一条 Rectangle.SelectionBar，同一个选中行就并排出现两条竖杠。
/// 现在的口径是「模板那条是唯一指示条，颜色/宽度/圆角走 Ckan token，视图不再自绘」。
/// </summary>
[TestFixture]
public sealed class ListSelectionIndicatorTests
{
    [Test]
    public void SelectedRow_HasExactlyOneIndicator_PaintedWithTheCkanAccent()
    {
        HeadlessAppHost.EnsureInitialized();
        var list = new ListBox { ItemsSource = new[] { "alpha", "bravo", "charlie" }, SelectedIndex = 1 };
        var window = HeadlessAppHost.CreateTestWindow(list);
        HeadlessAppHost.PumpJobs();

        var item = list.GetVisualDescendants().OfType<ListBoxItem>().Single(i => i.IsSelected);
        var indicators = item.GetVisualDescendants().OfType<Rectangle>()
            .Where(r => r.Name == "SelectionIndicator")
            .ToList();

        Assert.That(indicators, Has.Count.EqualTo(1), "选中行只允许存在一条指示条");

        var bar = indicators[0];
        Assert.That(bar.IsEffectivelyVisible, Is.True, "选中行的指示条必须可见");
        Assert.That(bar.Width, Is.EqualTo(3d), "指示条宽度走 token 的 3px");
        Assert.That(bar.RadiusX, Is.EqualTo(1.5d));
        Assert.That(bar.RadiusY, Is.EqualTo(1.5d));

        Assert.That(bar.TryFindResource("CkanAccentBrush", bar.ActualThemeVariant, out var resource), Is.True,
            "必须能解析 CkanAccentBrush");
        var expected = ((ISolidColorBrush)resource!).Color;
        Assert.That(bar.Fill, Is.InstanceOf<ISolidColorBrush>());
        Assert.That(((ISolidColorBrush)bar.Fill!).Color, Is.EqualTo(expected),
            "指示条颜色必须是 CkanAccentBrush，不能是模板默认的系统强调色");

        // 自绘那条已经全部删掉，视觉树里不应再有任何 SelectionBar。
        var legacy = window.GetVisualDescendants().OfType<Rectangle>()
            .Where(r => r.Classes.Contains("SelectionBar"))
            .ToList();
        Assert.That(legacy, Is.Empty, "视图不应再自绘 Rectangle.SelectionBar");

        window.Close();
    }

    [Test]
    public void RealPage_SelectedRowHasExactlyOneIndicator()
    {
        HeadlessAppHost.EnsureInitialized();
        Window? window = null;
        var services = ServiceRegistration.Build(() => window);
        var shell = services.GetRequiredService<ShellViewModel>();
        var mainWindow = new MainWindow { DataContext = shell, Width = 1280, Height = 800 };
        window = mainWindow;
        window.Show();
        HeadlessAppHost.PumpJobs();

        shell.NavigateCommand.Execute("cache");
        HeadlessAppHost.PumpJobs();

        var list = window.GetVisualDescendants().OfType<ListBox>().First(l => l.ItemCount > 0);
        list.SelectedIndex = 0;
        HeadlessAppHost.PumpJobs();

        var selected = list.GetVisualDescendants().OfType<ListBoxItem>().Single(i => i.IsSelected);
        var indicators = selected.GetVisualDescendants().OfType<Rectangle>()
            .Where(r => r.Name == "SelectionIndicator")
            .ToList();
        Assert.That(indicators, Has.Count.EqualTo(1), "真实页面里选中行也只能有一条指示条");
        Assert.That(indicators[0].IsEffectivelyVisible, Is.True);
        Assert.That(indicators[0].Width, Is.EqualTo(3d));

        Assert.That(indicators[0].TryFindResource("CkanAccentBrush", indicators[0].ActualThemeVariant, out var resource),
            Is.True);
        Assert.That(((ISolidColorBrush)indicators[0].Fill!).Color, Is.EqualTo(((ISolidColorBrush)resource!).Color),
            "真实页面里指示条也必须是 CkanAccentBrush，不能落回系统强调色");

        var legacy = window.GetVisualDescendants().OfType<Rectangle>()
            .Where(r => r.Classes.Contains("SelectionBar"))
            .ToList();
        Assert.That(legacy, Is.Empty, "真实页面里不应再有自绘 SelectionBar");

        // 命令面板浮层（Ctrl+K）：过去也是自绘 paletteBar + 模板条并排两条，现在只留模板那条。
        var palette = services.GetRequiredService<CkanUI.ViewModels.CommandPalette.CommandPaletteViewModel>();
        mainWindow.AttachCommandPalette(
            palette,
            services.GetRequiredService<CkanUI.Services.Abstractions.Commands.ICommandCatalog>(),
            services.GetRequiredService<Func<string, string>>());
        palette.OpenCommand.Execute(null);
        HeadlessAppHost.PumpJobs();

        var paletteList = window.GetVisualDescendants().OfType<ListBox>()
            .First(l => l.Classes.Contains("paletteList"));
        var paletteSelected = paletteList.GetVisualDescendants().OfType<ListBoxItem>()
            .Where(i => i.IsSelected)
            .ToList();
        Assert.That(paletteSelected, Has.Count.EqualTo(1), "命令面板必须有一条选中行");
        var paletteIndicators = paletteSelected[0].GetVisualDescendants().OfType<Rectangle>()
            .Where(r => r.Name == "SelectionIndicator")
            .ToList();
        Assert.That(paletteIndicators, Has.Count.EqualTo(1), "命令面板选中行也只能有一条指示条");
        Assert.That(paletteIndicators[0].Width, Is.EqualTo(3d));
        Assert.That(window.GetVisualDescendants().OfType<Rectangle>()
            .Where(r => r.Classes.Contains("paletteBar")).ToList(), Is.Empty,
            "命令面板不应再自绘 paletteBar");

        window.Close();
        services.Dispose();
    }

    [Test]
    public void NoViewDrawsItsOwnSelectionBar()
    {
        var root = ResolveRepositoryRoot();
        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "src"), "*.axaml", SearchOption.AllDirectories))
        {
            // 注释里的历史说明不算自绘，先剥掉 XML 注释再判断。
            var text = Regex.Replace(File.ReadAllText(file), "<!--.*?-->", string.Empty, RegexOptions.Singleline);
            if (text.Contains("Classes=\"SelectionBar\"", StringComparison.Ordinal) ||
                text.Contains("Rectangle.SelectionBar", StringComparison.Ordinal) ||
                text.Contains("Classes=\"paletteBar\"", StringComparison.Ordinal) ||
                text.Contains("Rectangle.paletteBar", StringComparison.Ordinal))
            {
                offenders.Add(Path.GetRelativePath(root, file));
            }
        }

        Assert.That(offenders, Is.Empty,
            "选中指示条由 ListBoxItem 模板统一承担，视图不得自绘：" + string.Join(", ", offenders));
    }

    [Test]
    public void SharedStyles_DefineTheSingleIndicator()
    {
        var root = ResolveRepositoryRoot();
        var styles = File.ReadAllText(Path.Combine(root, "src", "CkanUI.App", "Resources", "FluentStyles.axaml"));
        Assert.That(styles,
            Does.Contain("ListBoxItem:selected /template/ Rectangle#SelectionIndicator"),
            "共享样式必须把模板自带的指示条染成 Ckan 的强调色");
        Assert.That(styles, Does.Contain("CkanAccentBrush"));
    }

    private static string ResolveRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "src", "CkanUI.App", "CkanUI.App.csproj")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("未能从测试输出目录向上找到仓库根");
    }
}