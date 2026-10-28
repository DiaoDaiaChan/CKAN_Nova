using System;
using System.Threading.Tasks;
using CkanUI.ViewModels.Dialogs;
using NUnit.Framework;

namespace CkanUI.ViewModels.Tests;

/// <summary>关于框（12 篇 F-55）：版本矩阵、链接与失败提示。</summary>
[TestFixture]
public sealed class AboutDialogTests
{
    private static AboutInfo Info() => new("CKAN UI", "v0.1.0", "v1.36.0", ".NET 10.0", "Windows 11");

    [Test]
    public void Constructor_ExposesFourVersionFields()
    {
        var vm = new AboutDialogViewModel(Info());

        Assert.That(vm.Fields, Has.Count.EqualTo(4));
        Assert.That(vm.Fields[0].Label, Is.EqualTo("界面版本"));
        Assert.That(vm.Fields[0].Value, Is.EqualTo("v0.1.0"));
        Assert.That(vm.Fields[1].Value, Is.EqualTo("v1.36.0"));
        Assert.That(vm.Fields[2].Value, Is.EqualTo(".NET 10.0"));
        Assert.That(vm.Fields[3].Value, Is.EqualTo("Windows 11"));
    }

    [Test]
    public void Title_ContainsProductName_AndOnlyOneButton()
    {
        var vm = new AboutDialogViewModel(Info());

        Assert.That(vm.Title, Is.EqualTo("关于 CKAN UI"));
        Assert.That(vm.PrimaryButtonText, Is.EqualTo("关闭"));
        Assert.That(vm.HasSecondaryButton, Is.False);
    }

    [Test]
    public void Links_AreTheThreeOfficialOnes()
    {
        var vm = new AboutDialogViewModel(Info());

        Assert.That(vm.Links, Has.Count.EqualTo(3));
        Assert.That(vm.Links[0].Url, Does.Contain("github.com/KSP-CKAN/CKAN"));
    }

    /// <summary>
    /// 许可文案必须是两段：界面自身 GPL-3.0、内置核心库 MIT。
    /// 回归点：曾经的文案写成「CKAN 与 CKAN UI 均以 GPL-3.0 发布」，与上游 MIT 事实冲突。
    /// </summary>
    [Test]
    public void LicenseText_SeparatesUiAndUpstreamLicenses()
    {
        var vm = new AboutDialogViewModel(Info());

        Assert.That(vm.LicenseText, Does.Contain("CKAN UI 以 GPL-3.0"));
        Assert.That(vm.LicenseText, Does.Contain("MIT"));
        Assert.That(vm.LicenseText, Does.Not.Contain("CKAN 与 CKAN UI 均以"));
    }

    [Test]
    public void Primary_ClosesTheDialog()
    {
        var vm = new AboutDialogViewModel(Info());

        vm.PrimaryCommand.Execute(null);

        Assert.That(vm.IsClosed, Is.True);
        Assert.That(vm.IsConfirmed, Is.True);
    }

    [Test]
    public void OpenLink_WithoutChannel_ShowsValidationMessage()
    {
        var vm = new AboutDialogViewModel(Info());

        vm.OpenLinkCommand.Execute(vm.Links[0]);

        Assert.That(vm.HasValidationError, Is.True);
        Assert.That(vm.ValidationError, Does.Contain("浏览器"));
        Assert.That(vm.IsClosed, Is.False);
    }

    [Test]
    public void OpenLink_DelegatesUrlToTheInjectedChannel()
    {
        string? opened = null;
        var vm = new AboutDialogViewModel(Info(), url =>
        {
            opened = url;
            return Task.FromResult(true);
        });

        vm.OpenLinkCommand.Execute(vm.Links[1]);

        Assert.That(opened, Is.EqualTo(vm.Links[1].Url));
        Assert.That(vm.HasValidationError, Is.False);
    }

    [Test]
    public void OpenLink_WhenChannelFails_ShowsTheUrl()
    {
        var vm = new AboutDialogViewModel(Info(), _ => Task.FromResult(false));

        vm.OpenLinkCommand.Execute(vm.Links[2]);

        Assert.That(vm.ValidationError, Does.Contain(vm.Links[2].Url));
    }

    [Test]
    public void OpenLink_WhenChannelThrows_IsSwallowedIntoValidationMessage()
    {
        var vm = new AboutDialogViewModel(Info(), _ => throw new InvalidOperationException("没有默认浏览器"));

        vm.OpenLinkCommand.Execute(vm.Links[0]);

        Assert.That(vm.ValidationError, Does.Contain("没有默认浏览器"));
    }

    [Test]
    public void OpenLink_WithNullLink_DoesNothing()
    {
        var vm = new AboutDialogViewModel(Info(), _ => Task.FromResult(true));

        vm.OpenLinkCommand.Execute(null);

        Assert.That(vm.HasValidationError, Is.False);
    }
}
