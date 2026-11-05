using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;

using CkanUI.Ui.Tables;
using CkanUI.Ui.Theming;

using NUnit.Framework;

namespace CkanUI.Ui.Tests.Tables;

/// <summary>
/// 重排动画：排序/筛选之后行从旧位置滑到新位置，结束把 RenderTransform 原样还回去。
/// </summary>
[TestFixture]
public sealed class TableReorderMotionTests
{
    private const double RowHeight = 30d;

    public sealed record Row(string Name, int Rank);

    [SetUp]
    public void SetUp()
    {
        HeadlessHost.EnsureInitialized();
        Application.Current!.Resources[CkanMotionTokens.Base] = TimeSpan.FromMilliseconds(160);
    }

    [TearDown]
    public void TearDown()
    {
        Application.Current!.Resources.Remove(CkanMotionTokens.Base);
    }

    [Test]
    public void SortingSlidesRowsFromTheirOldPositions()
    {
        var (list, window) = BuildTable();

        // 初始 alpha(3), bravo(2), charlie(1)：按 Rank 降序后 charlie 从第三行跑到第一行。
        ListTableTools.SortBy(list, 0, true);
        Pump(window);

        var shifted = Containers(list)
            .Select(container => YOf(container))
            .Where(y => Math.Abs(y) > 1d)
            .ToList();

        Assert.That(shifted, Is.Not.Empty, "排序后应该有行从旧位置开始滑");

        for (var i = 0; i < 40 && Containers(list).Any(container => Math.Abs(YOf(container)) > 0.1d); i++)
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
        }

        Assert.Multiple(() =>
        {
            foreach (var container in Containers(list))
            {
                Assert.That(YOf(container), Is.EqualTo(0d).Within(0.1d), "落位之后位移要归零");
                Assert.That(container.RenderTransform, Is.Null, "落位之后不该再占着 RenderTransform");
            }
        });
    }

    [Test]
    public void WithoutMotionTokenNothingMoves()
    {
        Application.Current!.Resources.Remove(CkanMotionTokens.Base);
        var (list, window) = BuildTable();

        ListTableTools.SortBy(list, 0, true);
        Pump(window);

        Assert.That(Containers(list).Select(YOf), Has.None.GreaterThan(0.1d));
    }

    [Test]
    public void ExistingRenderTransformIsPutBack()
    {
        var (list, window) = BuildTable();
        var target = Containers(list)
            .First(container => (list.ItemFromContainer(container) as Row)?.Name == "charlie");
        var scale = new ScaleTransform(0.95d, 0.95d);
        target.RenderTransform = scale;

        ListTableTools.SortBy(list, 0, true);
        Pump(window);
        Assert.That(YOf(target), Is.Not.EqualTo(0d), "别的变换在的时候也要能滑");

        for (var i = 0; i < 40 && Math.Abs(YOf(target)) > 0.1d; i++)
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
        }

        Assert.Multiple(() =>
        {
            Assert.That(YOf(target), Is.EqualTo(0d).Within(0.1d));
            Assert.That(target.RenderTransform, Is.SameAs(scale), "结束后要把原来的变换还回去");
        });
    }

    private static (ListBox List, Window Window) BuildTable()
    {
        var rows = new ObservableCollection<Row>
        {
            new("alpha", 3),
            new("bravo", 2),
            new("charlie", 1),
        };

        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,120") };
        for (var i = 0; i < 2; i++)
        {
            var caption = new TextBlock { Text = i == 0 ? "名称" : "数量", Classes = { "CkanCaption" } };
            Grid.SetColumn(caption, i);
            header.Children.Add(caption);
        }

        var list = new ListBox
        {
            ItemsSource = rows,
            ItemTemplate = new FuncDataTemplate<Row>((row, _) => new Border
            {
                Height = RowHeight,
                Child = new TextBlock { Text = row?.Name ?? string.Empty },
            }),
        };

        var host = new StackPanel();
        host.Children.Add(new Border { Child = header });
        host.Children.Add(list);

        ListTableTools.SetColumns(list, "Name,Rank");
        ListTableTools.SetEnabled(list, true);

        var window = new Window { Width = 400, Height = 320, Content = host };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        return (list, window);
    }

    private static void Pump(Window window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    private static IEnumerable<Control> Containers(ListBox list) => list.GetRealizedContainers();

    private static double YOf(Control container)
        => container.RenderTransform switch
        {
            TranslateTransform translate => translate.Y,
            TransformGroup group => group.Children.OfType<TranslateTransform>().Select(t => t.Y).FirstOrDefault(),
            _ => 0d,
        };
}
