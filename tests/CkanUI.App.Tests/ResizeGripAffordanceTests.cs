using System.Collections.ObjectModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using CkanUI.App.Controls;
using CkanUI.App.Tests.Automation;
using CkanUI.Ui.Tables;
using NUnit.Framework;

namespace CkanUI.App.Tests;

/// <summary>列宽拖拽的视觉指引：边界要看得见、摸得到、拖得动。</summary>
[TestFixture]
public sealed class ResizeGripAffordanceTests
{
    private sealed record Row(string Name, int Size);

    [SetUp]
    public void SetUp() => HeadlessAppHost.EnsureInitialized();

    /// <summary>取画刷里最不透明的那个颜色（纯色直接返回，渐变取不透明度最高的色标）。</summary>
    private static Color OpaqueColorOf(IBrush? brush) => brush switch
    {
        ISolidColorBrush solid => solid.Color,
        IGradientBrush gradient => gradient.GradientStops
            .OrderByDescending(stop => stop.Color.A)
            .Select(stop => stop.Color)
            .FirstOrDefault(),
        _ => Colors.Transparent,
    };

    private static (DataGridColumnHeader Header, Rectangle Separator) FindGrip(Window window)
    {
        foreach (var header in window.GetVisualDescendants().OfType<DataGridColumnHeader>())
        {
            var separator = header.GetVisualDescendants().OfType<Rectangle>().FirstOrDefault(r => r.Name == "VerticalSeparator");
            if (separator is not null)
            {
                return (header, separator);
            }
        }

        var names = string.Join(",", window.GetVisualDescendants().Select(v => v.GetType().Name + (v.Name is null ? string.Empty : "#" + v.Name)));
        Assert.Fail("找不到表头模板里的 VerticalSeparator；视觉树：" + names);
        throw new InvalidOperationException();
    }

    private static DataGrid CreateGrid(out Window window)
    {
        var grid = new DataGrid
        {
            AutoGenerateColumns = false,
            ItemsSource = new[] { new Row("a", 1), new Row("b", 2) },
            Columns =
            {
                new DataGridTextColumn { Header = "Name", Binding = new Avalonia.Data.Binding("Name"), Width = new DataGridLength(140) },
                new DataGridTextColumn { Header = "Size", Binding = new Avalonia.Data.Binding("Size"), Width = new DataGridLength(140) },
            },
        };
        TableTools.SetEnabled(grid, true);
        window = HeadlessAppHost.CreateTestWindow(grid, 700, 300);
        HeadlessAppHost.PumpJobs();
        return grid;
    }

    [Test]
    public void DataGridSeparator_IsWidenedAndShowsTheResizeCursor()
    {
        var grid = CreateGrid(out var window);
        var (_, separator) = FindGrip(window);

        Assert.That(separator.MinWidth, Is.GreaterThanOrEqualTo(6d), "命中区至少 6px，1px 太难点中");
        Assert.That(separator.Cursor, Is.Not.Null, "边界上要出现横向调整光标");
        Assert.That(separator.Cursor!.ToString(), Does.Contain("SizeWestEast"));
    }

    [Test]
    public void DataGridSeparator_LightsUpOnHover()
    {
        var grid = CreateGrid(out var window);
        var (header, separator) = FindGrip(window);

        Assert.That(separator.Fill, Is.Not.Null, "静止时也要有画刷（透明），否则命中区没有背景");
        Assert.That(OpaqueColorOf(separator.Fill).A, Is.EqualTo(0), "静止时不该画出边界线");

        var center = header.TranslatePoint(new Point(header.Bounds.Width / 2, header.Bounds.Height / 2), window);
        Assert.That(center, Is.Not.Null);
        window.MouseMove(center!.Value);
        HeadlessAppHost.PumpJobs();

        var accent = window.TryFindResource("CkanAccentBrush", window.ActualThemeVariant, out var value) && value is ISolidColorBrush brush
            ? brush.Color
            : (Color?)null;
        Assert.That(accent, Is.Not.Null, "主题里必须有 CkanAccentBrush");
        Assert.That(OpaqueColorOf(separator.Fill), Is.EqualTo(accent), "悬停表头时边界应点亮为强调色");

        // 命中区 6 px 宽，但视觉只能是一条细线：渐变两侧必须淡出。
        Assert.That(separator.Fill, Is.InstanceOf<IGradientBrush>(), "6 px 命中区里只留 1 px 视觉线");
        var stops = ((IGradientBrush)separator.Fill!).GradientStops;
        Assert.That(stops.First().Color.A, Is.EqualTo(0), "渐变左端淡出");
        Assert.That(stops.Last().Color.A, Is.EqualTo(0), "渐变右端淡出");

        window.MouseMove(new Point(window.Bounds.Width - 2, window.Bounds.Height - 2));
        HeadlessAppHost.PumpJobs();
        Assert.That(OpaqueColorOf(separator.Fill).A, Is.EqualTo(0), "指针离开后恢复隐形");
    }

    [Test]
    public void ListGrip_ShowsGuideLineOnHover()
    {
        var rows = new ObservableCollection<Row> { new("alpha", 3), new("bravo", 1) };
        var headerGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,120") };
        var nameCaption = new TextBlock { Text = "名称" };
        nameCaption.Classes.Add("CkanCaption");
        Grid.SetColumn(nameCaption, 0);
        var sizeCaption = new TextBlock { Text = "数量" };
        sizeCaption.Classes.Add("CkanCaption");
        Grid.SetColumn(sizeCaption, 1);
        headerGrid.Children.Add(nameCaption);
        headerGrid.Children.Add(sizeCaption);

        var headerHost = new Border { Child = headerGrid };
        var list = new ListBox
        {
            ItemsSource = rows,
            ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<Row>((row, _) => new TextBlock { Text = row?.Name ?? string.Empty }),
        };
        ListTableTools.SetColumns(list, "Name,Size");
        ListTableTools.SetEnabled(list, true);

        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        Grid.SetRow(headerHost, 0);
        Grid.SetRow(list, 1);
        root.Children.Add(headerHost);
        root.Children.Add(list);
        var window = HeadlessAppHost.CreateTestWindow(root, 700, 300);
        HeadlessAppHost.PumpJobs();

        var handle = headerGrid.Children.OfType<Border>().FirstOrDefault(b => b.Width == 6 && b.Child is Border);
        Assert.That(handle, Is.Not.Null, "每个可拖列都要有 6px 把手");
        var guide = (Border)handle!.Child!;
        Assert.That(guide.Width, Is.EqualTo(1d), "视觉是一条 1px 细线");
        Assert.That(guide.Opacity, Is.EqualTo(0d), "静止时隐形，不抢注意力");
        Assert.That(handle.Cursor, Is.Not.Null);

        var point = handle.TranslatePoint(new Point(3, handle.Bounds.Height / 2), window);
        Assert.That(point, Is.Not.Null);
        window.MouseMove(point!.Value);
        HeadlessAppHost.PumpJobs();
        Assert.That(guide.Opacity, Is.GreaterThan(0d), "指针移到把手上要亮起边界线");

        window.MouseMove(new Point(window.Bounds.Width - 2, window.Bounds.Height - 2));
        HeadlessAppHost.PumpJobs();
        Assert.That(guide.Opacity, Is.EqualTo(0d), "指针离开后恢复隐形");
    }
}
