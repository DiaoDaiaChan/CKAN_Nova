using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using CkanUI.App.Ai;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Ai;
using CkanUI.ViewModels.Navigation;

using CommunityToolkit.Mvvm.Input;

using NUnit.Framework;

namespace CkanUI.App.Tests;

/// <summary>
/// 桥的遍历式控制面：状态读取、设置项反射、页面命令执行（含导航与破坏性确认）。
/// 这些路径的价值在于「不写清单也能覆盖新页面」，所以断言要看反射结果，而不是固定键表。
/// </summary>
[TestFixture]
public sealed class AiCkanBridgeUiControlTests
{
    [Test]
    public void GetUiState_ReadsNavigationFlagsAndPageFields()
    {
        var page = new FakePageViewModel();
        var navigation = new StubNavigation { Page = page, Key = "mods" };
        var bridge = new AiCkanBridge(navigation: navigation);

        var state = bridge.GetUiState();

        Assert.Multiple(() =>
        {
            Assert.That(state.PageKey, Is.EqualTo("mods"));
            Assert.That(state.PageTitle, Is.EqualTo("模组"));
            Assert.That(state.ViewModel, Is.EqualTo(nameof(FakePageViewModel)));
            Assert.That(state.Fields.Select(field => field.Name), Does.Contain("CanGoBack"));
            Assert.That(state.Fields.Single(field => field.Name == "SearchText").Value, Is.EqualTo("warp"));
            Assert.That(state.Actions.Select(action => action.Name), Does.Contain("RefreshCommand"));
        });
    }

    [Test]
    public void ListSettings_ExposesWritableUiSettingsAndMasksApiKey()
    {
        var bridge = new AiCkanBridge(uiSettings: new StubUiSettingsStore());

        var settings = bridge.ListSettings();

        Assert.Multiple(() =>
        {
            Assert.That(settings.Select(entry => entry.Key), Does.Contain("Theme"));
            var key = settings.Single(entry => entry.Key == "AiApiKeyProtected");
            Assert.That(key.Value, Is.EqualTo("***"), "密钥不能跟着设置清单进模型上下文");
            Assert.That(settings.Single(entry => entry.Key == "Theme").Kind, Is.EqualTo("text"));
        });
    }

    [Test]
    public async Task SetSettingAsync_WritesUiSettingsThroughReflectionAndAlias()
    {
        var store = new StubUiSettingsStore();
        var bridge = new AiCkanBridge(uiSettings: store);

        var theme = await bridge.SetSettingAsync("Theme", "light");
        var skin = await bridge.SetSettingAsync("skin", "coronal-gold");

        Assert.Multiple(() =>
        {
            Assert.That(theme.IsSuccess, Is.True);
            Assert.That(skin.IsSuccess, Is.True);
            Assert.That(store.Current.Theme, Is.EqualTo("light"));
            Assert.That(store.Current.ThemeSkin, Is.EqualTo("coronal-gold"), "旧别名要能落到真实属性上");
        });
    }

    [Test]
    public async Task RunPageActionAsync_NavigatesToTargetPageBeforeActing()
    {
        var page = new FakePageViewModel();
        var navigation = new StubNavigation { Page = page, Key = "home" };
        var bridge = new AiCkanBridge(navigation: navigation);

        var result = await bridge.RunPageActionAsync("RefreshCommand", pageKey: "mods");

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(navigation.Calls, Does.Contain("mods"));
            Assert.That(page.RefreshExecutions, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task RunPageActionAsync_DestructiveCommandNeedsUserConfirmation()
    {
        var page = new FakePageViewModel();
        var navigation = new StubNavigation { Page = page, Key = "mods" };
        var dialogs = new StubDialogs { ConfirmResult = false };
        var bridge = new AiCkanBridge(navigation: navigation, dialogs: dialogs);

        var refused = await bridge.RunPageActionAsync("UninstallCommand");

        Assert.Multiple(() =>
        {
            Assert.That(refused.IsSuccess, Is.False);
            Assert.That(refused.Message, Does.Contain("拒绝"));
            Assert.That(page.UninstallExecutions, Is.Zero, "用户没点头就不许动磁盘");
            Assert.That(dialogs.ConfirmCalls, Is.EqualTo(1));
        });

        dialogs.ConfirmResult = true;
        var accepted = await bridge.RunPageActionAsync("UninstallCommand");

        Assert.Multiple(() =>
        {
            Assert.That(accepted.IsSuccess, Is.True);
            Assert.That(page.UninstallExecutions, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task RunPageActionAsync_WithoutDialogServiceRefusesDestructiveAction()
    {
        var page = new FakePageViewModel();
        var bridge = new AiCkanBridge(navigation: new StubNavigation { Page = page, Key = "mods" });

        var result = await bridge.RunPageActionAsync("UninstallCommand");

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Message, Does.Contain("确认对话框不可用"));
            Assert.That(page.UninstallExecutions, Is.Zero);
        });
    }

    [Test]
    public async Task SetUiFieldAsync_NavigatesToTargetPageAndWritesValue()
    {
        var page = new FakePageViewModel();
        var navigation = new StubNavigation { Page = page, Key = "home" };
        var bridge = new AiCkanBridge(navigation: navigation);

        var result = await bridge.SetUiFieldAsync("SearchText", "realchute", pageKey: "mods");

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(navigation.Calls, Does.Contain("mods"));
            Assert.That(page.SearchText, Is.EqualTo("realchute"));
            Assert.That(result.Value, Does.Contain("realchute"));
        });
    }

    [Test]
    public async Task SetUiFieldAsync_RejectsSensitiveFieldWithoutChangingIt()
    {
        var page = new FakePageViewModel();
        var bridge = new AiCkanBridge(navigation: new StubNavigation { Page = page, Key = "settings" });

        var result = await bridge.SetUiFieldAsync("ApiKey", "leak");

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Message, Does.Contain("密钥"));
            Assert.That(page.ApiKey, Is.EqualTo("super-secret"));
        });
    }

    private sealed class FakePageViewModel
    {
        public string SearchText { get; set; } = "warp";

        public string ApiKey { get; set; } = "super-secret";

        public int Total { get; set; } = 3;

        public int RefreshExecutions { get; private set; }

        public int UninstallExecutions { get; private set; }

        public RelayCommand RefreshCommand { get; }

        public RelayCommand UninstallCommand { get; }

        public FakePageViewModel()
        {
            RefreshCommand = new RelayCommand(() => RefreshExecutions++);
            UninstallCommand = new RelayCommand(() => UninstallExecutions++);
        }
    }

    private sealed class StubNavigation : INavigationService
    {
        public List<string> Calls { get; } = new();

        public object? Page { get; set; }

        public string Key { get; set; } = string.Empty;

        public object? CurrentPage => Page;

        public string CurrentKey => Key;

        public event Action<object?, string>? Navigated
        {
            add { }
            remove { }
        }

        public Task<bool> NavigateAsync(string pageKey, object? parameter = null, CancellationToken ct = default)
        {
            Calls.Add(pageKey);
            Key = pageKey;
            return Task.FromResult(true);
        }
    }

    private sealed class StubDialogs : IDialogService
    {
        public bool ConfirmResult { get; set; } = true;

        public int ConfirmCalls { get; private set; }

        public Task<bool> ConfirmAsync(string title, string message, string? detail = null)
        {
            ConfirmCalls++;
            return Task.FromResult(ConfirmResult);
        }

        public Task<int> SelectAsync(string title, string message, IReadOnlyList<string> options, int defaultIndex = 0) =>
            Task.FromResult(defaultIndex);

        public Task ShowMessageAsync(string title, string message) => Task.CompletedTask;

        public Task ShowErrorAsync(string title, string message, string? detail = null) => Task.CompletedTask;

        public Task<OperationResult<bool>> RunWithProgressAsync(
            string title,
            Func<IProgress<double>, CancellationToken, Task> work) =>
            Task.FromResult(OperationResult<bool>.Ok(true));
    }

    private sealed class StubUiSettingsStore : IUiSettingsStore
    {
        public UiSettings Current { get; set; } = new();

        public event Action<UiSettings>? Changed;

        public UiSettings Load() => Current;

        public OperationResult<bool> Save(UiSettings settings)
        {
            Current = settings;
            Changed?.Invoke(settings);
            return OperationResult<bool>.Ok(true);
        }
    }

}