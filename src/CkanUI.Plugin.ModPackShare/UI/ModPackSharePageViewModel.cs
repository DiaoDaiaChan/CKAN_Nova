using System.Collections.ObjectModel;
using System.Diagnostics;
using CkanUI.Plugin.ModPackShare.Sources;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.Services.Abstractions.Profiles;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CkanUI.Plugin.ModPackShare.UI;

/// <summary>
/// 「整合包配置」页：把 .ckan 整合包导入成独立配置档，并在配置档之间切换。
/// </summary>
/// <remarks>
/// <para>本页不复制游戏实例：切换由宿主的 <see cref="IInstanceProfileService"/> 用目录换出/换入完成，
/// 页面只负责收集意图、调用服务、把结果翻译成中文。所有写操作先看游戏是否在跑——正在跑时服务也会拒绝，
/// 但在这里先拦住能给出更具体的提示。</para>
/// <para>任何一步失败都只写 <see cref="ErrorMessage"/>，不向调用方抛异常：宿主把页面当 UI 用，
/// 异常只会变成一张错误占位图，用户看不到「哪一步、为什么」。</para>
/// </remarks>
public sealed partial class ModPackSharePageViewModel : ObservableObject, IPluginPage, IDisposable
{
    private const string ProfilesFolderName = "CKANProfiles";
    private const string ModPackFileName = "modpack.ckan";
    private const int MaxProfileNameLength = 60;

    private readonly IInstanceProfileService? _profiles;
    private readonly IInstanceService? _instances;
    private readonly IModPackService? _modPacks;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly string _sourcesDirectory;
    private readonly Action<string> _openPath;

    private bool _started;
    private bool _disposed;

    /// <summary>
    /// 宿主 DI 入口：服务从 <see cref="IServiceProvider"/> 里取。
    /// 缺 <see cref="IInstanceProfileService"/> 时不抛：页面会显示「配置档服务不可用」，
    /// 这比让整个页签变成占位图更容易让用户知道缺了什么。
    /// </summary>
    public ModPackSharePageViewModel(IServiceProvider services)
        : this(
            (services ?? throw new ArgumentNullException(nameof(services))).GetService(typeof(IInstanceProfileService)) as IInstanceProfileService,
            services.GetService(typeof(IInstanceService)) as IInstanceService,
            services.GetService(typeof(IModPackService)) as IModPackService,
            httpClient: null,
            sourcesDirectory: null,
            openPath: null)
    {
    }

    /// <summary>
    /// 测试与宿主手动构造入口。<paramref name="httpClient"/> 与 <paramref name="sourcesDirectory"/> 可注入，
    /// 用例因此不必连真实网络，也不会写进用户的缓存目录。
    /// </summary>
    public ModPackSharePageViewModel(
        IInstanceProfileService? profiles,
        IInstanceService? instances = null,
        IModPackService? modPacks = null,
        HttpClient? httpClient = null,
        string? sourcesDirectory = null,
        Action<string>? openPath = null)
    {
        _profiles = profiles;
        _instances = instances;
        _modPacks = modPacks;
        _ownsHttpClient = httpClient is null;
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(GitHubModPackSource.TimeoutSeconds) };
        _sourcesDirectory = string.IsNullOrWhiteSpace(sourcesDirectory)
            ? DefaultSourcesDirectory
            : sourcesDirectory;
        _openPath = openPath ?? OpenWithShell;

        Warnings.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasWarnings));
            OnPropertyChanged(nameof(WarningBadgeText));
        };

        if (_instances is not null)
        {
            _instances.CurrentChanged += OnCurrentChanged;
            _instances.InstancesChanged += OnInstancesChanged;
        }
    }

    /// <summary>单测入口：显式给出依赖与下载目录，页面不会创建真实 HttpClient。</summary>
    public static ModPackSharePageViewModel CreateForTests(
        IInstanceProfileService? profiles,
        IInstanceService? instances = null,
        IModPackService? modPacks = null,
        HttpClient? httpClient = null,
        string? sourcesDirectory = null,
        Action<string>? openPath = null)
        => new(profiles, instances, modPacks, httpClient, sourcesDirectory, openPath ?? (_ => { }));

    /// <summary>默认缓存目录：%LocalAppData%/CKAN/ckan-ui/modpack-sources（Linux 上退化为用户目录）。</summary>
    public static string DefaultSourcesDirectory
    {
        get
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(local))
            {
                local = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            }

            return Path.Combine(local, "CKAN", "ckan-ui", "modpack-sources");
        }
    }

    /// <summary>左栏配置档行。</summary>
    public ObservableCollection<ModPackRowViewModel> Profiles { get; } = [];

    /// <summary>本次刷新/导入发现的问题（非致命）：来源 .ckan 不在配置档目录里、模组数读不出来等。</summary>
    public ObservableCollection<string> Warnings { get; } = [];

    [ObservableProperty]
    private string _instanceName = "未选择实例";

    [ObservableProperty]
    private bool _hasInstance;

    [ObservableProperty]
    private ModPackRowViewModel? _selectedProfile;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _busyText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatusMessage))]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string _sourceText = string.Empty;

    [ObservableProperty]
    private string _launchProfileName = "—";

    [ObservableProperty]
    private string _profilesDirectoryText = "—";

    [ObservableProperty]
    private bool _isGameRunning;

    [ObservableProperty]
    private string _newProfileName = "原版";

    [ObservableProperty]
    private string _emptyTitle = "正在读取配置档…";

    [ObservableProperty]
    private string _emptyDetail = string.Empty;

    [ObservableProperty]
    private bool _showEmptyState = true;

    [ObservableProperty]
    private bool _showEmptyAction;

    [ObservableProperty]
    private bool _showContent;

    /// <summary>当前是否有错误（视图据此决定是否显示错误条）。</summary>
    public bool HasError => ErrorMessage.Length > 0;

    /// <summary>当前是否有成功/提示文案。</summary>
    public bool HasStatusMessage => StatusMessage.Length > 0;

    /// <summary>有配置档时导入区才值得展开。</summary>
    public bool HasProfiles => Profiles.Count > 0;

    /// <summary>启动配置档是否已设置（决定「清除启动配置」是否可点）。</summary>
    public bool HasLaunchProfile => LaunchProfileName is not ("—" or "");

    /// <summary>当前生效的配置档名；没有生效档时明确说「用的是实例现有目录」，不编一个名字。</summary>
    public string ActiveProfileText
        => Profiles.FirstOrDefault(row => row.IsActive) is { } active
            ? $"生效：{active.Name}"
            : "生效：未切换";

    /// <summary>启动配置档徽标文案。</summary>
    public string LaunchProfileBadgeText => $"启动用：{LaunchProfileName}";

    /// <summary>列表计数文案；空列表时说明「还没有」，不显示 0。</summary>
    public string ProfileCountText => Profiles.Count == 0
        ? "还没有配置档"
        : $"{Profiles.Count} 个配置档";

    /// <summary>详情区右上角的切换提示。</summary>
    public string SwitchHintText => SelectedProfile is null
        ? "从左栏选择一个配置档"
        : SelectedProfile.IsActive
            ? "当前已生效"
            : "切换后重新打开游戏生效";

    /// <summary>是否有需要注意的问题。</summary>
    public bool HasWarnings => Warnings.Count > 0;

    /// <summary>警告徽标文案。</summary>
    public string WarningBadgeText => $"{Warnings.Count} 条注意";

    /// <summary>界面可交互（非忙碌）。</summary>
    public bool CanInteract => !IsBusy;

    /// <summary>选中行是否可以直接切换（已生效的档不用再切）。</summary>
    public bool CanSwitchSelected => SelectedProfile is { IsActive: false };

    /// <summary>页面挂到可视树时调用：只加载一次，之后重新挂载走刷新。</summary>
    public void Start()
    {
        if (_disposed)
        {
            return;
        }

        if (_started)
        {
            Refresh();
            return;
        }

        _started = true;
        _ = LoadAsync();
    }

    /// <summary>页面离开可视树：没有定时器也没有轮询，什么也不用停。</summary>
    public void Stop()
    {
    }

    /// <summary>重新读取实例与配置档。</summary>
    public void Refresh()
    {
        if (_disposed || IsBusy)
        {
            return;
        }

        _ = LoadAsync();
    }

    /// <summary>刷新配置档列表（工具条按钮）。</summary>
    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task RefreshAsync()
    {
        await LoadAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// 从 <see cref="SourceText"/> 导入整合包：解析来源 → 下载到缓存 → 读 .ckan 取包名/模组数 →
    /// 建配置档 → 把 .ckan 复制进配置档目录 → 刷新并选中。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanImport))]
    private async Task ImportAsync()
    {
        if (_disposed || IsBusy)
        {
            return;
        }

        var instanceName = _instances?.Current?.Name;
        var source = SourceText.Trim();
        if (string.IsNullOrWhiteSpace(instanceName))
        {
            ShowError("还没有选择游戏实例，无法导入整合包。先在「实例」页选一个实例。");
            return;
        }

        if (source.Length == 0)
        {
            ShowError("先填入 .ckan 的 GitHub 地址或本地路径。");
            return;
        }

        if (_profiles is null)
        {
            ShowError("配置档服务不可用，无法创建配置档。");
            return;
        }

        // 运行状态可能在页面打开后才变化（用户刚点了启动）；写操作前现查一次，别拿旧快照放行。
        SyncGameRunningState();
        if (IsGameRunning)
        {
            ShowError("游戏正在运行。先退出游戏再导入——切换/写入 GameData 会和运行中的游戏抢文件。");
            return;
        }

        IsBusy = true;
        ErrorMessage = string.Empty;
        StatusMessage = string.Empty;
        Warnings.Clear();
        try
        {
            BusyText = "正在下载整合包…";
            var download = await GitHubModPackSource
                .DownloadAsync(source, _httpClient, _sourcesDirectory)
                .ConfigureAwait(true);
            if (!download.IsSuccess || download.Value is null)
            {
                ShowError($"导入失败：{download.Message ?? "来源无法下载"}");
                return;
            }

            var pack = download.Value;

            BusyText = "正在读取 .ckan…";
            var packName = pack.DisplayName;
            var modCount = 0;
            if (_modPacks is not null)
            {
                var editor = await _modPacks.LoadEditorAsync(pack.FilePath).ConfigureAwait(true);
                if (!editor.IsSuccess || editor.Value is null)
                {
                    ShowError($"导入失败：这个 .ckan 读不出来（{editor.Message ?? "格式不正确"}）。没有创建配置档。");
                    return;
                }

                if (!string.IsNullOrWhiteSpace(editor.Value.Name))
                {
                    packName = editor.Value.Name.Trim();
                }

                modCount = CountMods(editor.Value);
            }
            else
            {
                Warnings.Add("宿主的整合包服务不可用：包名按文件名推断，模组数未知。");
            }

            BusyText = "正在创建配置档…";
            var profileName = ToProfileName(packName);
            var created = await _profiles
                .CreateAsync(instanceName, profileName, adoptCurrent: false, source: source)
                .ConfigureAwait(true);
            if (!created.IsSuccess || created.Value is null)
            {
                ShowError($"导入失败：{created.Message ?? "服务拒绝创建配置档"}");
                return;
            }

            var profile = created.Value;
            var copyError = await TryCopyModPackAsync(pack.FilePath, profile.Directory).ConfigureAwait(true);
            if (copyError is not null)
            {
                Warnings.Add(copyError);
            }

            IsBusy = false;
            await LoadAsync(profile.Name).ConfigureAwait(true);
            ShowStatus(modCount > 0
                ? $"已导入「{profile.Name}」（{modCount} 个模组）。它现在还不生效——点「切换」才会接管 GameData/CKAN。"
                : $"已导入「{profile.Name}」。它现在还不生效——点「切换」才会接管 GameData/CKAN。");
        }
        catch (Exception ex)
        {
            ShowError($"导入失败：{ex.Message}");
        }
        finally
        {
            IsBusy = false;
            BusyText = string.Empty;
            NotifyCommandStates();
        }
    }

    /// <summary>把当前的 GameData/CKAN 收进一个新配置档（第一次建立「原版」基线时用）。</summary>
    [RelayCommand(CanExecute = nameof(CanCreateFromCurrent))]
    private async Task CreateFromCurrentAsync()
    {
        var instanceName = _instances?.Current?.Name;
        if (_disposed || IsBusy || string.IsNullOrWhiteSpace(instanceName) || _profiles is null)
        {
            return;
        }

        SyncGameRunningState();
        if (IsGameRunning)
        {
            ShowError("游戏正在运行。先退出游戏再把当前状态收进配置档。");
            return;
        }

        var name = ToProfileName(NewProfileName);
        if (name.Length == 0)
        {
            ShowError("配置档名不能为空。");
            return;
        }

        IsBusy = true;
        ErrorMessage = string.Empty;
        StatusMessage = string.Empty;
        try
        {
            BusyText = "正在把当前状态收进配置档…";
            var result = await _profiles
                .CreateAsync(instanceName!, name, adoptCurrent: true)
                .ConfigureAwait(true);
            if (!result.IsSuccess || result.Value is null)
            {
                ShowError($"新建失败：{result.Message ?? "服务拒绝创建配置档"}");
                return;
            }

            IsBusy = false;
            await LoadAsync(result.Value.Name).ConfigureAwait(true);
            ShowStatus($"已新建「{result.Value.Name}」并接管当前 GameData/CKAN。之后再导入的整合包会与它互不影响。");
        }
        catch (Exception ex)
        {
            ShowError($"新建失败：{ex.Message}");
        }
        finally
        {
            IsBusy = false;
            BusyText = string.Empty;
            NotifyCommandStates();
        }
    }

    /// <summary>切换到选中的配置档。</summary>
    [RelayCommand(CanExecute = nameof(CanSwitch))]
    private async Task SwitchAsync()
    {
        var instanceName = _instances?.Current?.Name;
        var target = SelectedProfile;
        if (_disposed || IsBusy || target is null || string.IsNullOrWhiteSpace(instanceName) || _profiles is null)
        {
            return;
        }

        if (target.IsActive)
        {
            ShowError($"「{target.Name}」已经是当前生效的配置档，不需要切换。");
            return;
        }

        SyncGameRunningState();
        if (IsGameRunning)
        {
            ShowError("游戏正在运行。先退出游戏再切换配置档——GameData 被占用时换出会失败。");
            return;
        }

        IsBusy = true;
        ErrorMessage = string.Empty;
        StatusMessage = string.Empty;
        try
        {
            BusyText = $"正在切到「{target.Name}」…";
            var result = await _profiles.SwitchAsync(instanceName!, target.Name).ConfigureAwait(true);
            if (!result.IsSuccess)
            {
                ShowError($"切换失败：{result.Message ?? "服务拒绝切换"}");
                return;
            }

            IsBusy = false;
            await LoadAsync(target.Name).ConfigureAwait(true);
            ShowStatus($"已切到「{target.Name}」，重新打开游戏/回主菜单后生效。");
        }
        catch (Exception ex)
        {
            ShowError($"切换失败：{ex.Message}");
        }
        finally
        {
            IsBusy = false;
            BusyText = string.Empty;
            NotifyCommandStates();
        }
    }

    /// <summary>删除选中的配置档；当前生效的那一档由服务拒绝。</summary>
    [RelayCommand(CanExecute = nameof(CanDelete))]
    private async Task DeleteAsync()
    {
        var instanceName = _instances?.Current?.Name;
        var target = SelectedProfile;
        if (_disposed || IsBusy || target is null || string.IsNullOrWhiteSpace(instanceName) || _profiles is null)
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = string.Empty;
        StatusMessage = string.Empty;
        try
        {
            BusyText = $"正在删除「{target.Name}」…";
            var result = await _profiles.DeleteAsync(instanceName!, target.Name).ConfigureAwait(true);
            if (!result.IsSuccess)
            {
                // 当前生效档被拒绝是最常见的失败：把服务原因原样展示，用户知道要先切走。
                ShowError($"删除失败：{result.Message ?? "服务拒绝删除"}");
                return;
            }

            IsBusy = false;
            await LoadAsync().ConfigureAwait(true);
            ShowStatus($"已删除「{target.Name}」及其 GameData/CKAN。");
        }
        catch (Exception ex)
        {
            ShowError($"删除失败：{ex.Message}");
        }
        finally
        {
            IsBusy = false;
            BusyText = string.Empty;
            NotifyCommandStates();
        }
    }

    /// <summary>把选中的配置档设为「启动游戏时自动切换」。</summary>
    [RelayCommand(CanExecute = nameof(CanSetLaunch))]
    private async Task SetLaunchAsync()
    {
        var instanceName = _instances?.Current?.Name;
        var target = SelectedProfile;
        if (_disposed || IsBusy || target is null || string.IsNullOrWhiteSpace(instanceName) || _profiles is null)
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = string.Empty;
        StatusMessage = string.Empty;
        try
        {
            var result = await _profiles.SetLaunchProfileAsync(instanceName!, target.Name).ConfigureAwait(true);
            if (!result.IsSuccess)
            {
                ShowError($"设置启动配置档失败：{result.Message ?? "服务拒绝写入"}");
                return;
            }

            IsBusy = false;
            await LoadAsync(target.Name).ConfigureAwait(true);
            ShowStatus($"已把「{target.Name}」设为启动配置档：下次启动游戏前会自动切过去。");
        }
        catch (Exception ex)
        {
            ShowError($"设置启动配置档失败：{ex.Message}");
        }
        finally
        {
            IsBusy = false;
            NotifyCommandStates();
        }
    }

    /// <summary>清除启动配置档（启动时不再自动切换）。</summary>
    [RelayCommand(CanExecute = nameof(CanClearLaunch))]
    private async Task ClearLaunchAsync()
    {
        var instanceName = _instances?.Current?.Name;
        if (_disposed || IsBusy || string.IsNullOrWhiteSpace(instanceName) || _profiles is null)
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = string.Empty;
        StatusMessage = string.Empty;
        try
        {
            var result = await _profiles.SetLaunchProfileAsync(instanceName!, null).ConfigureAwait(true);
            if (!result.IsSuccess)
            {
                ShowError($"清除启动配置档失败：{result.Message ?? "服务拒绝写入"}");
                return;
            }

            IsBusy = false;
            await LoadAsync().ConfigureAwait(true);
            ShowStatus("已清除启动配置档，启动游戏时不再自动切换。");
        }
        catch (Exception ex)
        {
            ShowError($"清除启动配置档失败：{ex.Message}");
        }
        finally
        {
            IsBusy = false;
            NotifyCommandStates();
        }
    }

    /// <summary>打开配置档根目录（CKANProfiles），不碰文件内容。</summary>
    [RelayCommand(CanExecute = nameof(CanOpenFolder))]
    private void OpenFolder()
    {
        if (_disposed)
        {
            return;
        }

        var directory = ResolveProfilesDirectory();
        if (directory is null)
        {
            ShowError("还没有选择游戏实例，无法定位配置档目录。");
            return;
        }

        if (!Directory.Exists(directory))
        {
            ShowError($"配置档目录还不存在：{directory}。先新建或导入一个配置档。");
            return;
        }

        try
        {
            _openPath(directory);
            ShowStatus($"已在文件管理器里打开 {directory}。");
        }
        catch (Exception ex)
        {
            ShowError($"打开文件夹失败：{ex.Message}");
        }
    }

    /// <summary>释放订阅与自建的 HttpClient；重复调用安全。</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_instances is not null)
        {
            _instances.CurrentChanged -= OnCurrentChanged;
            _instances.InstancesChanged -= OnInstancesChanged;
        }

        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    partial void OnSelectedProfileChanged(ModPackRowViewModel? value)
    {
        foreach (var row in Profiles)
        {
            row.IsSelected = ReferenceEquals(row, value);
        }

        OnPropertyChanged(nameof(CanSwitchSelected));
        OnPropertyChanged(nameof(SwitchHintText));
        NotifyCommandStates();
    }

    partial void OnLaunchProfileNameChanged(string value)
    {
        OnPropertyChanged(nameof(HasLaunchProfile));
        OnPropertyChanged(nameof(LaunchProfileBadgeText));
    }

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanInteract));
        NotifyCommandStates();
    }

    private bool CanRefresh() => !IsBusy;

    private bool CanImport() => !IsBusy && HasInstance && SourceText.Trim().Length > 0;

    private bool CanCreateFromCurrent() => !IsBusy && HasInstance;

    private bool CanSwitch() => !IsBusy && HasInstance && SelectedProfile is { IsActive: false };

    private bool CanDelete() => !IsBusy && HasInstance && SelectedProfile is not null;

    private bool CanSetLaunch() => !IsBusy && HasInstance && SelectedProfile is { IsLaunchProfile: false };

    private bool CanClearLaunch() => !IsBusy && HasInstance && HasLaunchProfile;

    private bool CanOpenFolder() => HasInstance;

    private async Task LoadAsync(string? preferredName = null)
    {
        if (_disposed || IsBusy)
        {
            return;
        }

        ErrorMessage = string.Empty;
        StatusMessage = string.Empty;
        Warnings.Clear();

        var current = _instances?.Current;
        HasInstance = current is not null;
        InstanceName = current is null ? "未选择实例" : current.Name;
        IsGameRunning = current is not null && (_instances?.IsGameRunning(current.Name) ?? false);
        ProfilesDirectoryText = current is null
            ? "—"
            : Path.Combine(current.GameDir, ProfilesFolderName);

        if (_profiles is null)
        {
            ApplyEmptyState(
                "配置档服务不可用",
                "宿主的实例配置档服务没有注册，这一页无法读取或切换配置档。检查宿主版本或插件启用状态。",
                showAction: false);
            return;
        }

        if (current is null)
        {
            ApplyEmptyState(
                "还没有选择游戏实例",
                "配置档属于某个游戏实例。先在「实例」页选中一个实例，再回来导入整合包。",
                showAction: false);
            return;
        }

        IsBusy = true;
        BusyText = "正在读取配置档…";
        try
        {
            var listed = await _profiles.ListAsync(current.Name).ConfigureAwait(true);
            if (!listed.IsSuccess || listed.Value is null)
            {
                ApplyEmptyState(
                    "读取配置档失败",
                    listed.Message ?? "服务没有给出原因。",
                    showAction: false);
                ShowError($"读取配置档失败：{listed.Message ?? "服务没有给出原因"}");
                return;
            }

            var launch = await _profiles.GetLaunchProfileAsync(current.Name).ConfigureAwait(true);
            LaunchProfileName = launch.IsSuccess && !string.IsNullOrWhiteSpace(launch.Value)
                ? launch.Value!
                : "—";

            Profiles.Clear();
            foreach (var info in listed.Value)
            {
                Profiles.Add(new ModPackRowViewModel(info));
            }

            OnPropertyChanged(nameof(HasProfiles));
            BuildWarnings();

            if (Profiles.Count == 0)
            {
                ApplyEmptyState(
                    "这个实例还没有配置档",
                    "导入一个 .ckan 整合包，或者先把当前 GameData/CKAN 收进「原版」配置档，之后就能在多个整合包之间切换。",
                    showAction: true);
                return;
            }

            ShowContent = true;
            ShowEmptyState = false;
            ShowEmptyAction = false;
            EmptyTitle = string.Empty;
            EmptyDetail = string.Empty;

            var wanted = preferredName
                ?? SelectedProfile?.Name
                ?? Profiles.FirstOrDefault(row => row.IsActive)?.Name;
            SelectedProfile = Profiles.FirstOrDefault(row => row.Name == wanted)
                ?? Profiles.FirstOrDefault(row => row.IsActive)
                ?? Profiles[0];
        }
        catch (Exception ex)
        {
            ApplyEmptyState("读取配置档失败", ex.Message, showAction: false);
            ShowError($"读取配置档失败：{ex.Message}");
        }
        finally
        {
            IsBusy = false;
            BusyText = string.Empty;
            NotifyCommandStates();
        }
    }

    /// <summary>把运行状态同步成宿主的当前事实；写操作前的门禁必须基于这一份，而不是上次刷新的快照。</summary>
    private void SyncGameRunningState()
    {
        var current = _instances?.Current;
        var running = current is not null && (_instances?.IsGameRunning(current.Name) ?? false);
        if (running != IsGameRunning)
        {
            IsGameRunning = running;
        }
    }

    private void BuildWarnings()
    {
        foreach (var row in Profiles)
        {
            if (row.Model.Source is { Length: > 0 }
                && !File.Exists(Path.Combine(row.Model.Directory, ModPackFileName)))
            {
                Warnings.Add($"「{row.Name}」的 modpack.ckan 不在配置档目录里，来源只剩记录：{row.Model.Source}");
            }
        }

        if (HasLaunchProfile && Profiles.All(row => row.Name != LaunchProfileName))
        {
            Warnings.Add($"启动配置档「{LaunchProfileName}」已不存在，启动前请重新设置或清除。");
        }
    }

    private void ApplyEmptyState(string title, string detail, bool showAction)
    {
        Profiles.Clear();
        OnPropertyChanged(nameof(HasProfiles));
        SelectedProfile = null;
        ShowContent = false;
        ShowEmptyState = true;
        ShowEmptyAction = showAction;
        EmptyTitle = title;
        EmptyDetail = detail;
        ShowStatus(string.Empty);
        NotifyCommandStates();
    }

    /// <summary>把下载好的 .ckan 复制进配置档目录；失败返回中文原因而不是抛。</summary>
    private static async Task<string?> TryCopyModPackAsync(string sourcePath, string profileDirectory)
    {
        try
        {
            Directory.CreateDirectory(profileDirectory);
            await using var input = File.OpenRead(sourcePath);
            var target = Path.Combine(profileDirectory, ModPackFileName);
            await using var output = File.Create(target);
            await input.CopyToAsync(output).ConfigureAwait(false);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            return $"配置档已创建，但把 .ckan 复制进 {profileDirectory} 失败：{ex.Message}";
        }
    }

    private string? ResolveProfilesDirectory()
    {
        var current = _instances?.Current;
        if (current is null)
        {
            return null;
        }

        return ProfilesDirectoryText is "—" or ""
            ? Path.Combine(current.GameDir, ProfilesFolderName)
            : ProfilesDirectoryText;
    }

    /// <summary>统计 depends + recommends：这才是「这个整合包会带进来多少模组」。</summary>
    private static int CountMods(ModPackEditorDocument document)
        => document.Relations.Count(relation =>
            relation.RelationType is ModPackRelationTypes.Depends or ModPackRelationTypes.Recommends);

    /// <summary>
    /// 把包名压成配置档名：只允许字母/数字/空格/下划线/点/连字符（宿主契约限制），
    /// 其它字符替换成连字符，避免名字里带路径分隔符或 ..。
    /// </summary>
    private static string ToProfileName(string? raw)
    {
        var text = (raw ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return "整合包";
        }

        var buffer = new System.Text.StringBuilder(text.Length);
        foreach (var ch in text)
        {
            var allowed = char.IsLetterOrDigit(ch) || ch is ' ' or '_' or '.' or '-';
            buffer.Append(allowed ? ch : '-');
        }

        var name = buffer.ToString().Trim(' ', '.', '-');
        while (name.Contains("--", StringComparison.Ordinal))
        {
            name = name.Replace("--", "-", StringComparison.Ordinal);
        }

        if (name.Length == 0)
        {
            name = "整合包";
        }

        if (name.Contains("..", StringComparison.Ordinal))
        {
            name = name.Replace("..", ".", StringComparison.Ordinal);
        }

        return name.Length > MaxProfileNameLength
            ? name[..MaxProfileNameLength].Trim(' ', '.', '-')
            : name;
    }

    private void ShowStatus(string message)
    {
        StatusMessage = message;
        ErrorMessage = string.Empty;
    }

    private void ShowError(string message)
    {
        ErrorMessage = message;
        StatusMessage = string.Empty;
    }

    private void NotifyCommandStates()
    {
        RefreshCommand.NotifyCanExecuteChanged();
        ImportCommand.NotifyCanExecuteChanged();
        CreateFromCurrentCommand.NotifyCanExecuteChanged();
        SwitchCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
        SetLaunchCommand.NotifyCanExecuteChanged();
        ClearLaunchCommand.NotifyCanExecuteChanged();
        OpenFolderCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanSwitchSelected));
        OnPropertyChanged(nameof(HasLaunchProfile));
        OnPropertyChanged(nameof(ActiveProfileText));
        OnPropertyChanged(nameof(ProfileCountText));
        OnPropertyChanged(nameof(SwitchHintText));
    }

    private void OnCurrentChanged(InstanceInfo? current)
    {
        if (_disposed || IsBusy)
        {
            return;
        }

        _ = LoadAsync();
    }

    private void OnInstancesChanged()
    {
        if (_disposed || IsBusy)
        {
            return;
        }

        _ = LoadAsync();
    }

    private static void OpenWithShell(string path)
    {
        var info = OperatingSystem.IsWindows()
            ? new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true }
            : new ProcessStartInfo(path) { UseShellExecute = true };
        using var process = Process.Start(info);
    }
}
