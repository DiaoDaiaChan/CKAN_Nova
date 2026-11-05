using System;
using System.Threading.Tasks;
using CkanUI.Services.Abstractions.Dtos;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CkanUI.ViewModels.Pages.InstallHistory;

/// <summary>历史快照里的一个模组行（右侧"已装 / 未装"分组的单元）。</summary>
public sealed class InstallHistoryModuleRowViewModel : ObservableObject
{
    private bool _existsInCkan;
    private readonly Func<string, Task>? _navigateToMod;
    private readonly Func<bool, string>? _tooltipResolver;

    public InstallHistoryModuleRowViewModel(
        InstallHistoryModule module,
        bool existsInCkan = true,
        Func<string, Task>? navigateToMod = null,
        Func<bool, string>? tooltipResolver = null)
    {
        ArgumentNullException.ThrowIfNull(module);
        Identifier = module.Identifier;
        Version = module.Version;
        InstalledVersion = module.InstalledVersion;
        IsInstalled = module.IsInstalled;
        IsChanged = module.IsChanged;
        StateText = InstallHistoryText.ModuleStateLabel(module);
        _existsInCkan = existsInCkan;
        _navigateToMod = navigateToMod;
        _tooltipResolver = tooltipResolver;
        NavigateCommand = new AsyncRelayCommand(ExecuteNavigateAsync, () => ExistsInCkan);
    }

    /// <summary>模组标识（CKAN identifier）。</summary>
    public string Identifier { get; }

    /// <summary>快照里记的版本。</summary>
    public string Version { get; }

    /// <summary>当前注册表里的版本；未安装为 null。</summary>
    public string? InstalledVersion { get; }

    /// <summary>当前是否装着。</summary>
    public bool IsInstalled { get; }

    /// <summary>与快照不同（未装或版本不同）。</summary>
    public bool IsChanged { get; }

    /// <summary>状态文案（已安装 / 未安装 / 版本不同（当前 x））。</summary>
    public string StateText { get; }

    /// <summary>版本列文案：快照版本（→ 当前版本，若不同）。</summary>
    public string VersionText => IsChanged && IsInstalled && InstalledVersion is { Length: > 0 }
        ? Version + " → " + InstalledVersion
        : Version;

    /// <summary>该模组在 CKAN 仓库中是否存在。</summary>
    public bool ExistsInCkan
    {
        get => _existsInCkan;
        private set
        {
            if (SetProperty(ref _existsInCkan, value))
            {
                OnPropertyChanged(nameof(NavigationTooltip));
                NavigateCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>悬浮提示文案。</summary>
    public string NavigationTooltip =>
        _tooltipResolver?.Invoke(ExistsInCkan)
        ?? (ExistsInCkan ? "在模组列表中查看该模组" : "当前模组源未收录该模组");

    /// <summary>点击跳转到模组页命令。</summary>
    public IAsyncRelayCommand NavigateCommand { get; }

    /// <summary>更新在 CKAN 中的收录状态。</summary>
    public void SetExistsInCkan(bool exists)
    {
        ExistsInCkan = exists;
    }

    private Task ExecuteNavigateAsync()
    {
        if (!ExistsInCkan || _navigateToMod is null)
        {
            return Task.CompletedTask;
        }

        return _navigateToMod(Identifier);
    }
}
