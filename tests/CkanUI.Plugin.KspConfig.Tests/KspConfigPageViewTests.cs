using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.KspConfig;
using NUnit.Framework;

namespace CkanUI.Plugin.KspConfig.Tests;

/// <summary>
/// 「KSP 游戏设置」页签的插件契约：页签描述符、VM→View 命名约定、XAML 可加载。
/// </summary>
/// <remarks>
/// 这几条用例原本在 <c>tests/CkanUI.App.Tests</c>，断言的是宿主 <c>ViewLocator</c> / <c>ServiceRegistration.PageTypes</c> /
/// DI 解析。页面搬进插件后宿主不再注册它（宿主接线由 App 工程负责），所以这里改成钉插件自己必须保证的事：
/// 页签描述符字段（键/标题键/字形/分节/顺序/VM 类型名）、视图与 VM 同程序集同命名空间的命名约定，
/// 以及 XAML 在无头宿主里真的能实例化——x:Class 或 xmlns 还指着宿主命名空间时，会在这里红，
/// 而不是等导航时被 ViewLocator 静默回退成占位视图。
/// </remarks>
[TestFixture]
public sealed class KspConfigPageViewTests
{
    private static bool _headlessInitialized;
    private static readonly object HeadlessLock = new();

    private sealed class FakeKspConfigService : IKspConfigService
    {
        public Task<KspConfigDocument> LoadConfigAsync(string? instancePath = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new KspConfigDocument
            {
                FilePath = "settings.cfg",
                Settings = [],
                RawLines = [],
                LocalizationTable = new Dictionary<string, string>()
            });

        public Task<bool> SaveConfigAsync(KspConfigDocument document, IEnumerable<KspSettingItem> modifiedSettings, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<IReadOnlyDictionary<string, string>> LoadOfficialLocalizationAsync(string? instancePath = null, string? language = null, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>());

        public Task<bool> RestoreBackupAsync(string? instancePath = null, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public string? GetBackupPath(string? instancePath = null) => null;
    }

    /// <summary>
    /// 起一个无头 Avalonia 运行时：视图的 <c>InitializeComponent()</c> 要真的解析 XAML，才能证明搬家没把 x:Class 写坏。
    /// </summary>
    private static void EnsureHeadless()
    {
        if (_headlessInitialized)
        {
            return;
        }

        lock (HeadlessLock)
        {
            if (_headlessInitialized)
            {
                return;
            }

            // TestRunner 单进程跑全部程序集：别的测试宿主可能已经建好无头壳，
            // 第二次 SetupWithoutStarting 会抛「Setup was already called on one of AppBuilder instances」。
            if (Application.Current is null)
            {
                AppBuilder.Configure<Application>()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions())
                    .SetupWithoutStarting();
            }

            _headlessInitialized = true;
        }
    }

    [Test]
    public void PluginPages_DeclareKspConfigTab()
    {
        var plugin = new KspConfigPlugin();

        Assert.That(plugin.Pages, Has.Count.EqualTo(1));

        var page = plugin.Pages[0];
        Assert.That(page.Key, Is.EqualTo("kspconfig"));
        Assert.That(page.ViewModelTypeName, Is.EqualTo(typeof(KspConfigPageViewModel).FullName));
        Assert.That(page.TitleKey, Is.EqualTo("Ui_NavRail_KspConfig"));
        Assert.That(page.Glyph, Is.EqualTo("Settings"));
        Assert.That(page.Section, Is.EqualTo("instances"));
        Assert.That(page.Order, Is.EqualTo(50));
    }

    [Test]
    public void ViewLocator_ResolvesKspConfigPageViewModel_ToKspConfigPageView()
    {
        var viewModelType = typeof(KspConfigPageViewModel);
        var viewType = typeof(KspConfigPageView);

        // 宿主 ViewLocator 的插件规则：在视图模型自己的程序集里，把全名的 ViewModel 换成 View。
        Assert.That(viewType.Assembly, Is.SameAs(viewModelType.Assembly));
        Assert.That(viewType.Namespace, Is.EqualTo(viewModelType.Namespace));
        Assert.That(
            viewType.FullName,
            Is.EqualTo(viewModelType.FullName!.Replace("ViewModel", "View", StringComparison.Ordinal)));
        Assert.That(typeof(UserControl).IsAssignableFrom(viewType), Is.True);
        Assert.That(viewType.GetConstructor(Type.EmptyTypes), Is.Not.Null);
    }

    [Test]
    public void ViewModel_ImplementsPluginPageMarker()
    {
        Assert.That(typeof(IPluginPage).IsAssignableFrom(typeof(KspConfigPageViewModel)), Is.True);
    }

    [Test]
    public void View_LoadsXaml_AndAcceptsViewModelDataContext()
    {
        EnsureHeadless();

        var viewModel = new KspConfigPageViewModel(new FakeKspConfigService());
        var view = new KspConfigPageView { DataContext = viewModel };

        Assert.That(view.DataContext, Is.SameAs(viewModel));
        Assert.That(view.Content, Is.Not.Null, "XAML 没加载出来：检查 x:Class 与 xmlns 是否还指向宿主命名空间。");
    }
}
