using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Data;
using Avalonia.VisualTree;

using CkanUI.App.Controls;

using NUnit.Framework;

namespace CkanUI.App.Tests;

/// <summary>
/// 表格工具契约（m03735「所有的表单控件，添加筛选，拖动，排序」）：DataGrid 自带的排序/重排/列宽
/// 之前被逐个属性关掉，每张表的行为都不一样。这里锁住同一份契约：
/// 1) <c>TableTools.Enabled</c> 打开后排序、列拖动、列宽三个开关都被打开；
/// 2) 列头有筛选入口，筛选真的改变可见行，多列条件按「与」叠加；
/// 3) <c>ItemsSource</c> 换集合（模组页每次刷新都换一个 ObservableCollection）后筛选仍然生效；
/// 4) 显示文本经过格式化的列按原始值排序（"1.9" 不能排到 "1.10" 后面，2.3 MB 不能排到 900 KB 前面）。
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class TableToolsTests
{
    /// <summary>排序用的原始值列：界面上是 <c>DownloadText</c>（"1.2k"），排序必须看 <c>DownloadCount</c>。</summary>
    private sealed record Row(string Name, string Version, int DownloadCount, long SizeBytes)
    {
        public string DownloadText => DownloadCount > 0 ? DownloadCount.ToString("N0") : "—";

        public string SizeText => SizeBytes.ToString("N0");
    }

    [SetUp]
    public void SetUp() => CkanUI.App.Tests.Automation.HeadlessAppHost.EnsureInitialized();

    [Test]
    public void Enabled_TurnsOnSortingReorderingAndResizing()
    {
        var grid = CreateGrid();
        TableTools.SetEnabled(grid, true);

        Assert.That(grid.CanUserSortColumns, Is.True, "排序要能点");
        Assert.That(grid.CanUserReorderColumns, Is.True, "列要能拖动重排");
        Assert.That(grid.CanUserResizeColumns, Is.True, "列宽要能拖");
    }

    [Test]
    public void EveryFilterableColumn_GetsAFilterButton_AndNarrowColumnsCanOptOut()
    {
        var grid = CreateGrid();
        TableTools.SetEnabled(grid, true);

        Assert.That(grid.Columns.All(column => TableTools.HasFilterEntry(grid, column)), Is.True, "三列都该有筛选入口");

        // 行首复选框那种 38px 窄列：没有可筛文本，显式关掉后不该再塞按钮。
        var narrow = CreateTextColumn("Check", "Name", "Name");
        TableTools.SetFilterable(narrow, false);
        grid.Columns.Insert(0, narrow);

        Assert.That(TableTools.HasFilterEntry(grid, narrow), Is.False, "关掉筛选的列不该挂入口");
        Assert.That(TableTools.HasFilterEntry(grid, grid.Columns[1]), Is.True, "其余列不受影响");

        // 表头真的渲染出漏斗按钮（虚拟化下只保证已实例化的表头）。
        var realized = Headers(grid).Sum(FilterButtons);
        Assert.That(realized, Is.GreaterThanOrEqualTo(3), "表头里应能看到筛选按钮");
    }

    private static IReadOnlyList<DataGridColumnHeader> Headers(DataGrid grid)
    {
        CkanUI.App.Tests.Automation.HeadlessAppHost.PumpJobs();
        return grid.GetVisualDescendants().OfType<DataGridColumnHeader>().ToList();
    }

    private static int FilterButtons(DataGridColumnHeader header) =>
        header.GetVisualDescendants().OfType<Button>().Count(button => button.Classes.Contains("table-filter"));

    [Test]
    public void Filter_NarrowsRowsAndMultipleColumnsCombineWithAnd()
    {
        var grid = CreateGrid();
        TableTools.SetEnabled(grid, true);

        var visible = Visible(grid);
        Assert.That(visible, Is.EqualTo(4), "起始四行");

        TableTools.SetFilter(grid, grid.Columns[0], "alp");
        Assert.That(Visible(grid), Is.EqualTo(1), "按名称筛选");

        TableTools.SetFilter(grid, grid.Columns[0], "r");
        Assert.That(Visible(grid), Is.EqualTo(2), "bravo 与 charlie 含 r");

        TableTools.SetFilter(grid, grid.Columns[1], "1.10");
        Assert.That(Visible(grid), Is.EqualTo(1), "两列条件要按与叠加");
        Assert.That(Order(grid), Is.EqualTo(new[] { "charlie" }));

        TableTools.ClearFilters(grid);
        Assert.That(Visible(grid), Is.EqualTo(4), "清筛选后恢复");
    }

    [Test]
    public void Filter_SurvivesItemsSourceReplacement()
    {
        var grid = CreateGrid();
        TableTools.SetEnabled(grid, true);
        TableTools.SetFilter(grid, grid.Columns[0], "bravo");
        Assert.That(Visible(grid), Is.EqualTo(1));

        grid.ItemsSource = new ObservableCollection<Row>(Rows());

        Assert.That(Visible(grid), Is.EqualTo(1), "换集合后 DataGrid 会重建 CollectionView，筛选必须重挂");
    }

    [Test]
    public void NumericAndVersionColumns_SortByValueNotByText()
    {
        var grid = CreateGrid();
        TableTools.SetEnabled(grid, true);

        // 下载列界面文本是 "9"/"1,000"；按文本排序会把 9 排在 1,000 之后。
        SortBy(grid, column: 2, ListSortDirection.Descending);
        Assert.That(Order(grid), Is.EqualTo(new[] { "delta", "charlie", "bravo", "alpha" }), "下载量要按数值降序");

        // 版本列按字符串比较会把 1.10 排到 1.9 前面，数值化后应是 2.0 > 1.10 > 1.9 > 1.2。
        SortBy(grid, column: 1, ListSortDirection.Descending);
        Assert.That(Order(grid), Is.EqualTo(new[] { "delta", "charlie", "bravo", "alpha" }), "1.10 要排在 1.9 之上");
    }

    /// <summary>DataGrid 的排序效果由布局/调度器落到 CollectionView，排序后必须空转一次再断言。</summary>
    private static void SortBy(DataGrid grid, int column, ListSortDirection direction)
    {
        grid.Columns[column].Sort(direction);
        CkanUI.App.Tests.Automation.HeadlessAppHost.PumpJobs();
    }

    private static IEnumerable<Row> Rows() =>
    [
        new("alpha", "1.2", 9, 900),
        new("bravo", "1.9", 1_000, 2_300_000),
        new("charlie", "1.10", 3_000, 12_000_000),
        new("delta", "2.0", 12_000, 150),
    ];

    private static DataGrid CreateGrid()
    {
        var grid = new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            ItemsSource = new ObservableCollection<Row>(Rows()),
        };

        grid.Columns.Add(CreateTextColumn("Name", "Name", "Name"));
        grid.Columns.Add(CreateTextColumn("Version", "Version", "Version"));
        grid.Columns.Add(CreateTextColumn("Downloads", "DownloadText", "DownloadCount"));

        var window = CkanUI.App.Tests.Automation.HeadlessAppHost.CreateTestWindow(grid);
        window.Width = 900;
        window.Height = 400;
        CkanUI.App.Tests.Automation.HeadlessAppHost.PumpJobs();
        return grid;
    }

    private static DataGridTextColumn CreateTextColumn(string header, string bindingPath, string sortPath)
    {
        var column = new DataGridTextColumn
        {
            Header = header,
            Binding = new Binding(bindingPath),
        };

        TableTools.SetSortPath(column, sortPath);
        return column;
    }

    private static int Visible(DataGrid grid) =>
        grid.CollectionView is null ? 0 : grid.CollectionView.Cast<object>().Count();

    private static string[] Order(DataGrid grid) =>
        grid.CollectionView is null ? [] : grid.CollectionView.Cast<Row>().Select(row => row.Name).ToArray();

    [Test]
    public void Sorting_StartsRowMotionAndSettles()
    {
        var resources = Application.Current!.Resources;
        resources["CkanMotionDurationBase"] = TimeSpan.FromMilliseconds(160);
        try
        {
            var grid = CreateGrid();
            TableTools.SetEnabled(grid, true);

            // 升序后 alpha 从第一行跑到最后一行：它得先站回旧位置再滑过去。
            grid.Columns[0].Sort(ListSortDirection.Descending);
            CkanUI.App.Tests.Automation.HeadlessAppHost.PumpJobs();

            Assert.Multiple(() =>
            {
                Assert.That(Order(grid), Is.EqualTo(new[] { "delta", "charlie", "bravo", "alpha" }), "排序本身要生效");
                Assert.That(
                    Rows(grid).Any(row => row.RenderTransform is TranslateTransform or TransformGroup),
                    Is.True,
                    "排序之后行要挂着位移轨道滑回新位置");
            });
        }
        finally
        {
            resources.Remove("CkanMotionDurationBase");
        }
    }

    private static IReadOnlyList<DataGridRow> Rows(DataGrid grid) =>
        grid.GetVisualDescendants().OfType<DataGridRow>().ToList();

    [Test]
    public void BothTables_DeclareTheSameTools()
    {
        var root = ResolveRepositoryRoot();
        foreach (var relative in new[]
                 {
                     Path.Combine("src", "CkanUI.App", "Views", "ModsPageView.axaml"),
                     Path.Combine("src", "CkanUI.App", "Views", "DownloadStatsPageView.axaml"),
                 })
        {
            var text = File.ReadAllText(Path.Combine(root, relative));
            Assert.That(text, Does.Contain("TableTools.Enabled=\"True\""), relative + " 要开表格工具");
            Assert.That(text, Does.Not.Contain("CanUserSortColumns=\"False\""), relative + " 不能关排序");
            Assert.That(text, Does.Not.Contain("CanUserReorderColumns=\"False\""), relative + " 不能关列拖动");
        }

        // 版本列界面上是字符串（"1.9" / "1.10"），必须指回原始版本字段，否则按文本排序。
        var mods = File.ReadAllText(Path.Combine(root, "src", "CkanUI.App", "Views", "ModsPageView.axaml"));
        foreach (var path in new[] { "Data.InstalledVersion", "Data.LatestVersion", "Data.DownloadCount", "Data.SizeBytes" })
        {
            Assert.That(mods, Does.Contain("TableTools.SortPath=\"" + path + "\""), "模组表的格式化列要按原始值排序：" + path);
        }
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

        throw new DirectoryNotFoundException("未能从测试输出目录向上找到含 src/CkanUI.App/CkanUI.App.csproj 的仓库根。");
    }
}
