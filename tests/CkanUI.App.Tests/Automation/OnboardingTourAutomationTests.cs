using Avalonia.Controls;
using CkanUI.App.Views;
using CkanUI.Services.Abstractions;
using CkanUI.ViewModels;
using CkanUI.ViewModels.Onboarding;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace CkanUI.App.Tests.Automation;

/// <summary>
/// 首次启动新手高亮引导自动化测试套件：
/// 验证遮罩高亮引导在主窗口中的挂载、步骤推进、目标定位、跳过与完成持久化、
/// 以及通过快捷指令或代码调起引导。
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class OnboardingTourAutomationTests
{
    private string _tempDirectory = null!;
    private string _tempSettingsFile = null!;
    private ServiceProvider _services = null!;
    private ShellViewModel _shell = null!;
    private MainWindow _mainWindow = null!;
    private IUiSettingsStore _settingsStore = null!;
    private OnboardingTourViewModel _tourVm = null!;

    [SetUp]
    public void SetUp()
    {
        HeadlessAppHost.EnsureInitialized();

        _tempDirectory = Path.Combine(AppContext.BaseDirectory, "headless_temp", "onboarding-tour", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
        _tempSettingsFile = Path.Combine(_tempDirectory, "ckan-ui.json");
        _settingsStore = new CkanUI.Services.Settings.UiSettingsStore(_tempSettingsFile);

        _services = ServiceRegistration.Build(
            () => _mainWindow,
            services => services.AddSingleton<IUiSettingsStore>(_settingsStore));

        _shell = _services.GetRequiredService<ShellViewModel>();
        _tourVm = _services.GetRequiredService<OnboardingTourViewModel>();

        _mainWindow = new MainWindow
        {
            DataContext = _shell
        };
        _mainWindow.AttachOnboardingTour(_tourVm);

        _mainWindow.Show();
        HeadlessAppHost.PumpJobs();
    }

    [TearDown]
    public void TearDown()
    {
        _mainWindow?.Close();
        HeadlessAppHost.PumpJobs();
        _services?.Dispose();

        if (File.Exists(_tempSettingsFile))
        {
            try { File.Delete(_tempSettingsFile); } catch { }
        }
    }

    [Test]
    public void OnboardingTour_ShowAndStepThrough_CompletesAndPersistsSettings()
    {
        var overlay = _mainWindow.FindControl<OnboardingTourOverlay>("OnboardingOverlay");
        Assert.That(overlay, Is.Not.Null, "主窗口必须包含 OnboardingTourOverlay 控件");

        TestContext.WriteLine($"Initial HasSeenOnboardingTour={_settingsStore.Load().HasSeenOnboardingTour}");

        // 触发新手引导
        _mainWindow.ShowOnboardingGuide();
        HeadlessAppHost.PumpJobs();

        Assert.That(_tourVm.IsActive, Is.True, "引导启动后状态应为 Active");
        Assert.That(_tourVm.CurrentStepIndex, Is.EqualTo(0), "引导启动默认在第一步");
        Assert.That(_tourVm.CanGoBack, Is.False, "第一步不能后退");
        Assert.That(_tourVm.NextButtonText, Is.Not.Empty);

        // 单步推进测试
        _tourVm.NextCommand.Execute(null);
        HeadlessAppHost.PumpJobs();

        Assert.That(_tourVm.CurrentStepIndex, Is.EqualTo(1));
        Assert.That(_tourVm.CanGoBack, Is.True, "第二步应允许后退");
        Assert.That(_tourVm.CurrentStep?.TargetType, Is.EqualTo(OnboardingTargetType.NavigationSidebar));

        // 后退测试
        _tourVm.PreviousCommand.Execute(null);
        HeadlessAppHost.PumpJobs();
        Assert.That(_tourVm.CurrentStepIndex, Is.EqualTo(0));

        // 推进到最后一步并完成
        while (_tourVm.CurrentStepIndex < _tourVm.Steps.Count - 1)
        {
            _tourVm.NextCommand.Execute(null);
            HeadlessAppHost.PumpJobs();
        }

        Assert.That(_tourVm.CurrentStepIndex, Is.EqualTo(_tourVm.Steps.Count - 1));
        _tourVm.NextCommand.Execute(null);
        HeadlessAppHost.PumpJobs();

        Assert.That(_tourVm.IsActive, Is.False, "完成最后一步后引导应退出");
        var savedSettings = _settingsStore.Load();
        Assert.That(savedSettings.HasSeenOnboardingTour, Is.True, "完成引导后必须持久化 HasSeenOnboardingTour = true");
    }

    [Test]
    public void OnboardingTour_Skip_ClosesTourAndPersistsSettings()
    {
        _mainWindow.ShowOnboardingGuide();
        HeadlessAppHost.PumpJobs();

        Assert.That(_tourVm.IsActive, Is.True);
        _tourVm.SkipCommand.Execute(null);
        HeadlessAppHost.PumpJobs();

        Assert.That(_tourVm.IsActive, Is.False, "跳过引导后应退出");
        var savedSettings = _settingsStore.Load();
        Assert.That(savedSettings.HasSeenOnboardingTour, Is.True, "跳过引导也应持久化 HasSeenOnboardingTour = true");
    }
}
