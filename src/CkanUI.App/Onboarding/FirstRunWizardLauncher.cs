using Avalonia.Controls;
using CkanUI.App.Views;
using CkanUI.App.Views.Dialogs;
using CkanUI.Services.Abstractions;
using CkanUI.ViewModels.Dialogs;
using CkanUI.ViewModels.Pages;

namespace CkanUI.App.Onboarding;

/// <summary>
/// 首次启动指引的 App 实现：把 DI 里的服务装进 VM，再用主窗口做 owner 弹模态。
/// </summary>
/// <remarks>
/// 启动时的自动弹出与「设置 → 首次启动指引」的手动重入共用这一条路径 ——
/// 两处各写一遍装配，迟早会出现「首启能扫到 Steam 库、重入扫不到」的漂移。
/// </remarks>
public sealed class FirstRunWizardLauncher : IFirstRunWizardLauncher
{
    private readonly Func<Window?> _ownerProvider;
    private readonly IInstanceService _instances;
    private readonly IKspInstallScanner _scanner;
    private readonly IProxyConfigurator _proxy;
    private readonly ICompatibleVersionsService _versions;
    private readonly IRepositoryService _repositories;
    private readonly IChangeSetService _changeSets;
    private readonly IInstallerService _installer;
    private readonly IUiSettingsStore _uiSettings;
    private readonly Func<string, string> _translate;

    /// <summary>构造启动器。</summary>
    /// <param name="ownerProvider">返回当前主窗口，用于模态 owner。</param>
    /// <param name="instances">实例服务。</param>
    /// <param name="scanner">安装目录扫描器。</param>
    /// <param name="proxy">下载代理装配器。</param>
    /// <param name="versions">兼容版本服务。</param>
    /// <param name="repositories">元数据索引服务。</param>
    /// <param name="changeSets">变更集构建服务。</param>
    /// <param name="installer">安装执行服务。</param>
    /// <param name="uiSettings">UI 设置存储。</param>
    /// <param name="translate">文案解析器（本地化器的 Get）。</param>
    public FirstRunWizardLauncher(
        Func<Window?> ownerProvider,
        IInstanceService instances,
        IKspInstallScanner scanner,
        IProxyConfigurator proxy,
        ICompatibleVersionsService versions,
        IRepositoryService repositories,
        IChangeSetService changeSets,
        IInstallerService installer,
        IUiSettingsStore uiSettings,
        Func<string, string> translate)
    {
        _ownerProvider = ownerProvider ?? throw new ArgumentNullException(nameof(ownerProvider));
        _instances = instances ?? throw new ArgumentNullException(nameof(instances));
        _scanner = scanner ?? throw new ArgumentNullException(nameof(scanner));
        _proxy = proxy ?? throw new ArgumentNullException(nameof(proxy));
        _versions = versions ?? throw new ArgumentNullException(nameof(versions));
        _repositories = repositories ?? throw new ArgumentNullException(nameof(repositories));
        _changeSets = changeSets ?? throw new ArgumentNullException(nameof(changeSets));
        _installer = installer ?? throw new ArgumentNullException(nameof(installer));
        _uiSettings = uiSettings ?? throw new ArgumentNullException(nameof(uiSettings));
        _translate = translate ?? throw new ArgumentNullException(nameof(translate));
    }

    /// <inheritdoc />
    public async Task LaunchAsync()
    {
        if (_ownerProvider() is not { } owner)
        {
            return;
        }

        var viewModel = new FirstRunWizardViewModel(
            _instances,
            _scanner,
            _proxy,
            _versions,
            _repositories,
            _changeSets,
            _installer,
            _uiSettings)
        {
            // Translate 与 FolderPicker 必须先于 DataContext 挂上：绑定时就要拿到译文与选择器。
            Translate = _translate,
            FolderPicker = new AvaloniaInstanceFolderPicker(owner),
        };

        var view = new FirstRunWizardView { DataContext = viewModel };
        await view.ShowDialog(owner).ConfigureAwait(true);
    }
}
