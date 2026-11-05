using System.Collections.ObjectModel;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.VisualTree;

using CkanUI.Ui.Tables;
using CkanUI.Ui.Tests;

using NUnit.Framework;

namespace CkanUI.Ui.Tests.Tables;

/// <summary>
/// 表头与行的对齐契约：两者的列宽、左右边缘在拖动前后都必须一致。
/// 真实页面里行会被 ListBoxItem 的内边距推进来、右侧还会被竖直滚动条吃掉十几像素，
/// 表头若按自己的容器铺满，列宽对上了也照样看着错位。
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class ListTableToolsAlignmentTests
{
    private sealed record Row(string Name, int Count);

    [SetUp]
    public void SetUp() => HeadlessHost.EnsureInitialized();

    [Test]
    public void HeaderAndRowsStayAligned_WhileAndAfterDraggingAColumnEdge()
    {
        var rows = new ObservableCollection<Row>(
            Enumerable.Range(0, 30).Select(i => new Row("row-" + i, i)));

        var headerGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,150,210,56") };
        for (var i = 0; i < 4; i++)
        {
            var caption = new TextBlock { Text = "列" + i };
            caption.Classes.Add("CkanCaption");
            Grid.SetColumn(caption, i);
            headerGrid.Children.Add(caption);
        }

        var header = new Border { Padding = new Thickness(12, 5), Child = headerGrid };

        var list = new ListBox
        {
            ItemsSource = rows,
            ItemTemplate = new FuncDataTemplate<Row>((row, _) =>
            {
                if (row is null)
                {
                    return new Panel();
                }

                var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,150,210,56") };
                var name = new TextBlock { Text = row.Name };
                Grid.SetColumn(name, 0);
                grid.Children.Add(name);
                return new Border { Padding = new Thickness(12, 3), Child = grid };
            }),
        };
        list.ItemsPanel = new FuncTemplate<Panel?>(() => new VirtualizingStackPanel());
        ScrollViewer.SetHorizontalScrollBarVisibility(list, Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled);
        ListTableTools.SetColumns(list, "Name,Count,Value,-");
        ListTableTools.SetEnabled(list, true);

        // KspConfig 那种结构：表头在上一行，列表再包一层 Grid。
        var body = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        var listHost = new Grid();
        listHost.Children.Add(list);
        Grid.SetRow(listHost, 1);
        body.Children.Add(header);
        body.Children.Add(listHost);

        var window = new Window { Content = body, Width = 1000, Height = 520 };
        window.Show();
        Pump(window);

        AssertAligned(window, list, headerGrid, "静止时");

        var handle = headerGrid.Children.OfType<Border>()
            .FirstOrDefault(border => border.Width == 6 && border.Cursor is not null);
        Assert.That(handle, Is.Not.Null, "表头每列右缘都要有拖动把手");

        var activeHandle = handle!;
        var point = activeHandle.TranslatePoint(new Point(activeHandle.Bounds.Width / 2, 4), window);
        Assert.That(point.HasValue, Is.True);
        var anchor = point.GetValueOrDefault();
        window.MouseMove(anchor);
        window.MouseDown(anchor, MouseButton.Left);
        window.MouseMove(anchor + new Vector(90, 0));
        Pump(window);

        AssertAligned(window, list, headerGrid, "拖动过程中");

        window.MouseUp(anchor + new Vector(90, 0), MouseButton.Left);
        Pump(window);

        AssertAligned(window, list, headerGrid, "松开之后");
    }

    private static void Pump(Window window)
    {
        window.UpdateLayout();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    private static void AssertAligned(Window window, ListBox list, Grid header, string when)
    {
        var rowGrids = list.GetRealizedContainers()
            .Select(container => container.GetVisualDescendants().OfType<Grid>()
                .FirstOrDefault(grid => grid.ColumnDefinitions.Count == 4))
            .Where(grid => grid is not null)
            .Cast<Grid>()
            .ToList();
        Assert.That(rowGrids, Is.Not.Empty, when + "至少要有已实例化的行");

        var headerWidths = header.ColumnDefinitions.Select(definition => definition.ActualWidth).ToArray();
        var headerLeft = header.TranslatePoint(default, window)!.Value.X;
        var headerRight = header.TranslatePoint(new Point(header.Bounds.Width, 0), window)!.Value.X;

        foreach (var row in rowGrids)
        {
            var rowWidths = row.ColumnDefinitions.Select(definition => definition.ActualWidth).ToArray();
            var rowLeft = row.TranslatePoint(default, window)!.Value.X;
            var rowRight = row.TranslatePoint(new Point(row.Bounds.Width, 0), window)!.Value.X;

            Assert.Multiple(() =>
            {
                Assert.That(rowWidths, Is.EqualTo(headerWidths).Within(0.6), when + "逐列宽度必须一致");
                Assert.That(rowLeft, Is.EqualTo(headerLeft).Within(0.6), when + "左缘必须对齐");
                Assert.That(rowRight, Is.EqualTo(headerRight).Within(0.6), when + "右缘必须对齐");
            });
        }
    }
}
