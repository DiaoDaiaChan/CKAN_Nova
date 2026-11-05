using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using NUnit.Framework;

namespace CkanUI.App.Tests;

/// <summary>
/// 手画表头 + ListBox 的表格页覆盖面闸门：接了 ListTableTools 的页面，
/// 声明的列数必须和表头的列数对得上，否则筛选按钮会挂到错误的列上。
/// </summary>
[TestFixture]
public sealed class ListTableToolsCoverageTests
{
    /// <summary>
    /// 已接线的表格页：页面文件 + 文件里每张表的表头列数。新增表格页时往这里补一行；
    /// 一页有多张表（例如模组包页的预览表与差异表）就把列数都列出来。
    /// </summary>
    private static readonly (string File, int[] Columns)[] WiredTables =
    [
        ("src/CkanUI.App/Views/CachePageView.axaml", [6]),
        ("src/CkanUI.App/Views/ReposPageView.axaml", [5]),
        ("src/CkanUI.App/Views/PlayTimePageView.axaml", [4]),
        ("src/CkanUI.App/Views/DedupPageView.axaml", [5]),
        ("src/CkanUI.App/Views/ChangesPageView.axaml", [7]),
        ("src/CkanUI.App/Views/InstancesPageView.axaml", [5]),
        ("src/CkanUI.App/Views/ModPacksPageView.axaml", [6, 3]),
        ("src/CkanUI.Plugin.Assets/ShipsPageView.axaml", [8]),
        ("src/CkanUI.Plugin.Saves/SavesPageView.axaml", [5]),
        ("src/CkanUI.Plugin.Storage/UI/StoragePageView.axaml", [6]),
        // KspConfig 页已改成卡片式条目（列定义 *,240,96，不再有表头行），表格工具随之退出。
        ("src/CkanUI.Plugin.ModText/ModTextPageView.axaml", [4]),
    ];

    [Test]
    public void EveryWiredTable_DeclaresOneColumnPerHeaderColumn()
    {
        var root = ResolveRepositoryRoot();
        foreach (var (file, columns) in WiredTables)
        {
            var path = Path.Combine(root, file.Replace('/', Path.DirectorySeparatorChar));
            Assert.That(File.Exists(path), Is.True, $"找不到页面文件：{file}");
            var text = File.ReadAllText(path);

            Assert.That(text, Does.Contain("ListTableTools.Enabled=\"True\""),
                $"{file} 没有打开 ListTableTools，筛选/排序/拖动不会出现");

            var declared = Regex.Matches(text, @"ListTableTools\.Columns=""([^""]*)""")
                .Select(match => match.Groups[1].Value
                    .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                    .Length)
                .ToArray();
            Assert.That(declared, Is.Not.Empty, $"{file} 缺少 ListTableTools.Columns");
            Assert.That(declared, Is.EquivalentTo(columns),
                $"{file} 声明的列数（{string.Join('/', declared)}）和期望（{string.Join('/', columns)}）对不上");

            var headers = HeaderColumnCounts(XDocument.Parse(text));
            foreach (var count in declared)
            {
                Assert.That(headers, Does.Contain(count),
                    $"{file} 的 Columns 声明了 {count} 列，但文件里找不到 {count} 列的表头，筛选按钮会错位");
            }
        }
    }

    [Test]
    public void EveryWiredTable_UsesTheSharedToolNamespace()
    {
        var root = ResolveRepositoryRoot();
        foreach (var (file, _) in WiredTables)
        {
            var text = File.ReadAllText(Path.Combine(root, file.Replace('/', Path.DirectorySeparatorChar)));
            Assert.That(text, Does.Contain("using:CkanUI.Ui.Tables"),
                $"{file} 缺少 xmlns 声明：using:CkanUI.Ui.Tables");
        }
    }

    /// <summary>
    /// 表头 = 「自己拥有最多 CkanCaption 标题」的 Grid，返回它的列数。
    /// 标题归属按最近的带 <c>ColumnDefinitions</c> 的 Grid 祖先判定：Dedup 页最外层
    /// <c>*,6,380</c> 的 Grid 里嵌着表头和右侧详情面板，若按「后代里有多少标题」算，
    /// 外层会凭详情面板的标题抢走表头身份，把 5 列误判成 3 列。
    /// </summary>
    private static int[] HeaderColumnCounts(XDocument document)
    {
        var owners = new Dictionary<XElement, int>();
        foreach (var caption in document.Descendants().Where(IsCaption))
        {
            var owner = caption.Ancestors().FirstOrDefault(IsColumnGrid);
            if (owner is not null)
            {
                owners[owner] = owners.GetValueOrDefault(owner) + 1;
            }
        }

        return owners
            // 表头一列最多一个标题：标题比列还多的 Grid 是右侧详情面板那种
            // 「两列里竖排一堆标签」的布局，不是表头，先排除。
            .Where(owner => owner.Value >= 2 && owner.Value <= ColumnCount(owner.Key))
            .Select(owner => ColumnCount(owner.Key))
            .Distinct()
            .ToArray();
    }

    private static bool IsCaption(XElement element) =>
        element.Name.LocalName == "TextBlock" &&
        element.Attribute("Classes")?.Value.Contains("CkanCaption", StringComparison.Ordinal) == true;

    private static bool IsColumnGrid(XElement element) =>
        element.Name.LocalName == "Grid" && ColumnCount(element) > 0;

    private static int ColumnCount(XElement grid) =>
        grid.Attribute("ColumnDefinitions")?.Value
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Length ?? 0;

    /// <summary>仓库根以 <c>src/CkanUI.App/CkanUI.App.csproj</c> 为锚点（sln 文件名是小写的 ckan-ui.sln）。</summary>
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

        throw new DirectoryNotFoundException(
            string.Create(CultureInfo.InvariantCulture, $"未能从 {AppContext.BaseDirectory} 向上找到仓库根"));
    }
}
