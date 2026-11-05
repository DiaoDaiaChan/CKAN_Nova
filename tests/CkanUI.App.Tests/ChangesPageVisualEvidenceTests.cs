#pragma warning disable CS0618
using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using CkanUI.App.Tests.Automation;
using CkanUI.App.Views;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.ViewModels.Dialogs.Mods;
using CkanUI.ViewModels.Pages;
using CkanUI.ViewModels.Pages.Changes;
using NUnit.Framework;

namespace CkanUI.App.Tests;

/// <summary>
/// 变更集页收口后的像素级证据：把修好的状态栏、冲突行、警告出口渲染成 PNG，
/// 落到仓库 <c>artifacts/visual-probe/</c>，供人眼看图复核（无头环境也能留证）。
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class ChangesPageVisualEvidenceTests
{
    [Test]
    public void RendersFixedStatusBarAndConflictRow()
    {
        HeadlessAppHost.EnsureInitialized();

        var preview = new ChangeSetPreview(
            new[]
            {
                new ChangeItemDto("NearFutureLaunchVehicles", "Near Future Launch Vehicles", ChangeAction.Install, null, "2.0.0", ChangeOrigin.UserRequested, 131_100_000),
                new ChangeItemDto("Waterfall", "Waterfall Core", ChangeAction.Upgrade, "0.10.0", "0.11.0", ChangeOrigin.Dependency, 1_200_000),
                new ChangeItemDto("DeployableEngines", "Deployable Engines", ChangeAction.Remove, "1.0", null, ChangeOrigin.UserRequested, null),
            },
            new[] { "RP-1 4.7.0.0 与 CommunityTechTree 1:3.4.5 冲突" },
            131_100_000,
            new[] { "未能读取下载缓存目录，预计下载体积可能重复计算已缓存的文件。" })
        {
            ConflictEntries = new[]
            {
                new ConflictEntry("RP-1 4.7.0.0 与 CommunityTechTree 1:3.4.5 冲突", "RP-1", "Realistic Progression One"),
            },
        };

        var vm = new ChangesPageViewModel
        {
            Preview = preview,
            State = ChangeExecutionState.Done,
            ExecutionPercent = 100,
            ProgressStageText = "安装 3/3",
        };

        var view = new ChangesPageView { DataContext = vm };
        var window = HeadlessAppHost.CreateTestWindow(view, 1280, 800);
        try
        {
            var frame = window.CaptureRenderedFrame();
            Assert.That(frame, Is.Not.Null, "无头渲染必须产出帧，否则像素证据无效");

            var path = Path.Combine(ResolveRepoRoot(), "artifacts", "visual-probe", "changes-after-fix.png");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            frame!.Save(path);

            Assert.That(new FileInfo(path).Length, Is.GreaterThan(4096), "渲染图不能是空壳");
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// 失败态的证据图：一屏只说一次「失败」，原因在错误栏，结果区不再被上游问句占据。
    /// 修复前这张图里同一件事出现在四个位置（结果区 / 错误栏 / 状态栏 / 结果汇总）。
    /// </summary>
    [Test]
    public void RendersSinglePlaceFailureState()
    {
        HeadlessAppHost.EnsureInitialized();

        var preview = new ChangeSetPreview(
            new[]
            {
                new ChangeItemDto("AIES", "AIES Aerospace", ChangeAction.Install, null, "1.0.0", ChangeOrigin.UserRequested, 10_700_000),
            },
            Array.Empty<string>(),
            10_700_000,
            new[] { "未能读取下载缓存目录，预计下载体积可能重复计算已缓存的文件。" });

        var vm = new ChangesPageViewModel
        {
            Preview = preview,
            State = ChangeExecutionState.Failed,
            ExecutionPercent = 0,
            StatusText = string.Empty,
            ResultText = string.Empty,
            LastErrorText = "有多个模组提供同一依赖，无法自动选择，未执行任何操作。",
        };

        var view = new ChangesPageView { DataContext = vm };
        var window = HeadlessAppHost.CreateTestWindow(view, 1280, 800);
        try
        {
            var frame = window.CaptureRenderedFrame();
            Assert.That(frame, Is.Not.Null);

            var path = Path.Combine(ResolveRepoRoot(), "artifacts", "visual-probe", "changes-failure-after-fix.png");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            frame!.Save(path);

            Assert.That(new FileInfo(path).Length, Is.GreaterThan(4096));
        }
        finally
        {
            window.Close();
        }
    }

    private static string ResolveRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ckan-ui.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? AppContext.BaseDirectory;
    }
}
