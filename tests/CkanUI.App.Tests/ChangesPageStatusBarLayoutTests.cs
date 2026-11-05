using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using CkanUI.App.Tests.Automation;
using CkanUI.App.Views;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.ViewModels.Dialogs.Mods;
using CkanUI.ViewModels.Pages;
using CkanUI.ViewModels.Pages.Changes;
using NUnit.Framework;

namespace CkanUI.App.Tests;

/// <summary>
/// 变更集页状态栏与冲突区的像素级回归（E1/E2/E3/E4）。
/// 这几条在真机上是「进度条横穿文字」「执行完进度条消失」「冲突只有一句话没有出口」，
/// 纯 VM 测试看不出重叠，必须在无头窗口里量真实 Bounds。
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class ChangesPageStatusBarLayoutTests
{
    [SetUp]
    public void SetUp() => HeadlessAppHost.EnsureInitialized();

    private static ChangeSetPreview Preview(params string[] warnings) => new(
        new[]
        {
            new ChangeItemDto("NearFutureLaunchVehicles", "Near Future Launch Vehicles", ChangeAction.Install, null, "2.0.0", ChangeOrigin.UserRequested, 131_100_000),
        },
        new[] { "RP-1 4.7.0.0 与 CommunityTechTree 1:3.4.5 冲突" },
        131_100_000,
        warnings)
    {
        ConflictEntries = new[]
        {
            new ConflictEntry("RP-1 4.7.0.0 与 CommunityTechTree 1:3.4.5 冲突", "RP-1", "Realistic Progression One"),
        },
    };

    private static (Window Window, ChangesPageView View, ChangesPageViewModel Vm) Show(ChangeSetPreview preview, ChangeExecutionState state, double percent)
    {
        var vm = new ChangesPageViewModel
        {
            Preview = preview,
            State = state,
            ExecutionPercent = percent,
        };
        var view = new ChangesPageView { DataContext = vm };
        var window = HeadlessAppHost.CreateTestWindow(view);
        return (window, view, vm);
    }

    private static Rect BoundsInWindow(Visual visual, Window window)
    {
        var origin = visual.TranslatePoint(new Point(0, 0), window) ?? default;
        return new Rect(origin, visual.Bounds.Size);
    }

    [Test]
    public void StatusText_AndProgressBar_DoNotOverlap()
    {
        var (window, view, vm) = Show(Preview(), ChangeExecutionState.Done, 100);
        try
        {
            var statusText = view.GetVisualDescendants().OfType<TextBlock>()
                .Single(block => block.Text == vm.StateText && block.IsEffectivelyVisible);
            var progressBar = view.GetVisualDescendants().OfType<ProgressBar>()
                .Single(bar => bar.IsEffectivelyVisible);

            var textRect = BoundsInWindow(statusText, window);
            var barRect = BoundsInWindow(progressBar, window);

            Assert.Multiple(() =>
            {
                Assert.That(textRect.Intersects(barRect), Is.False,
                    $"状态文字 {textRect} 与进度条 {barRect} 不得重叠（E4：进度条曾横穿文字）");
                Assert.That(barRect.Top, Is.GreaterThanOrEqualTo(textRect.Bottom - 0.5),
                    "进度条必须整行落在状态文字下方，不是并排挤在同一行");
                Assert.That(textRect.Width, Is.GreaterThan(360),
                    "状态文字列要占满整行自适应换行（旧布局 MaxWidth=360 会在这里失败）");
                Assert.That(barRect.Width, Is.GreaterThan(0), "进度条要有实际宽度，不能是 0");
            });
        }
        finally
        {
            window.Close();
        }
    }

    [Test]
    public void ProgressBar_StaysVisibleAfterDone()
    {
        var (window, view, vm) = Show(Preview(), ChangeExecutionState.Done, 100);
        try
        {
            var bar = view.GetVisualDescendants().OfType<ProgressBar>().Single(control => control.IsEffectivelyVisible);

            Assert.Multiple(() =>
            {
                Assert.That(vm.IsApplying, Is.False, "终态不是在执行中");
                Assert.That(vm.IsProgressVisible, Is.True, "执行结束后进度条仍要显示最终进度（E3）");
                Assert.That(bar.Value, Is.EqualTo(100));
                Assert.That(view.GetVisualDescendants().OfType<TextBlock>()
                    .Any(block => block.Text == "100%" && block.IsEffectivelyVisible), Is.True, "终态百分比要看得见");
            });
        }
        finally
        {
            window.Close();
        }
    }

    [Test]
    public void ConflictRow_ShowsModuleIdentity_AndOffersModsExit()
    {
        var (window, view, vm) = Show(Preview(), ChangeExecutionState.Idle, 0);
        try
        {
            var moduleTexts = view.GetVisualDescendants().OfType<TextBlock>()
                .Where(block => block.DataContext is ConflictRowViewModel)
                .Select(block => block.Text)
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .ToArray();

            Assert.Multiple(() =>
            {
                Assert.That(vm.ConflictRows, Has.Count.EqualTo(1));
                Assert.That(moduleTexts, Does.Contain(vm.ConflictRows[0].ModuleText),
                    "冲突行要带模组标识（B2：以前只有一句描述，点不进去也不知道是谁）");
                Assert.That(view.GetVisualDescendants().OfType<Button>()
                    .Any(button => button.Command == vm.GoToModsCommand && button.IsEffectivelyVisible), Is.True,
                    "冲突要给出路：跳到模组页取消勾选（E2）");
            });
        }
        finally
        {
            window.Close();
        }
    }

    [Test]
    public void CacheWarning_OnlyThenShowsSettingsExit()
    {
        var (firstWindow, firstView, firstVm) = Show(
            Preview("未能读取下载缓存目录，预计下载体积可能重复计算已缓存的文件。"),
            ChangeExecutionState.Idle,
            0);
        var (secondWindow, secondView, secondVm) = Show(
            Preview("已忽略 1 条卸载请求：该模组仍被其它模组依赖。"),
            ChangeExecutionState.Idle,
            0);
        try
        {
            Assert.Multiple(() =>
            {
                Assert.That(firstVm.HasDownloadCacheWarning, Is.True);
                Assert.That(firstView.GetVisualDescendants().OfType<Button>()
                    .Any(button => button.Command == firstVm.OpenDownloadCacheCommand && button.IsEffectivelyVisible), Is.True,
                    "缓存目录读不到时要给「打开缓存设置」的出口（E1）");

                Assert.That(secondVm.HasDownloadCacheWarning, Is.False);
                Assert.That(secondView.GetVisualDescendants().OfType<Button>()
                    .Any(button => button.Command == secondVm.OpenDownloadCacheCommand && button.IsEffectivelyVisible), Is.False,
                    "其它提示不该冒充缓存提示给按钮，否则是误导");
            });
        }
        finally
        {
            firstWindow.Close();
            secondWindow.Close();
        }
    }
}
