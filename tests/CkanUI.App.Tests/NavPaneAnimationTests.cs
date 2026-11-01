using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using CkanUI.App.Tests.Automation;
using CkanUI.ViewModels;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace CkanUI.App.Tests;

[TestFixture]
[NonParallelizable]
public sealed class NavPaneAnimationTests
{
    [SetUp]
    public void SetUp() => HeadlessAppHost.EnsureInitialized();

    [Test]
    public void MainWindow_NavView_HasSmoothCollapseAndExpand()
    {
        MainWindow? window = null;
        var services = ServiceRegistration.Build(() => window!);
        try
        {
            var shell = services.GetRequiredService<ShellViewModel>();
            window = new MainWindow { DataContext = shell };
            window.Show();

            HeadlessAppHost.PumpJobs();
            var nav = window.FindControl<FANavigationView>("NavView");
            Assert.That(nav, Is.Not.Null);
            if (nav is null) return;

            var splitView = nav.GetVisualDescendants().OfType<SplitView>().FirstOrDefault();
            Assert.That(splitView, Is.Not.Null);
            if (splitView is null) return;

            var paneRoot = splitView.GetVisualDescendants().OfType<Panel>().FirstOrDefault(p => p.Name == "PART_PaneRoot");
            Assert.That(paneRoot, Is.Not.Null);
            if (paneRoot is null) return;

            // 1. 展开
            shell.ToggleNavCollapsedCommand.Execute(null);
            for (var i = 0; i < 25 && paneRoot.Bounds.Width < 100; i++)
            {
                HeadlessAppHost.PumpJobs();
                System.Threading.Thread.Sleep(15);
            }

            Assert.That(nav.IsPaneOpen, Is.True);
            Assert.That(paneRoot.Bounds.Width, Is.GreaterThan(100), "展开后侧栏宽度应扩大");

            // 2. 收起
            shell.ToggleNavCollapsedCommand.Execute(null);
            for (var i = 0; i < 20 && paneRoot.Bounds.Width > 60; i++)
            {
                HeadlessAppHost.PumpJobs();
                System.Threading.Thread.Sleep(15);
            }

            Assert.That(nav.IsPaneOpen, Is.False);
            Assert.That(paneRoot.Bounds.Width, Is.LessThanOrEqualTo(60), "收起后侧栏回到 52px 紧凑宽度");
        }
        finally
        {
            window?.Close();
            HeadlessAppHost.PumpJobs();
            services.Dispose();
        }
    }
}
