using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using CkanUI.Services.Abstractions.Assets;
using CkanUI.Services.Abstractions.Dtos;
using NUnit.Framework;

namespace CkanUI.Plugin.Assets.Tests;

/// <summary>
/// 飞船页的载具库选择器在真实可视树上成立：下拉框真的绑到了 <c>Libraries</c>，
/// 换库会驱动页面改扫另一个 Ships 目录。
/// </summary>
/// <remarks>
/// VM 测试只能证明 <c>Libraries</c> 里有几项；绑定名写错（例如 SelectedItem 绑到
/// 不存在的属性）时 VM 全绿、界面上的下拉框却是空的。这里把视图装进无头窗口核对。
/// </remarks>
[TestFixture]
public sealed class ShipsPageLibrarySelectorTests
{
    [Test]
    public async Task LibrarySelector_ListsRootAndSaveFolders_AndSwitchingRescansThatFolder()
    {
        HeadlessHost.EnsureInitialized();
        if (!HeadlessHost.CanCreateWindows)
        {
            Assert.Ignore("无头壳由全量套件里另一个测试程序集在别的线程建好，本线程没有窗口亲和性；" +
                "单跑 CkanUI.Plugin.Assets.Tests 即可验证本条。");
        }

        var instances = new InstancesFakeService();
        var info = new InstanceInfo("测试实例", "D:/KSP", "1.12.5", true, false, 0);
        instances.Seed(info);
        instances.Current = info;

        var saves = new InstancesFakeSaveFiles();
        saves.Saves.Add(new SaveGameInfo(
            "M6_FlightUI / persistent",
            "D:/KSP/saves/M6_FlightUI/persistent.sfs",
            1024,
            new DateTimeOffset(2024, 5, 1, 12, 0, 0, TimeSpan.Zero),
            "测试存档",
            "CAREER",
            "1.12.5",
            "FLIGHT",
            "1234.5",
            12,
            "1000000",
            0,
            false,
            true));

        var crafts = new FakeCraftLibrary();
        crafts.Assets.Add(new CraftAsset(
            "Apollo",
            "D:/KSP/Ships/VAB/Apollo.craft",
            CraftKind.Vab,
            null,
            "1.12.5",
            42,
            3,
            2048,
            new DateTimeOffset(2024, 5, 1, 12, 0, 0, TimeSpan.Zero)));

        var viewModel = new ShipsPageViewModel(
            instances,
            crafts,
            saves,
            new InstancesFakeErrorReporter(),
            new InstancesFakeDialogs());
        await viewModel.RefreshCommand.ExecuteAsync(null);

        var window = new Window
        {
            Width = 1180,
            Height = 720,
            Content = new ShipsPageView { DataContext = viewModel },
        };
        window.Show();
        HeadlessHost.PumpJobs();
        window.Measure(new Size(1180, 720));
        window.Arrange(new Rect(0, 0, 1180, 720));
        HeadlessHost.PumpJobs();

        var selector = window.GetVisualDescendants().OfType<ComboBox>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(selector.ItemsSource!.Cast<CraftLibraryOption>().Select(option => option.DisplayName),
                Is.EqualTo(new[] { "根目录（所有存档共用）", "M6_FlightUI" }),
                "下拉框里要是根库 + 每个存档库各一项。");
            Assert.That(selector.SelectedItem, Is.SameAs(viewModel.Libraries[0]), "默认选中根库。");
        });

        var copyButton = window.GetVisualDescendants()
            .OfType<Button>()
            .Single(button => ReferenceEquals(button.Command, viewModel.CopyToLibraryCommand));
        var moveButton = window.GetVisualDescendants()
            .OfType<Button>()
            .Single(button => ReferenceEquals(button.Command, viewModel.MoveToLibraryCommand));
        Assert.That(copyButton.IsEnabled, Is.True, "刷新后默认选中第一艘，按钮该能点。");
        Assert.That(moveButton.IsEnabled, Is.True, "刷新后默认选中第一艘，按钮该能点。");

        viewModel.SelectedCraft = null;
        HeadlessHost.PumpJobs();
        Assert.That(copyButton.IsEnabled, Is.False, "取消选中后「复制到…」该是灰的。");
        Assert.That(moveButton.IsEnabled, Is.False, "取消选中后「移动到…」该是灰的。");

        viewModel.SelectedCraft = viewModel.Crafts[0];
        HeadlessHost.PumpJobs();
        Assert.That(copyButton.IsEnabled, Is.True, "重新选中后「复制到…」要能点。");

        selector.SelectedIndex = 1;
        HeadlessHost.PumpJobs();
        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.SelectedLibrary!.DisplayName, Is.EqualTo("M6_FlightUI"),
                "从界面换库要真的写回 VM，否则列表永远扫根目录。");
            Assert.That(
                crafts.LibraryScans,
                Has.Some.EqualTo(System.IO.Path.Combine("D:/KSP/saves", "M6_FlightUI", "Ships")),
                "换库后要扫那个存档的 Ships 目录。");
        });
    }
}
