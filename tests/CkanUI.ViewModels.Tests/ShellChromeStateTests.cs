using System.Windows.Input;
using CkanUI.ViewModels;
using CkanUI.ViewModels.Navigation;
using CkanUI.ViewModels.Pages.Logs;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NUnit.Framework;

namespace CkanUI.ViewModels.Tests;

/// <summary>
/// 第二阶段外壳骨骼的状态机：状态栏日志开关、未保存浮条的显隐与命令转发。
/// 这些属性全部由 XAML 的 Classes.is-open 绑定消费，所以「值是推送过的」比「第一次算对」更重要。
/// </summary>
[TestFixture]
public sealed class ShellChromeStateTests
{
    [Test]
    public void ToggleLogDrawer_IsDisabledWithoutDrawer()
    {
        var shell = new ShellViewModel();

        Assert.That(shell.LogDrawer, Is.Null);
        Assert.That(shell.IsLogDrawerOpen, Is.False);
        Assert.That(shell.ToggleLogDrawerCommand.CanExecute(null), Is.False);
    }

    [Test]
    public void LogStatusText_FallsBackToNavTitleWithoutDrawer()
    {
        var shell = new ShellViewModel(new NavigationService(_ => null), key => "T:" + key);

        Assert.That(shell.LogStatusText, Is.EqualTo("T:Ui_NavRail_Logs"));
    }

    [Test]
    public void ToggleLogDrawer_FlipsDrawerAndMirrorsState()
    {
        var shell = new ShellViewModel(new NavigationService(_ => null), key => "T:" + key);
        var drawer = new LogDrawerViewModel();
        shell.LogDrawer = drawer;

        Assert.That(shell.IsLogDrawerOpen, Is.False);
        Assert.That(shell.ToggleLogDrawerCommand.CanExecute(null), Is.True);
        Assert.That(shell.LogStatusText, Does.Contain("0"));

        shell.ToggleLogDrawerCommand.Execute(null);
        Assert.That(drawer.IsOpen, Is.True);
        Assert.That(shell.IsLogDrawerOpen, Is.True);

        shell.ToggleLogDrawerCommand.Execute(null);
        Assert.That(drawer.IsOpen, Is.False);
        Assert.That(shell.IsLogDrawerOpen, Is.False);
    }

    [Test]
    public void LogStatusText_AppendsBadgeOnlyWhileCollapsed()
    {
        var shell = new ShellViewModel(new NavigationService(_ => null), key => "T:" + key);
        var drawer = new LogDrawerViewModel();
        shell.LogDrawer = drawer;

        drawer.NewErrorCount = 2;
        Assert.That(drawer.IsBadgeVisible, Is.True);
        Assert.That(shell.LogStatusText, Does.Contain("有 2 个错误"));

        // 展开后徽标清零，状态栏文案回到纯标题。
        drawer.Open();
        Assert.That(drawer.IsBadgeVisible, Is.False);
        Assert.That(shell.LogStatusText, Does.Not.Contain("有"));
    }

    [Test]
    public void UnsavedBar_IgnoresPagesWithoutTheContract()
    {
        var shell = new ShellViewModel(new NavigationService(_ => new object()), key => "T:" + key);

        Assert.That(shell.HasUnsavedChanges, Is.False);
        Assert.That(shell.HasUnsavedSaveAction, Is.False);
        Assert.That(shell.HasUnsavedDiscardAction, Is.False);
    }

    [Test]
    public async Task UnsavedBar_HidesBothActionsForPagesWithoutCommands()
    {
        var page = new PlainDirtyPage { HasUnsavedChanges = true };
        var shell = new ShellViewModel(new NavigationService(_ => page), key => "T:" + key);

        await shell.NavigateCommand.ExecuteAsync("plain");

        Assert.That(shell.HasUnsavedChanges, Is.True);
        Assert.That(shell.UnsavedSaveCommand, Is.Null);
        Assert.That(shell.UnsavedDiscardCommand, Is.Null);
        Assert.That(shell.HasUnsavedSaveAction, Is.False);
        Assert.That(shell.HasUnsavedDiscardAction, Is.False);
    }

    [Test]
    public async Task UnsavedBar_FollowsThePageFlagAndForwardsCommands()
    {
        var page = new DirtyPage();
        var shell = new ShellViewModel(new NavigationService(_ => page), key => "T:" + key);

        await shell.NavigateCommand.ExecuteAsync("dirty");

        Assert.That(shell.HasUnsavedChanges, Is.False);
        Assert.That(shell.UnsavedSaveCommand, Is.SameAs(page.SaveCommand));
        Assert.That(shell.UnsavedDiscardCommand, Is.SameAs(page.DiscardCommand));
        Assert.That(shell.HasUnsavedSaveAction, Is.True);
        Assert.That(shell.HasUnsavedDiscardAction, Is.True);

        page.IsDirty = true;
        Assert.That(shell.HasUnsavedChanges, Is.True);

        page.IsDirty = false;
        Assert.That(shell.HasUnsavedChanges, Is.False);
    }

    [Test]
    public async Task UnsavedBar_StopsListeningToThePreviousPage()
    {
        var first = new DirtyPage { IsDirty = true };
        var second = new DirtyPage();
        object? current = first;
        var shell = new ShellViewModel(new NavigationService(_ => current), key => "T:" + key);

        await shell.NavigateCommand.ExecuteAsync("first");
        Assert.That(shell.HasUnsavedChanges, Is.True);

        current = second;
        await shell.NavigateCommand.ExecuteAsync("second");
        Assert.That(shell.HasUnsavedChanges, Is.False);

        // 上一页即使此刻自己变脏，也不该再把外壳的提示条点亮。
        first.IsDirty = false;
        first.IsDirty = true;
        Assert.That(shell.HasUnsavedChanges, Is.False);
    }

    /// <summary>只有标记、没有保存/放弃语义的页面：提示条上两个按钮都不该出现。</summary>
    private sealed class PlainDirtyPage : ObservableObject, IUnsavedChangesAware
    {
        private bool _hasUnsavedChanges;

        public bool HasUnsavedChanges
        {
            get => _hasUnsavedChanges;
            set => SetProperty(ref _hasUnsavedChanges, value);
        }
    }

    /// <summary>带明确保存/放弃语义的页面（设置页、modpack 编辑器都是这一类）。</summary>
    private sealed class DirtyPage : ObservableObject, IUnsavedChangesAware
    {
        public DirtyPage()
        {
            SaveCommand = new RelayCommand(() => { });
            DiscardCommand = new RelayCommand(() => { });
        }

        private bool _isDirty;

        public bool IsDirty
        {
            get => _isDirty;
            set
            {
                if (SetProperty(ref _isDirty, value))
                {
                    OnPropertyChanged(nameof(HasUnsavedChanges));
                }
            }
        }

        public bool HasUnsavedChanges => IsDirty;

        public ICommand SaveCommand { get; }

        public ICommand DiscardCommand { get; }

        ICommand? IUnsavedChangesAware.SaveChangesCommand => SaveCommand;

        ICommand? IUnsavedChangesAware.DiscardChangesCommand => DiscardCommand;
    }
}
