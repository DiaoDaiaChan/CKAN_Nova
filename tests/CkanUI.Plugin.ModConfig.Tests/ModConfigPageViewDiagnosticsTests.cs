using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.ModConfig;
using CkanUI.Plugin.ModConfig;
using NUnit.Framework;

namespace CkanUI.Plugin.ModConfig.Tests;

/// <summary>
/// 页面级诊断条的真渲染核对：光有 VM 属性不够，要证明它真的在可视树上、
/// 真的带出了目录级诊断原文（`adapter-dir-missing` 是"装完没适配器"的唯一线索）。
/// </summary>
[TestFixture]
public sealed class ModConfigPageViewDiagnosticsTests
{
    [Test]
    public void PageDiagnosticsBanner_IsRenderedWithTheCatalogMessage()
    {
        if (!EnsureHost())
        {
            Assert.Ignore("当前线程无法创建无头窗口（宿主已在本进程其它线程建过壳）。");
        }

        var model = ModConfigTestData.Model(mods: [], adapterlessModCount: 0);
        model = new ModConfigInstanceModel(
            InstanceName: model.InstanceName,
            GameDir: model.GameDir,
            GameVersion: model.GameVersion,
            IsGameRunning: false,
            Mods: [],
            AdapterlessModCount: 0,
            Diagnostics: [ModConfigDiagnostic.Warning("adapter-dir-missing", "内置 adapter 目录不存在：/app/adapters（适配器可能没随包复制）")]);
        var config = new StubConfigService(model);
        var vm = new ModConfigPageViewModel(config);
        vm.ApplyModel(model);

        var view = new ModConfigPageView { DataContext = vm };
        var window = new Window { Width = 1200, Height = 720, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        view.Measure(new Size(1200, 720));
        view.Arrange(new Rect(0, 0, 1200, 720));
        Dispatcher.UIThread.RunJobs();

        Assert.That(vm.HasPageDiagnostics, Is.True);
        Assert.That(vm.PageDiagnosticsTitle, Is.EqualTo("配置适配器加载时发现问题："));
        var texts = view.GetVisualDescendants().OfType<TextBlock>().Select(block => block.Text ?? string.Empty).ToList();
        Assert.That(texts, Does.Contain(vm.PageDiagnosticsTitle), "诊断条的标题必须真的在可视树上");
        Assert.That(
            texts.Any(text => text.Contains("内置 adapter 目录不存在", StringComparison.Ordinal)),
            Is.True,
            "目录级诊断原文必须渲染出来，否则用户装完看到空列表却不知原因");
        window.Close();
    }

    private static bool EnsureHost()
    {
        if (Application.Current is not null)
        {
            return Dispatcher.UIThread.CheckAccess();
        }

        AppBuilder.Configure<HeadlessApplication>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions())
            .SetupWithoutStarting();
        if (!Application.Current!.Styles.OfType<FluentTheme>().Any())
        {
            Application.Current.Styles.Add(new FluentTheme());
        }

        return true;
    }

    private sealed class HeadlessApplication : Application
    {
    }

    private sealed class StubConfigService : IModConfigService
    {
        private readonly ModConfigInstanceModel _model;

        public StubConfigService(ModConfigInstanceModel model)
        {
            _model = model;
            Current = model;
        }

        public ModConfigInstanceModel? Current { get; set; }

        public Task<OperationResult<ModConfigInstanceModel>> LoadAsync(
            string instanceName,
            bool forceReload = false,
            CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult<ModConfigInstanceModel>.Ok(_model));
    }
}
