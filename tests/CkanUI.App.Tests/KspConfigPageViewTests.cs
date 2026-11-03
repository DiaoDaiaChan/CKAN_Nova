using Avalonia.Controls;
using CkanUI.App;
using CkanUI.App.Views;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.Services.Abstractions.KspConfig;
using CkanUI.ViewModels.Pages;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace CkanUI.App.Tests;

[TestFixture]
public sealed class KspConfigPageViewTests
{
    private sealed class FakeInstanceService : IInstanceService
    {
        public InstanceInfo? Current { get; set; }
        public IReadOnlyList<InstanceInfo> Instances => Current is not null ? new[] { Current } : Array.Empty<InstanceInfo>();
#pragma warning disable CS0067
        public event Action? InstancesChanged;
        public event Action<InstanceInfo?>? CurrentChanged;
#pragma warning restore CS0067
        public Task<OperationResult<IReadOnlyList<InstanceInfo>>> RefreshAsync(CancellationToken ct = default) =>
            Task.FromResult(OperationResult<IReadOnlyList<InstanceInfo>>.Ok(Instances));
        public Task<OperationResult<InstanceInfo>> SwitchAsync(string instanceName, CancellationToken ct = default) =>
            throw new NotImplementedException();
        public Task<OperationResult<InstanceInfo>> AddAsync(string path, string? name = null, CancellationToken ct = default) =>
            throw new NotImplementedException();
        public Task<OperationResult<InstanceInfo>> CloneAsync(string sourceName, string newName, CancellationToken ct = default) =>
            throw new NotImplementedException();
        public Task<OperationResult<bool>> RemoveAsync(string instanceName, CancellationToken ct = default) =>
            throw new NotImplementedException();
        public Task<OperationResult<InstanceInfo>> RenameAsync(string instanceName, string newName, CancellationToken ct = default) =>
            throw new NotImplementedException();
    }

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

    [Test]
    public void ViewLocator_ResolvesKspConfigPageViewModel_ToKspConfigPageView()
    {
        var viewType = ViewLocator.ResolveViewType(typeof(KspConfigPageViewModel));
        Assert.That(viewType, Is.Not.Null);
        Assert.That(viewType!.FullName, Is.EqualTo("CkanUI.App.Views.KspConfigPageView"));
    }

    [Test]
    public void PageTypes_ContainsKspConfigKey()
    {
        Assert.That(ServiceRegistration.PageTypes.ContainsKey("kspconfig"), Is.True);
        Assert.That(ServiceRegistration.PageTypes["kspconfig"], Is.EqualTo(typeof(KspConfigPageViewModel)));
    }

    [Test]
    public void ServiceRegistration_ResolvesKspConfigPageViewModel()
    {
        using var provider = ServiceRegistration.Build(() => null, services =>
        {
            services.AddSingleton<IKspConfigService>(new FakeKspConfigService());
            services.AddSingleton<IInstanceService>(new FakeInstanceService());
        });
        var vm = provider.GetService<KspConfigPageViewModel>();
        Assert.That(vm, Is.Not.Null);
        Assert.That(vm!.Title, Is.EqualTo("KSP 游戏原生设置"));
    }

    [Test]
    public void ViewLocator_Build_InstantiatesKspConfigPageView()
    {
        var locator = new ViewLocator();
        var fakeVm = new KspConfigPageViewModel(new FakeKspConfigService());

        var control = locator.Build(fakeVm);

        Assert.That(control, Is.Not.Null);
        Assert.That(control, Is.InstanceOf<KspConfigPageView>());
        Assert.That(control, Is.Not.InstanceOf<PagePlaceholderView>());
        Assert.That(control!.DataContext, Is.SameAs(fakeVm));
    }
}
