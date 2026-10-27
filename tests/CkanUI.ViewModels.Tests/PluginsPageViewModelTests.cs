using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.ViewModels.Pages;
using CkanUI.ViewModels.Pages.Plugins;
using NUnit.Framework;

namespace CkanUI.ViewModels.Tests;

/// <summary>
/// 插件页 VM（12 篇 F-17 / 06 篇 UI-33）：Active / Dormant 两栏分组 + 按钮启用条件 + 中文状态文案。
/// </summary>
/// <remarks>
/// 服务用假实现（真服务要碰文件系统和 ALC，见 <c>CkanUI.Services.Tests.PluginServiceTests</c>）。
/// 这里只验证页面口径：两栏互斥选中、按钮随选中/忙碌变化、失败文案与错误细节。
/// </remarks>
[TestFixture]
public sealed class PluginsPageViewModelTests
{
    [Test]
    public async Task Activate_LoadsBothColumnsAndSummaries()
    {
        var service = new FakePluginService();
        service.ActiveItems.Add(PluginsFixtures.Plugin("com.example.on", state: PluginState.Active));
        service.DormantItems.Add(PluginsFixtures.Plugin("com.example.off"));
        var vm = Create(service);

        vm.Activate();
        await WaitForAsync(() => vm.ActivePlugins.Count == 1 && vm.DormantPlugins.Count == 1);

        Assert.That(vm.ActivePlugins[0].StateText, Is.EqualTo("已激活"));
        Assert.That(vm.DormantPlugins[0].StateText, Is.EqualTo("未激活"));
        Assert.That(vm.ActiveSummary, Is.EqualTo("已激活 1 个"));
        Assert.That(vm.DormantSummary, Is.EqualTo("未激活 1 个"));
        Assert.That(vm.HasNoActive, Is.False);
        Assert.That(vm.PluginsDirectory, Is.EqualTo(service.PluginsDirectory));
        Assert.That(service.DiscoverCalls, Is.EqualTo(1), "首次激活只扫一次目录");
    }

    [Test]
    public async Task EmptyService_ShowsEmptySummariesAndNoSelection()
    {
        var vm = Create(new FakePluginService());

        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.That(vm.HasNoActive, Is.True);
        Assert.That(vm.HasNoDormant, Is.True);
        Assert.That(vm.ActiveSummary, Is.EqualTo("没有激活的插件"));
        Assert.That(vm.DormantSummary, Is.EqualTo("没有待激活的插件"));
        Assert.That(vm.CurrentSelection, Is.Null);
    }

    [Test]
    public async Task Buttons_RequireSelectionInTheRightColumn()
    {
        var service = new FakePluginService();
        service.ActiveItems.Add(PluginsFixtures.Plugin("com.example.on", state: PluginState.Active));
        service.DormantItems.Add(PluginsFixtures.Plugin("com.example.off"));
        var vm = Create(service);
        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.That(vm.CanActivate, Is.False);
        Assert.That(vm.CanDeactivate, Is.False);
        Assert.That(vm.CanReload, Is.False);
        Assert.That(vm.CanUnload, Is.False);
        Assert.That(vm.CanRefresh, Is.True);
        Assert.That(vm.CanAddNew, Is.True);

        vm.SelectedDormant = vm.DormantPlugins[0];

        Assert.That(vm.CanActivate, Is.True, "选中未激活栏才能激活");
        Assert.That(vm.CanDeactivate, Is.False);
        Assert.That(vm.CanReload, Is.True);
        Assert.That(vm.CanUnload, Is.True);
        Assert.That(vm.ActivateCommand.CanExecute(null), Is.True);
        Assert.That(vm.DeactivateCommand.CanExecute(null), Is.False);

        vm.SelectedActive = vm.ActivePlugins[0];

        Assert.That(vm.SelectedDormant, Is.Null, "两栏是单选语义：选左栏会清掉右栏选中");
        Assert.That(vm.CanDeactivate, Is.True);
        Assert.That(vm.CanActivate, Is.False);
        Assert.That(vm.CurrentSelection?.Id, Is.EqualTo("com.example.on"));
    }

    [Test]
    public async Task ActivateCommand_MovesRowToActiveColumnAndReportsStatus()
    {
        var service = new FakePluginService();
        service.DormantItems.Add(PluginsFixtures.Plugin("com.example.off"));
        var vm = Create(service);
        await vm.RefreshCommand.ExecuteAsync(null);
        vm.SelectedDormant = vm.DormantPlugins[0];

        await vm.ActivateCommand.ExecuteAsync(null);

        Assert.That(service.ActivatedIds, Is.EqualTo(new[] { "com.example.off" }));
        Assert.That(vm.ActivePlugins.Count, Is.EqualTo(1));
        Assert.That(vm.DormantPlugins, Is.Empty);
        Assert.That(vm.StatusText, Does.Contain("已激活插件"));
        Assert.That(vm.HasError, Is.False);
    }

    [Test]
    public async Task ActivateCommand_WhenServiceFails_ShowsChineseError()
    {
        var service = new FakePluginService
        {
            ActivateResult = PluginLoadResult.Fail(
                PluginLoadFailure.ActivationFailed,
                "插件「示例插件 - 1.0.0」初始化失败：初始化时炸了",
                "System.InvalidOperationException"),
        };
        service.DormantItems.Add(PluginsFixtures.Plugin("com.example.off"));
        var vm = Create(service);
        await vm.RefreshCommand.ExecuteAsync(null);
        vm.SelectedDormant = vm.DormantPlugins[0];

        await vm.ActivateCommand.ExecuteAsync(null);

        Assert.That(vm.HasError, Is.True);
        Assert.That(vm.ErrorText, Does.Contain("初始化失败"));
        Assert.That(vm.ErrorDetail, Is.EqualTo("System.InvalidOperationException"));
        Assert.That(vm.DormantPlugins, Has.Count.EqualTo(1), "激活失败留在右栏，用户可以重试");
    }

    [Test]
    public async Task DeactivateCommand_ReportsStatusInChinese()
    {
        var service = new FakePluginService();
        service.ActiveItems.Add(PluginsFixtures.Plugin("com.example.on", state: PluginState.Active));
        var vm = Create(service);
        await vm.RefreshCommand.ExecuteAsync(null);
        vm.SelectedActive = vm.ActivePlugins[0];

        await vm.DeactivateCommand.ExecuteAsync(null);

        Assert.That(service.DeactivatedIds, Is.EqualTo(new[] { "com.example.on" }));
        Assert.That(vm.DormantPlugins, Has.Count.EqualTo(1));
        Assert.That(vm.StatusText, Does.Contain("已停用插件"));
    }

    [Test]
    public async Task UnloadCommand_TellsUserDllStaysOnDisk()
    {
        var service = new FakePluginService();
        service.DormantItems.Add(PluginsFixtures.Plugin("com.example.off"));
        var vm = Create(service);
        await vm.RefreshCommand.ExecuteAsync(null);
        vm.SelectedDormant = vm.DormantPlugins[0];

        await vm.UnloadCommand.ExecuteAsync(null);

        Assert.That(service.UnloadedIds, Is.EqualTo(new[] { "com.example.off" }));
        Assert.That(vm.DormantPlugins, Is.Empty);
        Assert.That(vm.StatusText, Does.Contain("dll 仍在插件目录里"));
    }

    [Test]
    public async Task ReloadCommand_PassesSelectionId()
    {
        var service = new FakePluginService();
        service.ActiveItems.Add(PluginsFixtures.Plugin("com.example.on", state: PluginState.Active));
        var vm = Create(service);
        await vm.RefreshCommand.ExecuteAsync(null);
        vm.SelectedActive = vm.ActivePlugins[0];

        await vm.ReloadCommand.ExecuteAsync(null);

        Assert.That(service.ReloadedIds, Is.EqualTo(new[] { "com.example.on" }));
        Assert.That(vm.ActivePlugins, Has.Count.EqualTo(1), "重载保持激活状态");
    }

    [Test]
    public async Task AddNew_WithoutPicker_ExplainsWhyItCannotWork()
    {
        var vm = Create(new FakePluginService());
        await vm.RefreshCommand.ExecuteAsync(null);

        await vm.AddNewCommand.ExecuteAsync(null);

        Assert.That(vm.HasError, Is.True);
        Assert.That(vm.ErrorText, Does.Contain("文件选择器"));
    }

    [Test]
    public async Task AddNew_UsesPickerPathAndReportsLoadResult()
    {
        var service = new FakePluginService();
        var vm = Create(service);
        vm.FilePicker = new PluginsFakeFilePicker(@"C:Downloadshello.dll");
        await vm.RefreshCommand.ExecuteAsync(null);

        await vm.AddNewCommand.ExecuteAsync(null);

        Assert.That(service.AddedPaths, Is.EqualTo(new[] { @"C:Downloadshello.dll" }));
        Assert.That(vm.StatusText, Does.Contain("已加载插件"), "结果文案来自服务层的 PluginLoadResult.Message");
    }

    [Test]
    public async Task AddNew_WhenPickerCancelled_DoesNothing()
    {
        var service = new FakePluginService();
        var vm = Create(service);
        vm.FilePicker = new PluginsFakeFilePicker(null);
        await vm.RefreshCommand.ExecuteAsync(null);

        await vm.AddNewCommand.ExecuteAsync(null);

        Assert.That(service.AddedPaths, Is.Empty);
        Assert.That(vm.HasError, Is.False);
    }

    [Test]
    public async Task Refresh_Failure_ShowsErrorWithDetail()
    {
        var service = new FakePluginService
        {
            DiscoverResult = OperationResult<IReadOnlyList<PluginInfo>>.Fail(
                "读取插件目录失败：拒绝访问。",
                "System.UnauthorizedAccessException"),
        };
        var vm = Create(service);

        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.That(vm.ErrorText, Is.EqualTo("读取插件目录失败：拒绝访问。"));
        Assert.That(vm.ErrorDetail, Is.EqualTo("System.UnauthorizedAccessException"));

        vm.ClearErrorCommand.Execute(null);

        Assert.That(vm.HasError, Is.False);
        Assert.That(vm.HasErrorDetail, Is.False);
    }

    [Test]
    public async Task FailedDlls_AreListedAsChineseLines()
    {
        var service = new FakePluginService();
        service.FailureItems.Add(PluginsFixtures.Failed("broken", "「broken.dll」不是有效的 .NET 程序集。"));
        var vm = Create(service);

        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.That(vm.HasFailures, Is.True);
        Assert.That(vm.FailureTexts, Has.Count.EqualTo(1));
        Assert.That(vm.FailureTexts[0], Is.EqualTo("broken：「broken.dll」不是有效的 .NET 程序集。"));
    }

    [Test]
    public async Task Row_ShowsPlaceholdersForMissingMetadata()
    {
        var service = new FakePluginService();
        service.DormantItems.Add(PluginsFixtures.Plugin(
            "com.example.bare",
            name: "裸插件",
            author: string.Empty,
            description: string.Empty,
            minimumHostVersion: string.Empty));
        var vm = Create(service);

        await vm.RefreshCommand.ExecuteAsync(null);
        var row = vm.DormantPlugins[0];

        Assert.That(row.DisplayName, Is.EqualTo("裸插件 - 1.0.0"));
        Assert.That(row.AuthorText, Is.EqualTo("未署名"));
        Assert.That(row.DescriptionText, Is.EqualTo("（插件没有提供描述）"));
        Assert.That(row.PagesText, Is.EqualTo("不提供页签"));
        Assert.That(row.MinimumHostVersionText, Is.EqualTo("不限宿主版本"));
    }

    [Test]
    public async Task Row_CountsPagesAndShowsHostRequirement()
    {
        var service = new FakePluginService();
        service.DormantItems.Add(PluginsFixtures.Plugin(
            "com.example.pages",
            minimumHostVersion: "1.2.0",
            pages: new[]
            {
                new PluginPageDescriptor("hello", "示例页", "Example.HelloPageViewModel"),
                new PluginPageDescriptor("about", "关于示例", "Example.AboutPageViewModel"),
            }));
        var vm = Create(service);

        await vm.RefreshCommand.ExecuteAsync(null);
        var row = vm.DormantPlugins[0];

        Assert.That(row.PagesText, Is.EqualTo("提供 2 个页签"));
        Assert.That(row.MinimumHostVersionText, Is.EqualTo("要求宿主 1.2.0+"));
        Assert.That(row.AssemblyPath, Is.Not.Empty);
    }

    private static PluginsPageViewModel Create(FakePluginService service) =>
        new(service, new PluginsFakeErrorReporter());

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(10).ConfigureAwait(false);
        }

        Assert.Fail("等待条件超时。");
    }
}

/// <summary>插件 DTO 工厂。</summary>
internal static class PluginsFixtures
{
    /// <summary>造一个插件快照。</summary>
    public static PluginInfo Plugin(
        string id,
        string name = "示例插件",
        PluginState state = PluginState.Dormant,
        string author = "示例作者",
        string description = "示例描述。",
        string minimumHostVersion = "",
        IReadOnlyList<PluginPageDescriptor>? pages = null) =>
        new(
            Id: id,
            Name: name,
            Version: "1.0.0",
            Author: author,
            Description: description,
            MinimumHostVersion: minimumHostVersion,
            AssemblyPath: @"C:UsersmeAppDataLocalCKANplugins" + id + ".dll",
            State: state)
        {
            Pages = pages ?? Array.Empty<PluginPageDescriptor>(),
        };

    /// <summary>造一个加载失败的条目。</summary>
    public static PluginInfo Failed(string fileName, string reason) =>
        new(
            Id: string.Empty,
            Name: fileName,
            Version: string.Empty,
            Author: string.Empty,
            Description: string.Empty,
            MinimumHostVersion: string.Empty,
            AssemblyPath: @"C:UsersmeAppDataLocalCKANplugins" + fileName + ".dll",
            State: PluginState.Failed,
            FailureReason: reason);
}

/// <summary>假的插件服务：列表可编排，调用可记录。</summary>
internal sealed class FakePluginService : IPluginService
{
    /// <summary>左栏内容。</summary>
    public List<PluginInfo> ActiveItems { get; } = new();

    /// <summary>右栏内容。</summary>
    public List<PluginInfo> DormantItems { get; } = new();

    /// <summary>失败留痕。</summary>
    public List<PluginInfo> FailureItems { get; } = new();

    /// <summary>扫描结果（null = 成功）。</summary>
    public OperationResult<IReadOnlyList<PluginInfo>>? DiscoverResult { get; set; }

    /// <summary>激活结果（null = 成功）。</summary>
    public PluginLoadResult? ActivateResult { get; set; }

    /// <summary>重载结果（null = 成功）。</summary>
    public PluginLoadResult? ReloadResult { get; set; }

    /// <summary>新增结果（null = 成功）。</summary>
    public PluginLoadResult? AddResult { get; set; }

    /// <summary>停用结果（null = 成功）。</summary>
    public OperationResult? DeactivateResult { get; set; }

    /// <summary>卸载结果（null = 成功）。</summary>
    public OperationResult? UnloadResult { get; set; }

    /// <summary>扫描次数。</summary>
    public int DiscoverCalls { get; private set; }

    /// <summary>被激活的 Id。</summary>
    public List<string> ActivatedIds { get; } = new();

    /// <summary>被停用的 Id。</summary>
    public List<string> DeactivatedIds { get; } = new();

    /// <summary>被重载的 Id。</summary>
    public List<string> ReloadedIds { get; } = new();

    /// <summary>被卸载的 Id。</summary>
    public List<string> UnloadedIds { get; } = new();

    /// <summary>新增时传入的路径。</summary>
    public List<string> AddedPaths { get; } = new();

    /// <inheritdoc />
    public string PluginsDirectory => @"C:UsersmeAppDataLocalCKANplugins";

    /// <inheritdoc />
    public IReadOnlyList<PluginInfo> Active => ActiveItems;

    /// <inheritdoc />
    public IReadOnlyList<PluginInfo> Dormant => DormantItems;

    /// <inheritdoc />
    public IReadOnlyList<PluginInfo> Failures => FailureItems;

    /// <inheritdoc />
    public event EventHandler? Changed
    {
        add { }
        remove { }
    }

    /// <inheritdoc />
    public Task<OperationResult<IReadOnlyList<PluginInfo>>> DiscoverAsync(CancellationToken ct = default)
    {
        DiscoverCalls++;
        return Task.FromResult(
            DiscoverResult ?? OperationResult<IReadOnlyList<PluginInfo>>.Ok(ActiveItems.Concat(DormantItems).ToArray()));
    }

    /// <inheritdoc />
    public Task<PluginLoadResult> LoadAsync(string pluginPath, bool activate = false, CancellationToken ct = default) =>
        Task.FromResult(AddResult ?? PluginLoadResult.Ok(PluginsFixtures.Plugin("com.example.loaded"), "已加载插件。"));

    /// <inheritdoc />
    public Task<PluginLoadResult> ActivateAsync(string pluginId, CancellationToken ct = default)
    {
        ActivatedIds.Add(pluginId);
        if (ActivateResult is { IsSuccess: false } failed)
        {
            return Task.FromResult(failed);
        }

        Move(pluginId, from: DormantItems, to: ActiveItems, PluginState.Active);
        return Task.FromResult(PluginLoadResult.Ok(PluginsFixtures.Plugin(pluginId, state: PluginState.Active), "已激活插件。"));
    }

    /// <inheritdoc />
    public Task<OperationResult> DeactivateAsync(string pluginId, CancellationToken ct = default)
    {
        DeactivatedIds.Add(pluginId);
        if (DeactivateResult is { IsSuccess: false } failed)
        {
            return Task.FromResult(failed);
        }

        Move(pluginId, from: ActiveItems, to: DormantItems, PluginState.Dormant);
        return Task.FromResult(OperationResult.Ok());
    }

    /// <inheritdoc />
    public Task<PluginLoadResult> ReloadAsync(string pluginId, CancellationToken ct = default)
    {
        ReloadedIds.Add(pluginId);
        return Task.FromResult(
            ReloadResult ?? PluginLoadResult.Ok(PluginsFixtures.Plugin(pluginId, state: PluginState.Active), "已重载插件。"));
    }

    /// <inheritdoc />
    public Task<OperationResult> UnloadAsync(string pluginId, CancellationToken ct = default)
    {
        UnloadedIds.Add(pluginId);
        if (UnloadResult is { IsSuccess: false } failed)
        {
            return Task.FromResult(failed);
        }

        ActiveItems.RemoveAll(info => info.Id == pluginId);
        DormantItems.RemoveAll(info => info.Id == pluginId);
        return Task.FromResult(OperationResult.Ok());
    }

    /// <inheritdoc />
    public Task<PluginLoadResult> AddFromPathAsync(string sourcePath, bool activate = false, CancellationToken ct = default)
    {
        AddedPaths.Add(sourcePath);
        if (AddResult is not null)
        {
            return Task.FromResult(AddResult);
        }

        DormantItems.Add(PluginsFixtures.Plugin("com.example.added"));
        return Task.FromResult(
            PluginLoadResult.Ok(PluginsFixtures.Plugin("com.example.added"), "已加载插件「示例插件 - 1.0.0」（未激活）。"));
    }

    private static void Move(string pluginId, List<PluginInfo> from, List<PluginInfo> to, PluginState state)
    {
        var index = from.FindIndex(info => string.Equals(info.Id, pluginId, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            return;
        }

        var moved = from[index] with { State = state };
        from.RemoveAt(index);
        to.Add(moved);
    }
}

/// <summary>假的选择器：返回预设路径（null = 用户取消）。</summary>
internal sealed class PluginsFakeFilePicker : IPluginFilePicker
{
    private readonly string? _path;

    public PluginsFakeFilePicker(string? path) => _path = path;

    public Task<string?> PickPluginFileAsync(CancellationToken ct = default) => Task.FromResult(_path);
}

/// <summary>假的错误上报：只记录。</summary>
internal sealed class PluginsFakeErrorReporter : IErrorReporter
{
    /// <summary>上报上下文。</summary>
    public List<string> Contexts { get; } = new();

    public event Action<ErrorEntry>? Reported
    {
        add { }
        remove { }
    }

    public IReadOnlyList<ErrorEntry> Recent => Array.Empty<ErrorEntry>();

    public void Report(Exception exception, string context, string? userMessage = null) => Contexts.Add(context);

    public void Report(string context, string userMessage, string? detail = null) => Contexts.Add(context);
}
