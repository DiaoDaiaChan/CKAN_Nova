using System;
using System.Threading.Tasks;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.ViewModels.Pages;
using CkanUI.ViewModels.Pages.Mods;
using NUnit.Framework;

namespace CkanUI.ViewModels.Tests;

[TestFixture]
public sealed class ModsContextMenuTests
{
    private static ModRowData CreateRow(
        string identifier,
        string name,
        bool isInstalled,
        string? homepage = null,
        string? downloadUrl = null)
    {
        return new ModRowData(
            Identifier: identifier,
            Name: name,
            Author: "Author",
            InstalledVersion: isInstalled ? "1.0.0" : null,
            LatestVersion: "1.0.0",
            DownloadCount: 100,
            Tags: Array.Empty<string>(),
            SizeBytes: 1024,
            IsInstalled: isInstalled,
            HasUpdate: false,
            IsCompatible: true,
            IsAutoInstalled: false,
            IsSelected: false,
            IsImplicit: false)
        {
            Facts = new ModSearchFacts
            {
                Homepage = homepage,
                DownloadUrl = downloadUrl,
            }
        };
    }

    [Test]
    public void CanReinstallSelected_ReflectsWhetherSelectedRowIsInstalled()
    {
        var vm = new ModsPageViewModel();
        Assert.That(vm.CanReinstallSelected, Is.False);

        var notInstalled = ModsRowView.From(CreateRow("ModA", "Mod A", false));
        vm.SelectedRow = notInstalled;
        Assert.That(vm.CanReinstallSelected, Is.False);

        var installed = ModsRowView.From(CreateRow("ModB", "Mod B", true));
        vm.SelectedRow = installed;
        Assert.That(vm.CanReinstallSelected, Is.True);
    }

    [Test]
    public void CanOpenHomepage_ReflectsWhetherSelectedRowHasHomepage()
    {
        var vm = new ModsPageViewModel();
        Assert.That(vm.CanOpenHomepage, Is.False);

        var withoutHome = ModsRowView.From(CreateRow("ModA", "Mod A", true, homepage: null));
        vm.SelectedRow = withoutHome;
        Assert.That(vm.CanOpenHomepage, Is.False);

        var withHome = ModsRowView.From(CreateRow("ModB", "Mod B", true, homepage: "https://example.com/modb"));
        vm.SelectedRow = withHome;
        Assert.That(vm.CanOpenHomepage, Is.True);
    }

    [Test]
    public void ReinstallSelected_WhenNotInstalled_ShowsStatusMessage()
    {
        var vm = new ModsPageViewModel();
        vm.SelectedRow = ModsRowView.From(CreateRow("ModA", "Mod A", false));

        vm.ReinstallSelected();

        Assert.That(vm.StatusText, Does.Contain("请先选择一个已安装的模组"));
        Assert.That(vm.SelectedCount, Is.EqualTo(0));
    }

    [Test]
    public void ReinstallSelected_WhenInstalled_AddsToPendingChanges()
    {
        var vm = new ModsPageViewModel();
        vm.SelectedRow = ModsRowView.From(CreateRow("ModA", "Mod A", true));

        vm.ReinstallSelected();

        Assert.That(vm.SelectedCount, Is.EqualTo(1));
        Assert.That(vm.StatusText, Does.Contain("已将模组 Mod A 加入变更列表（准备重新安装）").Or.Contain("已将模组 Mod A 加入变更列表（准备重新安装）。"));
    }

    [Test]
    public async Task CopySelectedIdentifier_WithoutClipboard_SetsFallbackStatus()
    {
        var vm = new ModsPageViewModel();
        vm.SelectedRow = ModsRowView.From(CreateRow("ModA", "Mod A", true));

        await vm.CopySelectedIdentifierCommand.ExecuteAsync(null);

        Assert.That(vm.StatusText, Does.Contain("标识符：ModA"));
        Assert.That(vm.StatusText, Does.Contain("未接入剪贴板"));
    }

    [Test]
    public async Task CopySelectedIdentifier_WithClipboard_WritesIdentifier()
    {
        var vm = new ModsPageViewModel();
        string? written = null;
        vm.ClipboardWriter = text =>
        {
            written = text;
            return Task.CompletedTask;
        };
        vm.SelectedRow = ModsRowView.From(CreateRow("ModA", "Mod A", true));

        await vm.CopySelectedIdentifierCommand.ExecuteAsync(null);

        Assert.That(written, Is.EqualTo("ModA"));
        Assert.That(vm.StatusText, Does.Contain("已复制标识符“ModA”到剪贴板"));
    }

    [Test]
    public async Task CopySelectedDownloadUrl_NoUrl_ShowsNoDownloadLinkStatus()
    {
        var vm = new ModsPageViewModel();
        vm.SelectedRow = ModsRowView.From(CreateRow("ModA", "Mod A", true, downloadUrl: null));

        await vm.CopySelectedDownloadUrlCommand.ExecuteAsync(null);

        Assert.That(vm.StatusText, Does.Contain("该模组没有记录下载链接"));
    }

    [Test]
    public async Task CopySelectedDownloadUrl_WithClipboard_WritesUrl()
    {
        var vm = new ModsPageViewModel();
        string? written = null;
        vm.ClipboardWriter = text =>
        {
            written = text;
            return Task.CompletedTask;
        };
        vm.SelectedRow = ModsRowView.From(CreateRow("ModA", "Mod A", true, downloadUrl: "https://example.com/mod.zip"));

        await vm.CopySelectedDownloadUrlCommand.ExecuteAsync(null);

        Assert.That(written, Is.EqualTo("https://example.com/mod.zip"));
        Assert.That(vm.StatusText, Does.Contain("已复制模组下载地址到剪贴板"));
    }

    [Test]
    public async Task OpenSelectedHomepage_NoHomepage_ShowsNoHomepageStatus()
    {
        var vm = new ModsPageViewModel();
        vm.SelectedRow = ModsRowView.From(CreateRow("ModA", "Mod A", true, homepage: ""));

        await vm.OpenSelectedHomepageCommand.ExecuteAsync(null);

        Assert.That(vm.StatusText, Does.Contain("该模组没有配置主页链接"));
    }

    [Test]
    public async Task OpenSelectedHomepage_WithLauncher_InvokesLauncher()
    {
        var vm = new ModsPageViewModel();
        string? launched = null;
        vm.UriLauncher = url =>
        {
            launched = url;
            return Task.CompletedTask;
        };
        vm.SelectedRow = ModsRowView.From(CreateRow("ModA", "Mod A", true, homepage: "https://github.com/KSP-CKAN/CKAN"));

        await vm.OpenSelectedHomepageCommand.ExecuteAsync(null);

        Assert.That(launched, Is.EqualTo("https://github.com/KSP-CKAN/CKAN"));
        Assert.That(vm.StatusText, Does.Contain("正在打开主页"));
    }
}
