using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using CkanUI.ViewModels;
using CkanUI.ViewModels.Shell;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace CkanUI.App.Tests;

/// <summary>
/// 分割条可抓握契约（m02308）：左右卡片是同一 Grid 里的兄弟节点，右侧卡片的
/// BoxShadow 会画到 6px 缝隙上、盖住分割条；共享样式里的 `Margin="2,10"` 又把 6px 轨道压成 2px。
/// 结果就是「看得见一条发丝、抓不住、没有手型」。这里锁住三件事：
/// 1) 轨道可见宽度 == 列宽（水平 Margin 为 0，不再被压窄）；
/// 2) 显式声明 SizeWestEast 光标，鼠标移上去就知道能拖；
/// 3) ZIndex 抬到 0 以上，让轨道画在卡片阴影之后（之上）。
/// 运行时断言只覆盖有数据的页面；源文件断言覆盖全部声明，避免新页面又退回发丝。
/// </summary>
[TestFixture]
public sealed class SplitterAffordanceTests
{
    /// <summary>
    /// 需要运行期核查的分割条页面：导航 key → 人类可读名。
    /// </summary>
    /// <remarks>
    /// 只列宿主自己的页面：载具 / 旗帜 / 载入图 / 存档 已经搬进可停用的插件，外壳里没有插件时
    /// 根本导航不过去。它们的声明由 <see cref="EverySplitterDeclaration_DeclaresTheHandleContract"/>
    /// 覆盖（扫描范围含 <c>src/CkanUI.Plugin.*</c>），这里不再假装检查过。
    /// </remarks>
    private static readonly (string Key, string Label)[] Pages =
    [
        ("content", "变更集"),
        ("dedup", "去重"),
        ("instances", "实例"),
        ("mods", "模组库"),
    ];

    private ServiceProvider _services = null!;
    private ShellViewModel _shell = null!;
    private MainWindow _window = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        Automation.HeadlessAppHost.EnsureInitialized();
        _services = ServiceRegistration.Build(() => _window);
        _shell = _services.GetRequiredService<ShellViewModel>();
        _window = new MainWindow { DataContext = _shell, Width = 1280, Height = 800 };
        _window.Show();
        Automation.HeadlessAppHost.PumpJobs();
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _window?.Close();
        Automation.HeadlessAppHost.PumpJobs();
        _services?.Dispose();
    }

    [Test]
    public void EverySplitterPage_ExposesGrabbableHandle()
    {
        foreach (var (key, label) in Pages)
        {
            var item = _shell.NavItems.FirstOrDefault(i => i.Key == key);
            Assert.That(item, Is.Not.Null, $"宿主页面 {key} 必须在导航项里，运行期核查不该被悄悄跳过。");

            if (item is null)
            {
                continue;
            }

            _shell.NavigateCommand.Execute(item.Key);
            Automation.HeadlessAppHost.PumpJobs();

            var splitters = _window.GetVisualDescendants().OfType<GridSplitter>().ToList();
            Assert.That(splitters, Is.Not.Empty, $"{label}页应当有一个可拖拽分割条。");

            // 空态（没有任何游戏实例/数据）时整行被 CkanEmptyStateCard 覆盖，没有左右两栏可分配，
            // 把手必须一起收起来——否则会在空态卡上划一道发丝。收起是有前提的：必须有可见的空态卡。
            if (splitters.All(s => !s.IsEffectivelyVisible))
            {
                var hasEmptyState = _window.GetVisualDescendants().OfType<Border>()
                    .Any(b => b.Classes.Contains("CkanEmptyStateCard") && b.IsEffectivelyVisible);
                Assert.That(hasEmptyState, Is.True, $"{label}页分割条被隐藏，但页面上没有可见的空态卡——隐藏没有理由。");
            }
            foreach (var splitter in splitters)
            {
                Assert.Multiple(() =>
                {
                    Assert.That(splitter.Margin.Left, Is.Zero, $"{label}页分割条左 Margin 必须为 0，否则 6px 轨道会被压成发丝。");
                    Assert.That(splitter.Margin.Right, Is.Zero, $"{label}页分割条右 Margin 必须为 0，否则 6px 轨道会被压成发丝。");
                    Assert.That(splitter.ZIndex, Is.GreaterThan(0), $"{label}页分割条必须抬到卡片之后绘制，否则右侧卡片阴影会盖住它。");
                    Assert.That(splitter.Cursor, Is.Not.Null, $"{label}页分割条必须声明拖拽光标，否则用户不知道能拖。");
                });
            }
        }
    }

    [Test]
    public void EverySplitterDeclaration_DeclaresTheHandleContract()
    {
        // 页面搬进插件之后，视图也搬走了：只扫宿主 Views 会漏掉插件自带的声明
        // （这条用例曾经因为扫描范围没跟上，阈值 10 掉到 4 而假红）。
        // 契约对「任何视图」都成立，所以扫整个 src 下的 axaml，插件和共享 UI 层一起算。
        var viewsRoot = Path.Combine(ResolveRepositoryRoot(), "src");
        var offenders = new List<string>();
        var scanned = 0;

        foreach (var file in Directory.EnumerateFiles(viewsRoot, "*.axaml", SearchOption.AllDirectories))
        {
            // 构建输出里也有 axaml 副本，扫进来会把同一份声明数两遍。
            if (file.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                || file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                continue;
            }

            var text = File.ReadAllText(file);
            foreach (Match match in Regex.Matches(text, "<GridSplitter\\b[^>]*>", RegexOptions.Singleline))
            {
                // 对话框里 1px 宽的分隔线不是拖拽把手：拖不动，也不该有光标。
                if (Regex.IsMatch(match.Value, "Width=\"1(\\.0+)?\""))
                {
                    continue;
                }

                scanned++;
                var relative = Path.GetRelativePath(viewsRoot, file).Replace('\\', '/');
                if (!match.Value.Contains("Cursor=\"SizeWestEast\""))
                {
                    offenders.Add($"{relative}: 缺 Cursor=\"SizeWestEast\"");
                }

                if (!match.Value.Contains("ZIndex=\"5\""))
                {
                    offenders.Add($"{relative}: 缺 ZIndex=\"5\"（会被卡片阴影盖住）");
                }

                var margin = Regex.Match(match.Value, "Margin=\"([^\"]*)\"");
                if (margin.Success)
                {
                    var horizontal = margin.Groups[1].Value.Split(',')[0].Trim();
                    if (horizontal != "0" && horizontal != "0.0")
                    {
                        offenders.Add($"{relative}: 水平 Margin 是 {horizontal}，不是 0，6px 轨道会被压成发丝");
                    }
                }
            }
        }

        Assert.That(scanned, Is.GreaterThanOrEqualTo(10), "应当扫到全部拖拽分割条声明。");
        Assert.That(offenders, Is.Empty, "分割条把手契约不完整：" + string.Join("；", offenders));
    }

    /// <summary>从测试输出目录向上找到仓库根（以 <c>src/CkanUI.App/CkanUI.App.csproj</c> 为锚点，与 sln 文件名无关）。</summary>
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
