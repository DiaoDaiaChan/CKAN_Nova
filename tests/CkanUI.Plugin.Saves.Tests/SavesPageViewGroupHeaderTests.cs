using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using NUnit.Framework;

namespace CkanUI.Plugin.Saves.Tests;

/// <summary>
/// 存档列表的「按文件夹分组」在真实可视树上成立：组头只在每组首行上方出现一次，
/// 其余行不画组头，首列显示尾段而不是完整槽位名。
/// </summary>
/// <remarks>
/// VM 层测试能证明 IsGroupStart 打对了，但证明不了行模板真的把组头画出来——
/// 绑定名写错（例如 IsVisible 绑到 IsGrouped）时 VM 测试全绿、界面却每行都挂一条组头。
/// 这里把视图真的装进无头窗口，按可视树核对渲染结果。
/// </remarks>
[TestFixture]
public sealed class SavesPageViewGroupHeaderTests
{
    /// <summary>找到某一行（ListBoxItem）里可见的组头 Border。</summary>
    private static List<Border> GroupHeadersOf(Control row)
        => row.GetVisualDescendants()
            .OfType<Border>()
            .Where(border => border.IsVisible && border.Classes.Contains("SavesFolderHeader"))
            .ToList();

    [Test]
    public async Task GroupHeader_RendersOncePerFolder_AndRowsShowLeafName()
    {
        HeadlessHost.EnsureInitialized();
        if (!HeadlessHost.CanCreateWindows)
        {
            Assert.Ignore("无头壳由全量套件里另一个测试程序集在别的线程建好，本线程没有窗口亲和性；" +
                "单跑 CkanUI.Plugin.Saves.Tests 即可验证本条。");
        }

        var instances = new InstancesFakeService { Current = SavesFixtures.Instance() };
        var saves = new SavesFakeSaveFileService
        {
            ListResult = OperationResult<IReadOnlyList<SaveGameInfo>>.Ok(new[]
            {
                SavesFixtures.Save(slot: "M6_FlightUI / persistent"),
                SavesFixtures.Save(slot: "M6_FlightUI / mfp-m6-checkpoint"),
                SavesFixtures.Save(slot: "M6_FlightUI / Backup / persistent (2026_10_31_16_06_27)"),
                SavesFixtures.Save(slot: "默认 / persistent"),
                SavesFixtures.Save(slot: "persistent"),
            }),
        };
        var viewModel = new SavesPageViewModel(instances, saves, new InstancesFakeErrorReporter(), new InstancesFakeDialogs());
        await viewModel.RefreshCommand.ExecuteAsync(null);

        var window = new Window
        {
            Width = 1100,
            Height = 700,
            Content = new SavesPageView { DataContext = viewModel },
        };
        window.Show();
        HeadlessHost.PumpJobs();
        window.Measure(new Size(1100, 700));
        window.Arrange(new Rect(0, 0, 1100, 700));
        HeadlessHost.PumpJobs();
        // 虚拟化面板按需实例化行：让布局真的跑一轮，容器才会被造出来。
        window.UpdateLayout();
        HeadlessHost.PumpJobs();

        try
        {
            var allListBoxes = window.GetVisualDescendants().OfType<ListBox>().ToList();
            Console.WriteLine($"[DEBUG] Found {allListBoxes.Count} ListBoxes: " +
                string.Join(", ", allListBoxes.Select(b => $"Name='{b.Name}', ItemsSourceType='{b.ItemsSource?.GetType().Name}'")));
            var list = allListBoxes.First();
            var rows = list.GetVisualDescendants().OfType<ListBoxItem>().ToList();
            Console.WriteLine($"[probe] ItemCount={list.ItemCount} rows={rows.Count} vis={list.IsVisible} bounds={list.Bounds} hasRows={viewModel.HasRows}");
            Assert.That(rows, Has.Count.EqualTo(viewModel.Rows.Count), "可视树里的行数应与 Rows 一致");

            foreach (var (row, index) in rows.Select((row, index) => (row, index)))
            {
                var headers = GroupHeadersOf(row);
                var expected = viewModel.Rows[index].IsGroupStart ? 1 : 0;
                Assert.That(headers, Has.Count.EqualTo(expected),
                    $"第 {index} 行（{viewModel.Rows[index].SlotName}）组头数量不对：IsGroupStart={viewModel.Rows[index].IsGroupStart}");
            }

            // 组头文案真的落在可视树上：文件夹名与「共 N 个存档」。
            var headerTexts = rows
                .SelectMany(GroupHeadersOf)
                .SelectMany(border => border.GetVisualDescendants().OfType<TextBlock>())
                .Select(block => block.Text ?? string.Empty)
                .ToList();
            Assert.That(headerTexts, Does.Contain("saves/"));
            Assert.That(headerTexts, Does.Contain("M6_FlightUI"));
            Assert.That(headerTexts, Does.Contain("M6_FlightUI / Backup"));
            Assert.That(headerTexts, Does.Contain("默认"));
            Assert.That(headerTexts, Does.Contain("共 1 个存档"));
            Assert.That(headerTexts, Does.Contain("共 2 个存档"), "M6_FlightUI 有三行（含 Backup 子目录）但只有两行属于该文件夹本身");
        }
        finally
        {
            window.Close();
        }
    }
}
