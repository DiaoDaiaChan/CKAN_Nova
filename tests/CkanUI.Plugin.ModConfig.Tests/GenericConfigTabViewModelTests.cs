using System.ComponentModel;
using CkanUI.Plugin.ModConfig;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.ModConfig;
using NUnit.Framework;

namespace CkanUI.Plugin.ModConfig.Tests;

[TestFixture]
public sealed class GenericConfigTabViewModelTests
{
    private sealed class FakeGenericConfigEditService : IGenericConfigEditService
    {
        public List<GenericConfigFile> DiscoveredFiles { get; set; } = [];
        public List<GenericConfigFile> WideFiles { get; set; } = [];
        public int HiddenWideCandidateCount { get; set; }
        public int DiscoverCalls { get; private set; }
        public GenericConfigScanOptions? LastOptions { get; private set; }
        public Dictionary<string, GenericConfigDocumentView> Documents { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public List<GenericConfigChange> LastSavedChanges { get; } = [];
        public bool ThrowOnSave { get; set; }

        public Task<OperationResult<GenericConfigCatalog>> DiscoverAsync(
            string gameDir,
            string instanceName,
            CancellationToken cancellationToken = default)
            => DiscoverAsync(gameDir, instanceName, GenericConfigScanOptions.Automatic, cancellationToken);

        public Task<OperationResult<GenericConfigCatalog>> DiscoverAsync(
            string gameDir,
            string instanceName,
            GenericConfigScanOptions options,
            CancellationToken cancellationToken = default)
        {
            DiscoverCalls++;
            LastOptions = options;
            var wide = options.Scope == GenericConfigScanScope.Wide;
            var files = wide ? WideFiles : DiscoveredFiles;
            var hidden = wide ? 0 : HiddenWideCandidateCount;
            return Task.FromResult(OperationResult<GenericConfigCatalog>.Ok(
                new GenericConfigCatalog(gameDir, files, 0, [], hidden)));
        }

        public Task<OperationResult<GenericConfigDocumentView>> LoadAsync(
            string gameDir,
            string filePath,
            CancellationToken cancellationToken = default)
        {
            if (Documents.TryGetValue(filePath, out var doc))
            {
                return Task.FromResult(OperationResult<GenericConfigDocumentView>.Ok(doc));
            }

            return Task.FromResult(OperationResult<GenericConfigDocumentView>.Fail("File not found"));
        }

        public Task<OperationResult<GenericConfigSaveResult>> SaveAsync(
            string gameDir,
            string instanceName,
            string filePath,
            IReadOnlyList<GenericConfigChange> changes,
            bool confirmedGameRunning,
            CancellationToken cancellationToken = default)
        {
            if (ThrowOnSave)
            {
                return Task.FromResult(OperationResult<GenericConfigSaveResult>.Fail("Save error"));
            }

            LastSavedChanges.Clear();
            LastSavedChanges.AddRange(changes);
            return Task.FromResult(OperationResult<GenericConfigSaveResult>.Ok(
                new GenericConfigSaveResult(filePath, filePath + ".bak", changes.Count, false)));
        }
    }

    [Test]
    public void RowViewModel_IdentifiesBooleanAndDetectsModification()
    {
        var boolRow = new GenericConfigRow(10, 1, "SETTINGS", "enableFeature", "true", "toggle feature");
        var vm = new GenericConfigRowViewModel(boolRow, canEdit: true);

        Assert.That(vm.IsBoolean, Is.True);
        Assert.That(vm.IsChecked, Is.True);
        Assert.That(vm.IsModified, Is.False);
        Assert.That(vm.HasComment, Is.True);
        Assert.That(vm.Comment, Is.EqualTo("toggle feature"));

        vm.IsChecked = false;
        Assert.That(vm.IsModified, Is.True);
        Assert.That(vm.CurrentValue, Is.EqualTo("false"));

        vm.Revert();
        Assert.That(vm.IsModified, Is.False);
        Assert.That(vm.IsChecked, Is.True);
        Assert.That(vm.CurrentValue, Is.EqualTo("true"));
    }

    [Test]
    public void RowViewModel_TextRowModificationAndRevert()
    {
        var textRow = new GenericConfigRow(20, 0, "", "maxPayload", "5000", null);
        var vm = new GenericConfigRowViewModel(textRow, canEdit: true);

        Assert.That(vm.IsBoolean, Is.False);
        Assert.That(vm.HasComment, Is.False);
        Assert.That(vm.IsModified, Is.False);

        vm.ValueText = "8000";
        Assert.That(vm.IsModified, Is.True);
        Assert.That(vm.CurrentValue, Is.EqualTo("8000"));

        vm.Revert();
        Assert.That(vm.IsModified, Is.False);
        Assert.That(vm.ValueText, Is.EqualTo("5000"));
    }

    [Test]
    public async Task TabViewModel_Scan_LoadsFilesAndFiltersBySearchText()
    {
        var fake = new FakeGenericConfigEditService
        {
            DiscoveredFiles =
            [
                new GenericConfigFile("C:/KSP/GameData/ModA/settings.cfg", "ModA/settings.cfg", "ModA", "Settings", "SETTINGS", 5, 200, DateTime.UtcNow, true, null),
                new GenericConfigFile("C:/KSP/GameData/ModB/config.cfg", "ModB/config.cfg", "ModB", "Config", "CONFIG", 3, 150, DateTime.UtcNow, false, "Read-only")
            ]
        };

        var tab = new GenericConfigTabViewModel(fake);
        tab.SetContext("C:/KSP/GameData", "TestInstance");

        await tab.RefreshAsync();

        Assert.That(tab.Files.Count, Is.EqualTo(2));
        Assert.That(tab.FilteredFiles.Count, Is.EqualTo(2));

        tab.SearchText = "ModA";
        Assert.That(tab.FilteredFiles.Count, Is.EqualTo(1));
        Assert.That(tab.FilteredFiles[0].ModName, Is.EqualTo("ModA"));

        tab.SearchText = "NonExistent";
        Assert.That(tab.FilteredFiles.Count, Is.EqualTo(0));

        tab.SearchText = "";
        Assert.That(tab.FilteredFiles.Count, Is.EqualTo(2));
    }

    [Test]
    public async Task TabViewModel_SelectFile_LoadsRowsAndAllowsSaving()
    {
        var path = "C:/KSP/GameData/ModA/settings.cfg";
        var fake = new FakeGenericConfigEditService
        {
            DiscoveredFiles =
            [
                new GenericConfigFile(path, "ModA/settings.cfg", "ModA", "Settings", "SETTINGS", 2, 200, DateTime.UtcNow, true, null)
            ],
            Documents =
            {
                [path] = new GenericConfigDocumentView(
                    path,
                    "ModA/settings.cfg",
                    CanWrite: true,
                    ReadonlyReason: null,
                    Rows:
                    [
                        new GenericConfigRow(1, 0, "", "name", "MyMod", null),
                        new GenericConfigRow(2, 0, "", "enabled", "false", null)
                    ],
                    ParseErrorCount: 0)
            }
        };

        var tab = new GenericConfigTabViewModel(fake);
        tab.SetContext("C:/KSP/GameData", "TestInstance");
        await tab.RefreshAsync();

        tab.SelectedFile = tab.Files[0];
        // Wait for async load triggered by SelectedFile change
        for (var i = 0; i < 20 && tab.Rows.Count == 0; i++)
        {
            await Task.Delay(20);
        }

        Assert.That(tab.HasSelectedFile, Is.True);
        Assert.That(tab.Rows.Count, Is.EqualTo(2));
        Assert.That(tab.IsDirty, Is.False);
        Assert.That(tab.CanSave, Is.False);

        // Edit row 2
        tab.Rows[1].IsChecked = true;
        Assert.That(tab.IsDirty, Is.True);
        Assert.That(tab.CanSave, Is.True);
        Assert.That(tab.PendingChangeCount, Is.EqualTo(1));

        // Save
        await tab.SaveCommand.ExecuteAsync(null);

        Assert.That(fake.LastSavedChanges.Count, Is.EqualTo(1));
        Assert.That(fake.LastSavedChanges[0].LineNumber, Is.EqualTo(2));
        Assert.That(fake.LastSavedChanges[0].NewValue, Is.EqualTo("true"));
    }

    [Test]
    public async Task TabViewModel_ReportsHiddenWideCandidatesFromTheAutomaticScan()
    {
        var visible = new GenericConfigFile(
            "C:/KSP/GameData/ModA/settings.cfg", "ModA/settings.cfg", "ModA", "Settings", "SETTINGS", 5, 200, DateTime.UtcNow, true, null);
        var fake = new FakeGenericConfigEditService
        {
            DiscoveredFiles = [visible],
            WideFiles = [visible],
            HiddenWideCandidateCount = 4
        };

        var tab = new GenericConfigTabViewModel(fake);
        tab.SetContext("C:/KSP/GameData", "TestInstance");
        await tab.RefreshAsync();

        Assert.Multiple(() =>
        {
            Assert.That(fake.LastOptions?.Scope, Is.EqualTo(GenericConfigScanScope.Automatic));
            Assert.That(tab.Files, Has.Count.EqualTo(1));
            Assert.That(tab.HasHiddenWideCandidates, Is.True);
            Assert.That(tab.FileCountText, Does.Contain("另有 4 个候选可宽松查看"));
            Assert.That(tab.HiddenWideCountText, Does.Contain("还有 4 个候选 cfg 没有列出"));
            Assert.That(tab.WideScan, Is.False, "保守模式扫到东西就不该自作主张扩大范围");
        });
    }

    [Test]
    public async Task TabViewModel_WideScanIsAnExplicitChoiceAndSilencesTheHint()
    {
        var narrow = new GenericConfigFile(
            "C:/KSP/GameData/ModA/settings.cfg", "ModA/settings.cfg", "ModA", "Settings", "SETTINGS", 5, 200, DateTime.UtcNow, true, null);
        var extra = new GenericConfigFile(
            "C:/KSP/GameData/ModB/Config/custom.cfg", "ModB/Config/custom.cfg", "ModB", "Custom", "MODB_SETTINGS", 3, 120, DateTime.UtcNow, true, null);
        var fake = new FakeGenericConfigEditService
        {
            DiscoveredFiles = [narrow],
            WideFiles = [narrow, extra],
            HiddenWideCandidateCount = 4
        };

        var tab = new GenericConfigTabViewModel(fake) { WideScan = true };
        tab.SetContext("C:/KSP/GameData", "TestInstance");
        await tab.RefreshAsync();

        Assert.Multiple(() =>
        {
            Assert.That(fake.LastOptions?.Scope, Is.EqualTo(GenericConfigScanScope.Wide));
            Assert.That(tab.Files, Has.Count.EqualTo(2));
            Assert.That(tab.HasHiddenWideCandidates, Is.False);
            Assert.That(tab.HiddenWideCountText, Is.Empty);
            Assert.That(tab.FileCountText, Does.Not.Contain("另有"));
            Assert.That(tab.StatusText, Does.Contain("宽松扫描已开启"));
        });
    }

    [Test]
    public async Task TabViewModel_FallsBackToWideScanWhenNothingWasListed()
    {
        var extra = new GenericConfigFile(
            "C:/KSP/GameData/ModB/Config/custom.cfg", "ModB/Config/custom.cfg", "ModB", "Custom", "MODB_SETTINGS", 3, 120, DateTime.UtcNow, true, null);
        var fake = new FakeGenericConfigEditService
        {
            DiscoveredFiles = [],
            WideFiles = [extra],
            HiddenWideCandidateCount = 2
        };

        var tab = new GenericConfigTabViewModel(fake);
        tab.SetContext("C:/KSP/GameData", "TestInstance");
        await tab.RefreshAsync();

        Assert.Multiple(() =>
        {
            Assert.That(fake.DiscoverCalls, Is.EqualTo(2), "保守模式空手而归时要自动再扫一次");
            Assert.That(fake.LastOptions?.Scope, Is.EqualTo(GenericConfigScanScope.Wide));
            Assert.That(tab.WideScan, Is.True, "自动扩大之后开关要反映真实状态");
            Assert.That(tab.Files, Has.Count.EqualTo(1));
            Assert.That(tab.HasHiddenWideCandidates, Is.False);
        });
    }

    private sealed class StubConfigService : IModConfigService
    {
        public Task<OperationResult<ModConfigInstanceModel>> LoadAsync(
            string instanceName,
            bool forceReload = false,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult<ModConfigInstanceModel>.Ok(
                new ModConfigInstanceModel("Test", "C:/KSP", "1.12.5", false, [], 0, [])));
    }

    [Test]
    public void PageViewModel_ModeSwitch_TogglesGenericAndAdapterModes()
    {
        var fake = new FakeGenericConfigEditService();
        var page = new ModConfigPageViewModel(
            config: new StubConfigService(),
            genericConfig: fake);

        Assert.That(page.IsAdapterMode, Is.True);
        Assert.That(page.IsGenericMode, Is.False);

        page.SelectGenericModeCommand.Execute(null);
        Assert.That(page.IsGenericMode, Is.True);
        Assert.That(page.IsAdapterMode, Is.False);

        page.SelectAdapterModeCommand.Execute(null);
        Assert.That(page.IsAdapterMode, Is.True);
        Assert.That(page.IsGenericMode, Is.False);
    }
}
