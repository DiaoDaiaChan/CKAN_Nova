using System.ComponentModel;
using CkanUI.Services.Abstractions;
using CkanUI.ViewModels;
using CkanUI.ViewModels.Navigation;
using NUnit.Framework;

namespace CkanUI.ViewModels.Tests;

/// <summary>
/// 页面缩放（底栏滑块）的取值口径：5% 网格、75%–150% 夹取、百分比双向换算、落盘一次一写。
/// 这些不变量在视图层没有第二份，滑块、Ctrl+滚轮、加减命令全走同一条 setter。
/// </summary>
[TestFixture]
public sealed class ShellPageZoomTests
{
    [Test]
    public void PageZoom_DefaultsToFullSize()
    {
        var shell = new ShellViewModel();

        Assert.That(shell.PageZoom, Is.EqualTo(ShellViewModel.DefaultPageZoom));
        Assert.That(shell.PageZoomPercent, Is.EqualTo(100d));
        Assert.That(shell.PageZoomPercentText, Is.EqualTo("100%"));
        Assert.That(shell.CanZoomIn, Is.True);
        Assert.That(shell.CanZoomOut, Is.True);
    }

    [Test]
    public void PageZoom_LoadsFromSettingsAndSnapsToTheGrid()
    {
        var store = new RecordingSettingsStore { Current = new UiSettings { PageZoom = 1.23d } };

        var shell = new ShellViewModel(new NavigationService(_ => null), settingsStore: store);

        Assert.That(shell.PageZoom, Is.EqualTo(1.25d));
        Assert.That(shell.PageZoomPercentText, Is.EqualTo("125%"));
        Assert.That(store.Saved, Is.Empty, "构造期只读设置，不该反手写一遍文件");
    }

    [Test]
    public void PageZoomPercent_ConvertsBothWays()
    {
        var shell = new ShellViewModel();

        shell.PageZoomPercent = 137d;

        Assert.That(shell.PageZoom, Is.EqualTo(1.35d));
        Assert.That(shell.PageZoomPercent, Is.EqualTo(135d));
        Assert.That(shell.PageZoomPercentText, Is.EqualTo("135%"));
    }

    [Test]
    public void PageZoom_ClampsAndFallsBackOnNonFiniteInput()
    {
        var shell = new ShellViewModel();

        shell.PageZoom = 3d;
        Assert.That(shell.PageZoom, Is.EqualTo(ShellViewModel.MaxPageZoom));

        shell.PageZoom = 0.1d;
        Assert.That(shell.PageZoom, Is.EqualTo(ShellViewModel.MinPageZoom));

        shell.PageZoom = double.NaN;
        Assert.That(shell.PageZoom, Is.EqualTo(ShellViewModel.DefaultPageZoom));
    }

    [Test]
    public void ZoomCommands_StepByFivePercentAndStopAtTheBounds()
    {
        var shell = new ShellViewModel();

        shell.ZoomOutCommand.Execute(null);
        Assert.That(shell.PageZoom, Is.EqualTo(0.95d).Within(1e-9));

        for (var i = 0; i < 10; i++)
        {
            shell.ZoomOutCommand.Execute(null);
        }

        Assert.That(shell.PageZoom, Is.EqualTo(ShellViewModel.MinPageZoom));
        Assert.That(shell.CanZoomOut, Is.False);
        Assert.That(shell.ZoomOutCommand.CanExecute(null), Is.False);

        for (var i = 0; i < 20; i++)
        {
            shell.ZoomInCommand.Execute(null);
        }

        Assert.That(shell.PageZoom, Is.EqualTo(ShellViewModel.MaxPageZoom));
        Assert.That(shell.CanZoomIn, Is.False);
        Assert.That(shell.ZoomInCommand.CanExecute(null), Is.False);
    }

    [Test]
    public void NudgePageZoom_TakesWholeStepsOnly()
    {
        var shell = new ShellViewModel();

        shell.NudgePageZoom(2);
        Assert.That(shell.PageZoom, Is.EqualTo(1.10d).Within(1e-9));

        shell.NudgePageZoom(-3);
        Assert.That(shell.PageZoom, Is.EqualTo(0.95d).Within(1e-9));

        shell.NudgePageZoom(0);
        Assert.That(shell.PageZoom, Is.EqualTo(0.95d).Within(1e-9));
    }

    [Test]
    public void ResetPageZoom_ReturnsToFullSize()
    {
        var shell = new ShellViewModel();
        shell.PageZoom = 1.4d;

        shell.ResetPageZoomCommand.Execute(null);

        Assert.That(shell.PageZoom, Is.EqualTo(ShellViewModel.DefaultPageZoom));
        Assert.That(shell.CanZoomIn && shell.CanZoomOut, Is.True);
    }

    [Test]
    public void PageZoom_NotifiesDerivedMembersAndPersistsOnce()
    {
        var store = new RecordingSettingsStore();
        var shell = new ShellViewModel(new NavigationService(_ => null), settingsStore: store);
        var changed = new List<string?>();
        ((INotifyPropertyChanged)shell).PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        shell.PageZoom = 1.15d;

        Assert.That(changed, Does.Contain(nameof(ShellViewModel.PageZoom)));
        Assert.That(changed, Does.Contain(nameof(ShellViewModel.PageZoomPercent)));
        Assert.That(changed, Does.Contain(nameof(ShellViewModel.PageZoomPercentText)));
        Assert.That(changed, Does.Contain(nameof(ShellViewModel.CanZoomIn)));
        Assert.That(changed, Does.Contain(nameof(ShellViewModel.CanZoomOut)));
        Assert.That(store.Saved, Has.Count.EqualTo(1));
        Assert.That(store.Saved[0].PageZoom, Is.EqualTo(1.15d).Within(1e-9));
    }

    [Test]
    public void PageZoom_SettingTheSameValueDoesNotRewriteSettings()
    {
        var store = new RecordingSettingsStore();
        var shell = new ShellViewModel(new NavigationService(_ => null), settingsStore: store);

        shell.PageZoom = ShellViewModel.DefaultPageZoom;
        shell.PageZoom = 1.4d;
        shell.PageZoom = 1.4d;

        Assert.That(store.Saved, Has.Count.EqualTo(1), "重复写入同一比例只会多一次磁盘写，没有意义");
    }

    [Test]
    public void PageZoom_DraggingCoalescesPersistenceIntoOneWriteOnRelease()
    {
        var store = new RecordingSettingsStore();
        var shell = new ShellViewModel(new NavigationService(_ => null), settingsStore: store);

        shell.BeginPageZoomGesture();
        foreach (var percent in new[] { 105d, 110d, 115d, 120d, 125d, 135d, 150d })
        {
            shell.PageZoomPercent = percent;
        }

        Assert.That(store.Saved, Is.Empty, "拖动过程中只改内存，不写盘");
        Assert.That(shell.PageZoom, Is.EqualTo(1.5d).Within(1e-9));

        shell.EndPageZoomGesture();

        Assert.That(store.Saved, Has.Count.EqualTo(1), "松开滑块只落盘一次");
        Assert.That(store.Saved[0].PageZoom, Is.EqualTo(1.5d).Within(1e-9));

        shell.EndPageZoomGesture();
        Assert.That(store.Saved, Has.Count.EqualTo(1), "重复收尾不该再写一次");
    }

    [Test]
    public void PageZoom_GestureWithoutChangesWritesNothing()
    {
        var store = new RecordingSettingsStore();
        var shell = new ShellViewModel(new NavigationService(_ => null), settingsStore: store);

        shell.BeginPageZoomGesture();
        shell.EndPageZoomGesture();

        Assert.That(store.Saved, Is.Empty);
    }

    private sealed class RecordingSettingsStore : IUiSettingsStore
    {
        public List<UiSettings> Saved { get; } = [];

        public UiSettings Current { get; set; } = new();

        public UiSettings Load() => Current;

        public event Action<UiSettings>? Changed
        {
            add { }
            remove { }
        }

        public OperationResult<bool> Save(UiSettings settings)
        {
            Current = settings;
            Saved.Add(settings);
            return OperationResult<bool>.Ok(true);
        }
    }
}
