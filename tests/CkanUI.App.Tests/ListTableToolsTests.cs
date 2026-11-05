using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.VisualTree;
using CkanUI.App.Tests.Automation;
using CkanUI.Ui.Tables;
using NUnit.Framework;

namespace CkanUI.App.Tests;

/// <summary>手画表头表格的筛选/排序/拖动工具。</summary>
[TestFixture]
public sealed class ListTableToolsTests
{
    private sealed record Row(string Name, int Count);

    [SetUp]
    public void SetUp() => HeadlessAppHost.EnsureInitialized();

    private static (Window Window, ListBox List, Grid Header) CreateTable(ObservableCollection<Row> rows)
    {
        var headerGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,120") };
        var nameCaption = new TextBlock { Text = "名称" };
        nameCaption.Classes.Add("CkanCaption");
        Grid.SetColumn(nameCaption, 0);
        var countCaption = new TextBlock { Text = "数量" };
        countCaption.Classes.Add("CkanCaption");
        Grid.SetColumn(countCaption, 1);
        headerGrid.Children.Add(nameCaption);
        headerGrid.Children.Add(countCaption);

        var header = new Border { Child = headerGrid };

        var list = new ListBox
        {
            ItemsSource = rows,
            ItemTemplate = new FuncDataTemplate<Row>((row, _) =>
            {
                if (row is null)
                {
                    return new Panel();
                }

                var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,120") };
                var name = new TextBlock { Text = row.Name };
                Grid.SetColumn(name, 0);
                var count = new TextBlock { Text = row.Count.ToString() };
                Grid.SetColumn(count, 1);
                grid.Children.Add(name);
                grid.Children.Add(count);
                return grid;
            }),
        };

        ListTableTools.SetColumns(list, "Name,Count");
        ListTableTools.SetEnabled(list, true);

        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        Grid.SetRow(header, 0);
        Grid.SetRow(list, 1);
        root.Children.Add(header);
        root.Children.Add(list);

        var window = HeadlessAppHost.CreateTestWindow(root);
        HeadlessAppHost.PumpJobs();
        return (window, list, headerGrid);
    }

    [Test]
    public void DraggingAColumnEdge_KeepsHeaderAndRowsAligned()
    {
        var rows = new ObservableCollection<Row> { new("alpha", 3), new("bravo", 1) };
        var (window, list, header) = CreateTable(rows);

        // 把手是表头里 Width=6、带 SizeWestEast 光标的 Border。
        var handle = header.Children.OfType<Border>()
            .FirstOrDefault(b => b.Width == 6 && b.Cursor is not null);
        Assert.That(handle, Is.Not.Null, "表头每列右缘都要有拖动把手");

        var handleCentre = handle!.TranslatePoint(new Point(handle!.Bounds.Width / 2, 4), window);
        Assert.That(handleCentre, Is.Not.Null);

        window.MouseMove(handleCentre!.Value);
        window.MouseDown(handleCentre!.Value, MouseButton.Left);
        window.MouseMove(handleCentre!.Value + new Vector(80, 0));
        window.MouseUp(handleCentre!.Value + new Vector(80, 0), MouseButton.Left);
        HeadlessAppHost.PumpJobs();
        window.UpdateLayout();
        HeadlessAppHost.PumpJobs();

        // 拖完：表头与每一行的列宽必须逐列相同，右缘也不能错位。
        var rowGrids = list.GetRealizedContainers()
            .Select(container => container.GetVisualDescendants().OfType<Grid>().FirstOrDefault(g => g.ColumnDefinitions.Count == 2))
            .Where(grid => grid is not null)
            .Cast<Grid>()
            .ToList();
        Assert.That(rowGrids, Is.Not.Empty, "至少要有已实例化的行才能比对");

        var headerWidths = header.ColumnDefinitions.Select(d => d.ActualWidth).ToArray();
        foreach (var rowGrid in rowGrids)
        {
            var rowWidths = rowGrid.ColumnDefinitions.Select(d => d.ActualWidth).ToArray();
            Assert.That(rowWidths, Is.EqualTo(headerWidths).Within(0.6), "行与表头的列宽必须一致");
        }

        var headerRight = header.TranslatePoint(new Point(header.Bounds.Width, 0), window)!.Value.X;
        var headerLeft = header.TranslatePoint(new Point(0, 0), window)!.Value.X;
        foreach (var rowGrid in rowGrids)
        {
            var rowRight = rowGrid.TranslatePoint(new Point(rowGrid.Bounds.Width, 0), window)!.Value.X;
            var rowLeft = rowGrid.TranslatePoint(new Point(0, 0), window)!.Value.X;
            Assert.That(
                rowRight,
                Is.EqualTo(headerRight).Within(0.6),
                $"行模板的右缘要和表头右缘对齐：header=[{headerLeft},{headerRight}] w={header.Bounds.Width}；row=[{rowLeft},{rowRight}] w={rowGrid.Bounds.Width}；" +
                $"headerCols=[{string.Join(",", headerWidths)}] rowCols=[{string.Join(",", rowGrid.ColumnDefinitions.Select(d => d.ActualWidth))}]");
        }
    }

    private static string[] Names(ListBox list)
        => list.ItemsSource!.Cast<Row>().Select(r => r.Name).ToArray();

    [Test]
    public void HeaderGetsFilterButtons_AndClickingSorts()
    {
        var rows = new ObservableCollection<Row> { new("alpha", 3), new("bravo", 1), new("charlie", 2) };
        var (_, list, header) = CreateTable(rows);

        var buttons = header.GetLogicalDescendants().OfType<Button>().Where(b => b.Classes.Contains("table-filter")).ToArray();
        Assert.That(buttons, Has.Length.EqualTo(2), "两个可排序列各一个筛选按钮");

        ListTableTools.SortBy(list, 1, false);
        HeadlessAppHost.PumpJobs();
        Assert.That(Names(list), Is.EqualTo(new[] { "bravo", "charlie", "alpha" }), "按数量升序");

        ListTableTools.SortBy(list, 1, true);
        HeadlessAppHost.PumpJobs();
        Assert.That(Names(list), Is.EqualTo(new[] { "alpha", "charlie", "bravo" }), "按数量降序");

        ListTableTools.SortBy(list, 1, null);
        HeadlessAppHost.PumpJobs();
        Assert.That(Names(list), Is.EqualTo(new[] { "alpha", "bravo", "charlie" }), "取消排序回到源顺序");
    }

    [Test]
    public void FiltersCombineAcrossColumns_AndSurviveItemsSourceReplacement()
    {
        var rows = new ObservableCollection<Row> { new("alpha", 3), new("bravo", 1), new("charlie", 2) };
        var (_, list, _) = CreateTable(rows);

        ListTableTools.SetFilter(list, 0, "a");
        HeadlessAppHost.PumpJobs();
        Assert.That(Names(list), Is.EqualTo(new[] { "alpha", "bravo", "charlie" }), "三行都含 a");

        ListTableTools.SetFilter(list, 0, "a -l");
        HeadlessAppHost.PumpJobs();
        Assert.That(Names(list), Is.EqualTo(new[] { "bravo" }), "-l 排除含 l 的行");

        ListTableTools.ClearFilters(list);
        HeadlessAppHost.PumpJobs();
        Assert.That(Names(list), Has.Length.EqualTo(3));

        ListTableTools.SetFilter(list, 1, "2");
        HeadlessAppHost.PumpJobs();
        Assert.That(Names(list), Is.EqualTo(new[] { "charlie" }), "按数量文本筛选");

        var replacement = new ObservableCollection<Row> { new("delta", 2), new("echo", 5) };
        list.ItemsSource = replacement;
        HeadlessAppHost.PumpJobs();
        Assert.That(list.ItemsSource!.GetType().Name, Is.EqualTo("TableView"), "换集合后应重新包装视图");
        Assert.That(Names(list), Is.EqualTo(new[] { "delta" }), "换集合后筛选仍然生效");
    }

    [Test]
    public void SkippedColumnsGetNoFilterButton_AndCannotBeSorted()
    {
        var rows = new ObservableCollection<Row> { new("alpha", 3), new("bravo", 1) };
        var (_, list, header) = CreateTable(rows);
        _ = header;

        ListTableTools.SetColumns(list, "-,Name");
        ListTableTools.SortBy(list, 0, false);
        HeadlessAppHost.PumpJobs();
        Assert.That(Names(list), Is.EqualTo(new[] { "alpha", "bravo" }), "跳过的列不参与排序");
    }

    /// <summary>表头不一定是列表的直接兄弟：页面把列表包一层 Grid 时也要找到（KspConfig 就是这种结构）。</summary>
    [Test]
    public void HeaderIsFoundThroughOneWrapperLevel()
    {
        var rows = new ObservableCollection<Row> { new("alpha", 3), new("bravo", 1) };

        var headerGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,120") };
        foreach (var (text, column) in new[] { ("名称", 0), ("数量", 1) })
        {
            var caption = new TextBlock { Text = text };
            caption.Classes.Add("CkanCaption");
            Grid.SetColumn(caption, column);
            headerGrid.Children.Add(caption);
        }

        var header = new Border { Child = headerGrid };

        var list = new ListBox
        {
            ItemsSource = rows,
            ItemTemplate = new FuncDataTemplate<Row>((row, _) =>
            {
                if (row is null)
                {
                    return new Panel();
                }

                var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,120") };
                var name = new TextBlock { Text = row.Name };
                Grid.SetColumn(name, 0);
                var count = new TextBlock { Text = row.Count.ToString() };
                Grid.SetColumn(count, 1);
                grid.Children.Add(name);
                grid.Children.Add(count);
                return grid;
            }),
        };
        ListTableTools.SetColumns(list, "Name,Count");
        ListTableTools.SetEnabled(list, true);

        // 中间多一层 Grid：表头是列表的祖父级子节点。
        var wrapper = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        Grid.SetRow(list, 1);
        wrapper.Children.Add(list);

        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        Grid.SetRow(header, 0);
        Grid.SetRow(wrapper, 1);
        root.Children.Add(header);
        root.Children.Add(wrapper);

        HeadlessAppHost.CreateTestWindow(root);
        HeadlessAppHost.PumpJobs();

        var buttons = headerGrid.GetLogicalDescendants().OfType<Button>()
            .Where(b => b.Classes.Contains("table-filter")).ToArray();
        Assert.That(buttons, Has.Length.EqualTo(2), "跨一层包装也要挂上筛选按钮");

        ListTableTools.SortBy(list, 1, false);
        HeadlessAppHost.PumpJobs();
        Assert.That(Names(list), Is.EqualTo(new[] { "bravo", "alpha" }), "跨层挂上后排序同样生效");
    }

    /// <summary>
    /// 标题搬进装饰容器后必须回到第 0 列、文字原样保留，漏斗紧跟在文字右边。
    /// 回归点：曾经漏了 <c>Grid.SetColumn(caption, 0)</c>，标题带着旧列号跑出容器，表头只剩一排漏斗。
    /// </summary>
    [Test]
    public void CaptionsKeepTextAndSitImmediatelyLeftOfTheFunnel()
    {
        var rows = new ObservableCollection<Row> { new("alpha", 3), new("bravo", 1) };
        var (window, list, header) = CreateTable(rows);
        _ = list;

        var captions = header.GetLogicalDescendants().OfType<TextBlock>()
            .Where(t => t.Classes.Contains("CkanCaption")).ToList();
        Assert.That(captions.Select(c => c.Text), Is.EqualTo(new[] { "名称", "数量" }), "表头文字不能被装饰吃掉");

        foreach (var caption in captions)
        {
            Assert.That(caption.IsEffectivelyVisible, Is.True, "标题必须仍然可见");
            var holder = caption.Parent as Grid;
            Assert.That(holder, Is.Not.Null, "标题应被搬进装饰容器");
            Assert.That(Grid.GetColumn(caption), Is.EqualTo(0), "标题在装饰容器里必须回到第 0 列");

            var button = holder!.Children.OfType<Button>().Single(b => b.Classes.Contains("table-filter"));
            var captionRight = caption.Bounds.X + caption.Bounds.Width;
            Assert.That(button.Bounds.X, Is.GreaterThanOrEqualTo(captionRight),
                "漏斗要排在标题右边，不能盖住标题");
            Assert.That(button.Bounds.X - captionRight, Is.LessThan(40d),
                "漏斗要紧跟标题，不能被推到列右缘");
        }

        window.Close();
    }

    /// <summary>换了 ItemsSource 之后，原来的选中项要装回去，否则右侧详情面板会突然清空。</summary>
    [Test]
    public void SelectedItemSurvivesItemsSourceReplacement()
    {
        var rows = new ObservableCollection<Row> { new("alpha", 3), new("bravo", 1), new("charlie", 2) };
        var (_, list, _) = CreateTable(rows);
        list.SelectedItem = rows[1];
        HeadlessAppHost.PumpJobs();

        var replacement = new ObservableCollection<Row> { new("delta", 2), rows[1] };
        list.ItemsSource = replacement;
        HeadlessAppHost.PumpJobs();

        Assert.That(list.SelectedItem, Is.EqualTo(rows[1]), "同一个行对象换表后仍然选中");
    }
}

