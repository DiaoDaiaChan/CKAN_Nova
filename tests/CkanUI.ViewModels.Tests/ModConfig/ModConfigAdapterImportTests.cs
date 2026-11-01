using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.ModConfig;
using CkanUI.ViewModels.Pages;
using NUnit.Framework;

namespace CkanUI.ViewModels.Tests.ModConfig;

/// <summary>
/// adapter 导入入口（ADP-04）：页面只负责"挑包 → 交给 IAdapterInstaller → 热重扫目录 → 报状态"，
/// 校验、解包、原子替换、清单与哈希全在服务层（Services 侧 AdapterInstallerTests 覆盖）。
/// </summary>
/// <remarks>
/// 用假 IAdapterInstaller / IAdapterCatalog：页面 VM 不碰文件、不碰网络，也不认识实现类型。
/// </remarks>
[TestFixture]
public sealed class ModConfigAdapterImportTests
{
    [Test]
    public void WithoutInstaller_HasNoImportEntry()
    {
        var harness = Create(withInstaller: false);

        Assert.That(harness.Vm.CanImportAdapter, Is.False);
        Assert.That(harness.Vm.ImportAdapterCommand.CanExecute(null), Is.False);
    }

    [Test]
    public void WithoutPicker_HasNoImportEntry()
    {
        var harness = Create(withPicker: false);

        Assert.That(harness.Vm.CanImportAdapter, Is.False);
    }

    [Test]
    public void WithInstallerAndPicker_ShowsImportEntry()
    {
        var harness = Create();

        Assert.That(harness.Vm.CanImportAdapter, Is.True);
        Assert.That(harness.Vm.ImportAdapterCommand.CanExecute(null), Is.True);
    }

    [Test]
    public async Task CancelledPicker_DoesNotInstall()
    {
        var harness = Create(pickedPath: null);

        await harness.Vm.ImportAdapterCommand.ExecuteAsync(null);

        Assert.That(harness.Installer.Calls, Is.Empty);
        Assert.That(harness.Vm.StatusText, Does.Not.Contain("已导入"));
    }

    [Test]
    public async Task InstalledPackage_ReloadsCatalogAndReportsStatus()
    {
        var harness = Create(pickedPath: "/tmp/third-party-adapter.zip");

        await harness.Vm.ImportAdapterCommand.ExecuteAsync(null);

        Assert.That(harness.Installer.Calls, Has.Count.EqualTo(1));
        Assert.That(harness.Installer.Calls[0].Kind, Is.EqualTo(AdapterPackageSourceKind.LocalFile));
        Assert.That(harness.Installer.Calls[0].Location, Is.EqualTo("/tmp/third-party-adapter.zip"));
        Assert.That(harness.Catalog.Reloads, Is.EqualTo(1));
        Assert.That(harness.Vm.StatusText, Is.EqualTo("已导入 adapter「第三方适配器」（sha256 abcdef123456）。"));
        Assert.That(harness.Vm.HasError, Is.False);
    }

    [Test]
    public async Task ReplacedPackage_SaysUpdated()
    {
        var harness = Create(pickedPath: "/tmp/third-party-adapter.json");
        harness.Installer.Replaced = true;

        await harness.Vm.ImportAdapterCommand.ExecuteAsync(null);

        Assert.That(harness.Vm.StatusText, Does.StartWith("已更新 adapter「"));
        Assert.That(harness.Catalog.Reloads, Is.EqualTo(1));
    }

    [Test]
    public async Task FailedInstall_ShowsErrorWithDiagnosticCodeAndKeepsCatalogUntouched()
    {
        var harness = Create(pickedPath: "/tmp/broken.zip");
        harness.Installer.Result = null;

        await harness.Vm.ImportAdapterCommand.ExecuteAsync(null);

        Assert.That(harness.Vm.HasError, Is.True);
        Assert.That(harness.Vm.ErrorText, Does.Contain("adapter-schema-invalid"));
        Assert.That(harness.Catalog.Reloads, Is.Zero);
    }

    [Test]
    public async Task InstallerThrows_IsReportedInsteadOfCrashing()
    {
        var harness = Create(pickedPath: "/tmp/broken.zip");
        harness.Installer.Throws = true;

        await harness.Vm.ImportAdapterCommand.ExecuteAsync(null);

        Assert.That(harness.Vm.HasError, Is.True);
        Assert.That(harness.Vm.ErrorText, Does.Contain("磁盘炸了"));
        Assert.That(harness.Vm.IsBusy, Is.False);
    }

    [Test]
    public async Task PickerThrows_IsReportedInsteadOfCrashing()
    {
        var harness = Create(withPicker: false);
        harness.Vm.AdapterPackagePicker = () => throw new InvalidOperationException("没有文件对话框");
        harness.Vm.AdapterInstaller = harness.Installer;

        await harness.Vm.ImportAdapterCommand.ExecuteAsync(null);

        Assert.That(harness.Vm.HasError, Is.True);
        Assert.That(harness.Vm.ErrorText, Does.Contain("没有文件对话框"));
    }

    private static Harness Create(
        string? pickedPath = "/tmp/adapter.json",
        bool withInstaller = true,
        bool withPicker = true)
    {
        var model = ModConfigTestData.Model(mods: [ModConfigTestData.Mod()]);
        var catalog = new RecordingCatalog();
        var installer = new RecordingInstaller();
        var vm = new ModConfigPageViewModel(new StubConfigService(model), catalog)
        {
            AdapterInstaller = withInstaller ? installer : null,
            AdapterPackagePicker = withPicker ? () => Task.FromResult<string?>(pickedPath) : null,
        };
        vm.ApplyModel(model);
        return new Harness(vm, installer, catalog);
    }

    private sealed class Harness
    {
        public Harness(ModConfigPageViewModel vm, RecordingInstaller installer, RecordingCatalog catalog)
        {
            Vm = vm;
            Installer = installer;
            Catalog = catalog;
        }

        public ModConfigPageViewModel Vm { get; }

        public RecordingInstaller Installer { get; }

        public RecordingCatalog Catalog { get; }
    }

    private sealed class StubConfigService(ModConfigInstanceModel model) : IModConfigService
    {
        public Task<OperationResult<ModConfigInstanceModel>> LoadAsync(string instanceName, bool forceReload = false, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult<ModConfigInstanceModel>.Ok(model));
    }

    private sealed class RecordingCatalog : IAdapterCatalog
    {
        public IReadOnlyList<ModConfigAdapterDescription> Adapters => [];

        public IReadOnlyList<ModConfigDiagnostic> Diagnostics => [];

        public int Reloads { get; private set; }

        public void Reload() => Reloads++;
    }

    private sealed class RecordingInstaller : IAdapterInstaller
    {
        public string AdapterRootDirectory => "/tmp/adapters";

        public List<AdapterPackageSource> Calls { get; } = [];

        /// <summary>安装结果；null 表示失败（带诊断码）。</summary>
        public AdapterInstallResult? Result { get; set; } = Sample();

        public bool Replaced { get; set; }

        public bool Throws { get; set; }

        public Task<OperationResult<AdapterInstallResult>> InstallAsync(
            AdapterPackageSource source,
            CancellationToken cancellationToken = default)
        {
            Calls.Add(source);
            if (Throws)
            {
                throw new IOException("磁盘炸了。");
            }

            if (Result is not { } result)
            {
                return Task.FromResult(OperationResult<AdapterInstallResult>.Fail(
                    "adapter 包里的 adapter.json 校验不通过。",
                    "adapter-schema-invalid"));
            }

            return Task.FromResult(OperationResult<AdapterInstallResult>.Ok(
                result with { ReplacedExisting = Replaced }));
        }

        public Task<OperationResult<AdapterInstallManifest>> GetInstalledAsync(
            string adapterId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult<AdapterInstallManifest>.Fail("未安装。", "adapter-not-installed"));

        public Task<OperationResult<AdapterInstalledIndex>> ListInstalledAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult<AdapterInstalledIndex>.Ok(new AdapterInstalledIndex([])));

        public Task<OperationResult> UninstallAsync(string adapterId, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult.Ok());

        public Task<OperationResult<AdapterUpdateCheckResult>> CheckForUpdateAsync(
            string adapterId,
            Uri remoteUri,
            string? etag = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult<AdapterUpdateCheckResult>.Fail("测试不查更新。", "no-network"));

        private static AdapterInstallResult Sample()
        {
            var manifest = new AdapterInstallManifest(
                Id: "third-party",
                Name: "第三方适配器",
                SchemaVersion: 1,
                Source: "/tmp/adapter.json",
                SourceKind: AdapterPackageSourceKind.LocalFile,
                Sha256: "abcdef1234567890abcdef1234567890abcdef1234567890abcdef1234567890",
                SizeBytes: 1024,
                InstalledAt: new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.Zero));

            return new AdapterInstallResult(
                manifest,
                "/tmp/adapters/third-party",
                new AdapterHashInfo(manifest.Sha256, manifest.SizeBytes),
                AdapterPackageFormat.Json,
                ReplacedExisting: false);
        }
    }
}
