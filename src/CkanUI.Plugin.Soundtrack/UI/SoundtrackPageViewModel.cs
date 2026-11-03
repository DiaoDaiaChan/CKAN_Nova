using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using CkanUI.Plugin.Soundtrack.Playlists;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CkanUI.Plugin.Soundtrack.UI;

/// <summary>
/// 「音乐」页的视图状态。
/// </summary>
/// <remarks>
/// <para>页面不复制 playlists.cfg 的模型：列表、计数、缺失标记都从同一个 <see cref="SoundtrackDocument"/> 现算，
/// 保存时原样交回服务。这样未知节点、换行风格这些页面不认识的字段只有一份，不会被界面层的第二次建模弄丢。</para>
/// <para>脏状态比较“当前文档签名”和“上次载入/保存时的签名”，而不是改一次就置真：用户把名字改回原样时，
/// 页面应该如实显示已与磁盘一致，而不是逼用户再存一次。</para>
/// </remarks>
public sealed partial class SoundtrackPageViewModel : ObservableObject, IPluginPage, IDisposable
{
    private const string PlaylistsFolderName = "Playlists";
    private const string PlaylistFileName = "playlists.cfg";

    private readonly ISoundtrackService? _soundtrack;
    private readonly IInstanceService? _instances;
    private readonly Action<string> _openPath;
    private readonly List<SoundtrackLibraryRowViewModel> _libraryAll = new();

    private SoundtrackDocument? _document;
    private SoundtrackPlaylistRowViewModel? _selectedPlaylist;
    private string _baselineSignature = string.Empty;
    private bool _isDirty;
    private bool _started;
    private bool _disposed;

    /// <summary>
    /// 宿主用 ActivatorUtilities 构造：两个服务都从 DI 取。参数允许为 null，是因为插件服务要由宿主注册；
    /// 没注册时页面至少能打开并说清缺什么，而不是让 PluginPageFactory 构造失败后静默地少一个页签。
    /// </summary>
    public SoundtrackPageViewModel(ISoundtrackService? soundtrack, IInstanceService? instances)
        : this(soundtrack, instances, OpenWithShell)
    {
    }

    /// <summary>
    /// 宿主只注册了实例服务时的构造：播放列表服务由插件自己造——它读写的这个文件只有本插件懂，
    /// 让宿主去注册一个插件内部实现是本末倒置。
    /// </summary>
    public SoundtrackPageViewModel(IInstanceService instances)
        : this(new SoundtrackService(), instances)
    {
    }

    private SoundtrackPageViewModel(ISoundtrackService? soundtrack, IInstanceService? instances, Action<string> openPath)
    {
        _soundtrack = soundtrack;
        _instances = instances;
        _openPath = openPath;
        if (_instances is not null)
        {
            _instances.CurrentChanged += OnCurrentChanged;
        }
    }

    /// <summary>
    /// 单测入口：正常路径只由宿主构造，测试不该为了拿到实例服务去实现整套 IInstanceService；
    /// 同时把“打开资源管理器”换成回调，用例可以断言失败提示而不用真的弹窗。
    /// </summary>
    public static SoundtrackPageViewModel CreateForTests(
        ISoundtrackService soundtrack,
        IInstanceService? instances = null,
        Action<string>? openPath = null)
        => new(soundtrack, instances, openPath ?? OpenWithShell);

    /// <summary>左栏的播放列表行，顺序即 playlists.cfg 里的节点顺序。</summary>
    public ObservableCollection<SoundtrackPlaylistRowViewModel> Playlists { get; } = new();

    /// <summary>右栏当前选中播放列表的曲目行，顺序即播放顺序。</summary>
    public ObservableCollection<SoundtrackTrackRowViewModel> SelectedTracks { get; } = new();

    /// <summary>乐曲库里当前可见的行（受 <see cref="LibrarySearch"/> 过滤）。</summary>
    public ObservableCollection<SoundtrackLibraryRowViewModel> LibraryRows { get; } = new();

    [ObservableProperty]
    private string _instanceName = "未选择实例";

    [ObservableProperty]
    private string _instanceGameDir = "—";

    [ObservableProperty]
    private string _playlistFilePath = "—";

    [ObservableProperty]
    private string _modStatusText = "未找到";

    [ObservableProperty]
    private bool _isModInstalled;

    [ObservableProperty]
    private string _playlistCountText = "0 个";

    [ObservableProperty]
    private string _trackCountText = "0 首";

    [ObservableProperty]
    private string _missingTrackCountText = "无缺失";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatusMessage))]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _showContent;

    [ObservableProperty]
    private bool _showEmptyState = true;

    [ObservableProperty]
    private bool _showCreateEntry;

    [ObservableProperty]
    private string _emptyTitle = "正在读取播放列表…";

    [ObservableProperty]
    private string _emptyDetail = string.Empty;

    [ObservableProperty]
    private string _librarySearch = string.Empty;

    [ObservableProperty]
    private string _librarySummaryText = "乐曲库为空";

    /// <summary>最近一次保存在进度中时给按钮层的提示（保存/载入都会切换它）。</summary>
    public bool CanInteract => !IsBusy;

    public bool HasError => ErrorMessage.Length > 0;

    public bool HasStatusMessage => StatusMessage.Length > 0;

    public bool IsDirty
    {
        get => _isDirty;
        private set
        {
            if (!SetProperty(ref _isDirty, value))
            {
                return;
            }

            OnPropertyChanged(nameof(DirtyText));
        }
    }

    /// <summary>脏状态的一句话，给页头的徽标用。</summary>
    public string DirtyText => IsDirty ? "有未保存的修改" : "已与磁盘一致";

    public bool HasSelection => _selectedPlaylist is not null;

    public bool HasSelectedTracks => SelectedTracks.Count > 0;

    public bool HasLibraryRows => LibraryRows.Count > 0;

    /// <summary>当前选中的播放列表行。切换时把右栏的曲目快照与详情字段一起换掉。</summary>
    public SoundtrackPlaylistRowViewModel? SelectedPlaylist
    {
        get => _selectedPlaylist;
        set
        {
            if (ReferenceEquals(_selectedPlaylist, value))
            {
                return;
            }

            if (_selectedPlaylist is not null)
            {
                _selectedPlaylist.IsSelected = false;
            }

            _selectedPlaylist = value;
            if (_selectedPlaylist is not null)
            {
                _selectedPlaylist.IsSelected = true;
            }

            OnPropertyChanged();
            SyncSelectionDetails();
            NotifyCommandStates();
        }
    }

    /// <summary>右栏的名字输入框。写回行 VM，脏标记统一由行的 Changed 事件触发。</summary>
    public string SelectedPlaylistName
    {
        get => _selectedPlaylist?.Name ?? string.Empty;
        set
        {
            if (_selectedPlaylist is null || string.Equals(_selectedPlaylist.Name, value, StringComparison.Ordinal))
            {
                return;
            }

            _selectedPlaylist.Name = value;
            OnPropertyChanged();
        }
    }

    public bool SelectedLoop
    {
        get => _selectedPlaylist?.Loop ?? false;
        set
        {
            if (_selectedPlaylist is null || _selectedPlaylist.Loop == value)
            {
                return;
            }

            _selectedPlaylist.Loop = value;
            OnPropertyChanged();
        }
    }

    public bool SelectedShuffle
    {
        get => _selectedPlaylist?.Shuffle ?? false;
        set
        {
            if (_selectedPlaylist is null || _selectedPlaylist.Shuffle == value)
            {
                return;
            }

            _selectedPlaylist.Shuffle = value;
            OnPropertyChanged();
        }
    }

    public bool SelectedPauseOnGamePause
    {
        get => _selectedPlaylist?.PauseOnGamePause ?? false;
        set
        {
            if (_selectedPlaylist is null || _selectedPlaylist.PauseOnGamePause == value)
            {
                return;
            }

            _selectedPlaylist.PauseOnGamePause = value;
            OnPropertyChanged();
        }
    }

    public bool SelectedDisableAfterPlay
    {
        get => _selectedPlaylist?.DisableAfterPlay ?? false;
        set
        {
            if (_selectedPlaylist is null || _selectedPlaylist.DisableAfterPlay == value)
            {
                return;
            }

            _selectedPlaylist.DisableAfterPlay = value;
            OnPropertyChanged();
        }
    }

    /// <summary>右栏曲目区的标题句：缺失数量要在这里说清，用户才知道为什么有歌播不出来。</summary>
    public string SelectedTrackCountText
    {
        get
        {
            if (_selectedPlaylist is null)
            {
                return "未选择播放列表";
            }

            var playable = _selectedPlaylist.Model.Tracks.Count;
            var missing = _selectedPlaylist.Model.Tracks.Count(track => !track.Exists);
            return missing == 0
                ? $"{playable} 首曲目"
                : $"{playable} 首曲目，其中 {missing} 首缺失";
        }
    }

    /// <summary>视图第一次挂到可视树时只加载一次：重新挂载不该把用户没保存的改动冲掉。</summary>
    public void Start()
    {
        if (_disposed || _started)
        {
            return;
        }

        _started = true;
        _ = LoadCoreAsync();
    }

    /// <summary>视图离开可视树。本页不轮询、不持有定时器，所以这里只占位保持调用对称，释放归宿主。</summary>
    public void Stop()
    {
    }

    /// <summary>重新载入：显式丢弃未保存的修改，是“放弃更改”和手动刷新的同一条路径。</summary>
    [RelayCommand(CanExecute = nameof(CanReload))]
    public async Task ReloadAsync()
    {
        _started = true;
        await LoadCoreAsync().ConfigureAwait(true);
    }

    /// <summary>新建一个空播放列表（只有名字，其余键用模型默认值，保存时才落盘）。</summary>
    [RelayCommand(CanExecute = nameof(CanCreatePlaylist))]
    public void CreatePlaylist()
    {
        if (_document is null)
        {
            ShowError("先选择一个游戏实例，才能新建播放列表。");
            return;
        }

        var playlist = new SoundtrackPlaylist { Name = MakeUniquePlaylistName() };
        _document.Playlists.Add(playlist);
        var row = CreatePlaylistRow(playlist);
        Playlists.Add(row);
        SelectedPlaylist = row;
        RefreshCounts();
        RefreshViewState();
        RefreshDirty();
        NotifyCommandStates();
        ShowStatus($"已新建播放列表「{playlist.Name}」，保存后写入 playlists.cfg。");
    }

    /// <summary>空态主按钮：先建一个播放列表，再走正常保存路径（目录与文件由服务创建）。</summary>
    [RelayCommand(CanExecute = nameof(CanCreatePlaylist))]
    public async Task CreatePlaylistAndSaveAsync()
    {
        CreatePlaylist();
        if (_document is null)
        {
            return;
        }

        await SaveAsync().ConfigureAwait(true);
    }

    /// <summary>删除选中播放列表：先从文档里摘掉再刷新选择，避免右栏继续引用已删除的对象。</summary>
    [RelayCommand(CanExecute = nameof(CanDeletePlaylist))]
    public void DeletePlaylist()
    {
        if (_selectedPlaylist is null || _document is null)
        {
            ShowError("先在左栏选中一个播放列表，再删除。");
            return;
        }

        var removed = _selectedPlaylist;
        var index = Math.Max(0, Playlists.IndexOf(removed));
        _document.Playlists.Remove(removed.Model);
        Playlists.Remove(removed);
        SelectedPlaylist = Playlists.Count == 0 ? null : Playlists[Math.Min(index, Playlists.Count - 1)];
        RefreshCounts();
        RefreshViewState();
        RefreshDirty();
        NotifyCommandStates();
        ShowStatus($"已删除播放列表「{removed.Name}」，保存后才会从 playlists.cfg 消失。");
    }

    /// <summary>放弃未保存修改：直接按磁盘内容重读，不做内存回滚（服务才是文件的权威）。</summary>
    [RelayCommand(CanExecute = nameof(CanDiscardChanges))]
    public async Task DiscardChangesAsync()
    {
        await LoadCoreAsync().ConfigureAwait(true);
        if (ErrorMessage.Length == 0)
        {
            ShowStatus("已放弃未保存的修改，页面回到磁盘上的内容。");
        }
    }

    /// <summary>保存：先本地校验名字，再交给服务写盘；服务说失败就保留脏状态，绝不自己宣布成功。</summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    public async Task SaveAsync()
    {
        if (_soundtrack is null)
        {
            ShowError("播放列表服务未就绪，暂时不能保存。");
            return;
        }

        if (_document is null)
        {
            ShowError("先选择一个游戏实例，才有地方保存播放列表。");
            return;
        }

        if (IsBusy)
        {
            return;
        }

        if (!TryValidate(out var validationError))
        {
            ShowError(validationError);
            return;
        }

        IsBusy = true;
        ErrorMessage = string.Empty;
        StatusMessage = string.Empty;
        try
        {
            // 写盘是同步的（备份 + 原子替换），放线程池才不会在保存大列表时冻住 UI。
            var document = _document;
            var result = await Task.Run(() => _soundtrack.Save(document)).ConfigureAwait(true);
            if (!result.IsSuccess)
            {
                // 服务返回失败时脏标记必须留着：用户还要靠它知道磁盘上仍是旧内容。
                ShowError($"保存失败：{result.Message ?? "服务没有给出原因"}");
                return;
            }

            _baselineSignature = ComputeSignature();
            IsDirty = false;
            document.FileExists = true;

            var outcome = result.Value;
            if (outcome is null)
            {
                ShowStatus("已保存到 playlists.cfg。");
            }
            else if (!string.IsNullOrWhiteSpace(outcome.BackupPath))
            {
                ShowStatus($"已保存 {outcome.PlaylistCount} 个播放列表、{outcome.TrackCount} 首曲目，旧文件备份在 {outcome.BackupPath}");
            }
            else
            {
                ShowStatus($"已保存 {outcome.PlaylistCount} 个播放列表、{outcome.TrackCount} 首曲目（第一次写入，没有旧文件需要备份）。");
            }

            RefreshViewState();
            NotifyCommandStates();
        }
        catch (Exception ex)
        {
            ShowError($"保存失败：{ex.Message}");
        }
        finally
        {
            IsBusy = false;
            NotifyCommandStates();
        }
    }

    /// <summary>把乐曲库里的一行加进选中播放列表；重名不重复添加，避免播放列表出现两份同一首歌。</summary>
    [RelayCommand(CanExecute = nameof(CanAddTrack))]
    public void AddTrack(SoundtrackLibraryRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        if (_selectedPlaylist is null)
        {
            ShowError("先在左栏选一个播放列表，再添加曲目。");
            return;
        }

        if (_selectedPlaylist.Model.Tracks.Any(track => string.Equals(track.Name, row.Name, StringComparison.OrdinalIgnoreCase)))
        {
            ShowError($"「{row.Name}」已经在这个播放列表里，没有重复添加。");
            return;
        }

        var reference = new SoundtrackTrackRef
        {
            Name = row.Name,
            FilePath = row.Model.FullPath,
            SizeBytes = row.Model.SizeBytes,
        };
        _selectedPlaylist.Model.Tracks.Add(reference);
        SelectedTracks.Add(new SoundtrackTrackRowViewModel(reference, OpenTrackInExplorer));
        row.IsAdded = true;
        _selectedPlaylist.NotifyTracksChanged();
        RefreshCounts();
        RefreshDirty();
        NotifyCommandStates();
        ShowStatus($"已把「{row.Name}」加入「{_selectedPlaylist.Name}」，保存后写入 playlists.cfg。");
    }

    /// <summary>曲目上移一位。行与模型同时移动，保证界面顺序就是保存顺序。</summary>
    [RelayCommand(CanExecute = nameof(CanEditTracks))]
    public void MoveTrackUp(SoundtrackTrackRowViewModel? row)
    {
        var index = row is null ? -1 : SelectedTracks.IndexOf(row);
        if (index <= 0)
        {
            return;
        }

        MoveTrack(index, index - 1);
    }

    /// <summary>曲目下移一位。</summary>
    [RelayCommand(CanExecute = nameof(CanEditTracks))]
    public void MoveTrackDown(SoundtrackTrackRowViewModel? row)
    {
        var index = row is null ? -1 : SelectedTracks.IndexOf(row);
        if (index < 0 || index >= SelectedTracks.Count - 1)
        {
            return;
        }

        MoveTrack(index, index + 1);
    }

    /// <summary>从播放列表移除曲目；只影响列表，不删磁盘上的音频文件。</summary>
    [RelayCommand(CanExecute = nameof(CanEditTracks))]
    public void RemoveTrack(SoundtrackTrackRowViewModel? row)
    {
        if (row is null || _selectedPlaylist is null)
        {
            return;
        }

        var index = SelectedTracks.IndexOf(row);
        if (index < 0)
        {
            return;
        }

        _selectedPlaylist.Model.Tracks.RemoveAt(index);
        SelectedTracks.RemoveAt(index);
        _selectedPlaylist.NotifyTracksChanged();
        RefreshLibraryAddedFlags();
        RefreshCounts();
        RefreshDirty();
        NotifyCommandStates();
        ShowStatus($"已从「{_selectedPlaylist.Name}」移除「{row.Name}」，保存后生效。");
    }

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
        }
    }

    partial void OnIsBusyChanged(bool value) => NotifyCommandStates();

    partial void OnLibrarySearchChanged(string value) => ApplyLibraryFilter();

    private bool CanReload() => !IsBusy;

    private bool CanCreatePlaylist() => !IsBusy && _document is not null;

    private bool CanDeletePlaylist() => !IsBusy && _selectedPlaylist is not null;

    private bool CanDiscardChanges() => !IsBusy && IsDirty;

    private bool CanSave() => !IsBusy && _document is not null && _soundtrack is not null;

    private bool CanAddTrack(SoundtrackLibraryRowViewModel? row) => !IsBusy && _selectedPlaylist is not null && row is not null;

    private bool CanEditTracks(SoundtrackTrackRowViewModel? row) => !IsBusy && _selectedPlaylist is not null && row is not null;

    private async Task LoadCoreAsync()
    {
        if (_disposed || IsBusy)
        {
            return;
        }

        var gameDir = _instances?.Current?.GameDir;
        ApplyInstanceHeader(gameDir);

        if (_soundtrack is null)
        {
            ApplyServiceMissingState();
            return;
        }

        if (string.IsNullOrWhiteSpace(gameDir))
        {
            ApplyNoInstanceState();
            return;
        }

        IsBusy = true;
        ErrorMessage = string.Empty;
        StatusMessage = string.Empty;
        try
        {
            // 服务是同步读盘（扫乐曲库可能上千个文件），放线程池才不会在切页时卡住 UI 线程。
            var document = await Task.Run(() => _soundtrack.Load(gameDir)).ConfigureAwait(true);
            ApplyDocument(document);
        }
        catch (Exception ex)
        {
            // 服务契约说 Load 不抛异常，但页面是最后一道防线：读盘炸了也要给用户留一条可读的路。
            _document = null;
            Playlists.Clear();
            _libraryAll.Clear();
            LibraryRows.Clear();
            SelectedPlaylist = null;
            ShowContent = false;
            ShowEmptyState = true;
            ShowCreateEntry = false;
            EmptyTitle = "读取播放列表失败";
            EmptyDetail = $"读取 {ResolvePlaylistPath(gameDir)} 时出错：{ex.Message}";
            ShowError($"读取 playlists.cfg 失败：{ex.Message}");
            RefreshCounts();
            RefreshDirty();
        }
        finally
        {
            IsBusy = false;
            NotifyCommandStates();
        }
    }

    private void ApplyInstanceHeader(string? gameDir)
    {
        var current = _instances?.Current;
        InstanceName = string.IsNullOrWhiteSpace(current?.Name) ? "未选择实例" : current.Name;
        InstanceGameDir = string.IsNullOrWhiteSpace(gameDir) ? "—" : gameDir;
        PlaylistFilePath = ResolvePlaylistPath(gameDir);
    }

    private void ApplyDocument(SoundtrackDocument document)
    {
        _document = document;

        // 文档里的路径比页面拼出来的更权威：服务可能按 Core 的实例布局解析到别的位置。
        PlaylistFilePath = string.IsNullOrWhiteSpace(document.FilePath)
            ? ResolvePlaylistPath(document.GameDir)
            : document.FilePath;
        IsModInstalled = !string.IsNullOrWhiteSpace(document.ModDirectory);
        ModStatusText = IsModInstalled
            ? $"已安装（{document.ModDirectory}）"
            : "未找到（GameData 下没有 Soundtrack 目录）";

        Playlists.Clear();
        foreach (var playlist in document.Playlists)
        {
            Playlists.Add(CreatePlaylistRow(playlist));
        }

        _libraryAll.Clear();
        foreach (var file in document.MusicLibrary)
        {
            _libraryAll.Add(new SoundtrackLibraryRowViewModel(file));
        }

        _libraryAll.Sort((left, right) => string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase));
        ApplyLibraryFilter();

        SelectedPlaylist = Playlists.Count > 0 ? Playlists[0] : null;
        RefreshCounts();
        RefreshViewState();
        _baselineSignature = ComputeSignature();
        IsDirty = false;
        NotifyCommandStates();
    }

    private void ApplyNoInstanceState()
    {
        ResetToEmptyState(
            "还没有选择游戏实例",
            "在「实例」页选一个实例后回到这里，音乐插件才知道读写哪个 Playlists/playlists.cfg。");
        ShowCreateEntry = false;
        IsModInstalled = false;
        ModStatusText = "未找到";
    }

    private void ApplyServiceMissingState()
    {
        ResetToEmptyState(
            "播放列表服务未就绪",
            "音乐插件没有拿到 ISoundtrackService，暂时无法读写 playlists.cfg。这通常是宿主没有注册该服务。");
        ShowCreateEntry = false;
        IsModInstalled = false;
        ModStatusText = "未找到";
    }

    private void ResetToEmptyState(string title, string detail)
    {
        _document = null;
        Playlists.Clear();
        _libraryAll.Clear();
        LibraryRows.Clear();
        SelectedPlaylist = null;
        ShowContent = false;
        ShowEmptyState = true;
        EmptyTitle = title;
        EmptyDetail = detail;
        RefreshCounts();
        RefreshDirty();
        NotifyCommandStates();
    }

    private void ApplyLibraryFilter()
    {
        var query = string.IsNullOrWhiteSpace(LibrarySearch) ? string.Empty : LibrarySearch.Trim();
        LibraryRows.Clear();
        foreach (var row in _libraryAll)
        {
            if (query.Length == 0
                || row.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                || row.FullPath.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                LibraryRows.Add(row);
            }
        }

        RefreshLibraryAddedFlags();
        LibrarySummaryText = _libraryAll.Count == 0
            ? "没有扫描到音频文件"
            : $"乐曲库 {_libraryAll.Count} 首，当前显示 {LibraryRows.Count} 首";
        OnPropertyChanged(nameof(HasLibraryRows));
    }

    /// <summary>把“这首已在当前播放列表里”标到乐曲库行上，避免用户重复点添加才看到提示。</summary>
    private void RefreshLibraryAddedFlags()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (_selectedPlaylist is not null)
        {
            foreach (var track in _selectedPlaylist.Model.Tracks)
            {
                names.Add(track.Name);
            }
        }

        foreach (var row in _libraryAll)
        {
            row.IsAdded = names.Contains(row.Name);
        }
    }

    private void SyncSelectionDetails()
    {
        SelectedTracks.Clear();
        if (_selectedPlaylist is not null)
        {
            foreach (var track in _selectedPlaylist.Model.Tracks)
            {
                SelectedTracks.Add(new SoundtrackTrackRowViewModel(track, OpenTrackInExplorer));
            }
        }

        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectedPlaylistName));
        OnPropertyChanged(nameof(SelectedLoop));
        OnPropertyChanged(nameof(SelectedShuffle));
        OnPropertyChanged(nameof(SelectedPauseOnGamePause));
        OnPropertyChanged(nameof(SelectedDisableAfterPlay));
        OnPropertyChanged(nameof(SelectedTrackCountText));
        OnPropertyChanged(nameof(HasSelectedTracks));
        RefreshLibraryAddedFlags();
    }

    private void RefreshCounts()
    {
        var playlistCount = _document?.Playlists.Count ?? 0;
        var trackCount = _document?.Playlists.Sum(playlist => playlist.Tracks.Count) ?? 0;
        var missing = _document?.MissingTrackCount ?? 0;
        PlaylistCountText = $"{playlistCount} 个";
        TrackCountText = $"{trackCount} 首";
        MissingTrackCountText = missing == 0 ? "无缺失" : $"{missing} 首缺失";
        OnPropertyChanged(nameof(SelectedTrackCountText));
    }

    private void RefreshViewState()
    {
        var hasPlaylists = _document is not null && _document.Playlists.Count > 0;
        ShowContent = hasPlaylists;
        ShowEmptyState = !hasPlaylists;
        if (hasPlaylists)
        {
            ShowCreateEntry = false;
            EmptyTitle = string.Empty;
            EmptyDetail = string.Empty;
            return;
        }

        if (_document is null)
        {
            return;
        }

        ShowCreateEntry = true;
        if (_document.FileExists)
        {
            EmptyTitle = "playlists.cfg 里还没有播放列表";
            EmptyDetail = $"文件在 {PlaylistFilePath}，但里面一个 playlist 节点都没有。新建一个并保存，Soundtrack 就能读到它。";
        }
        else
        {
            EmptyTitle = "这个实例还没有 Playlists/playlists.cfg";
            EmptyDetail = $"保存时会自动创建目录和文件：{PlaylistFilePath}";
        }
    }

    private void RefreshDirty()
    {
        if (_document is null)
        {
            IsDirty = false;
            return;
        }

        IsDirty = !string.Equals(_baselineSignature, ComputeSignature(), StringComparison.Ordinal);
    }

    /// <summary>只把页面能改的字段算进签名；未知节点与注释不会因为界面操作变脏。</summary>
    private string ComputeSignature()
    {
        if (_document is null)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (var playlist in _document.Playlists)
        {
            builder.Append(playlist.Name).Append('')
                .Append(playlist.Enabled?.ToString() ?? "none").Append('')
                .Append(playlist.Loop).Append('')
                .Append(playlist.Shuffle).Append('')
                .Append(playlist.PauseOnGamePause).Append('')
                .Append(playlist.DisableAfterPlay).Append('');
            foreach (var track in playlist.Tracks)
            {
                builder.Append(track.Name).Append('');
            }

            builder.Append('');
        }

        return builder.ToString();
    }

    private bool TryValidate(out string error)
    {
        if (_document is null)
        {
            error = "没有可保存的播放列表。";
            return false;
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var playlist in _document.Playlists)
        {
            if (string.IsNullOrWhiteSpace(playlist.Name))
            {
                error = "有播放列表的名字是空的。Soundtrack 读不了没有 name 的 playlist，先补一个名字。";
                return false;
            }

            if (!names.Add(playlist.Name.Trim()))
            {
                error = $"播放列表名字重复：「{playlist.Name.Trim()}」。保存前请改成不同的名字。";
                return false;
            }
        }

        error = string.Empty;
        return true;
    }

    private void MoveTrack(int from, int to)
    {
        if (_selectedPlaylist is null)
        {
            return;
        }

        var tracks = _selectedPlaylist.Model.Tracks;
        var track = tracks[from];
        tracks.RemoveAt(from);
        tracks.Insert(to, track);
        SelectedTracks.Move(from, to);
        RefreshDirty();
        ShowStatus("曲目顺序已调整，保存后生效。");
    }

    private string MakeUniquePlaylistName()
    {
        const string baseName = "新建播放列表";
        var existing = new HashSet<string>(Playlists.Select(row => row.Name), StringComparer.OrdinalIgnoreCase);
        if (!existing.Contains(baseName))
        {
            return baseName;
        }

        for (var index = 2; ; index++)
        {
            var candidate = $"{baseName} {index}";
            if (!existing.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    private SoundtrackPlaylistRowViewModel CreatePlaylistRow(SoundtrackPlaylist playlist)
        => new(playlist, OnPlaylistRowChanged);

    private void OnPlaylistRowChanged(SoundtrackPlaylistRowViewModel row)
    {
        RefreshDirty();
        RefreshCounts();
        if (ReferenceEquals(row, _selectedPlaylist))
        {
            OnPropertyChanged(nameof(SelectedPlaylistName));
        }
    }

    private void OnCurrentChanged(InstanceInfo? current)
    {
        if (_disposed)
        {
            return;
        }

        if (IsDirty)
        {
            // 有未保存改动时静默换实例等于替用户丢弃修改：只更新实例名，文档留给用户自己决定。
            InstanceName = string.IsNullOrWhiteSpace(current?.Name) ? "未选择实例" : current.Name;
            ShowError("当前实例已经切换，但页面上还有未保存的修改。先保存或「放弃更改」，页面才会切到新实例的播放列表。");
            return;
        }

        _ = LoadCoreAsync();
    }

    private void OpenTrackInExplorer(SoundtrackTrackRowViewModel row)
    {
        if (row.Model.FilePath is not { Length: > 0 } path)
        {
            ShowError($"「{row.Name}」在磁盘上找不到对应文件，没有可以打开的路径。");
            return;
        }

        try
        {
            _openPath(path);
            ShowStatus($"已在资源管理器里定位「{row.Name}」。");
        }
        catch (Exception ex)
        {
            ShowError($"打开资源管理器失败：{ex.Message}");
        }
    }

    /// <summary>UseShellExecute 交给系统决定怎么打开；只用来定位文件，不接管子进程生命周期。</summary>
    private static void OpenWithShell(string path)
    {
        using var process = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    private static string ResolvePlaylistPath(string? gameDir)
        => string.IsNullOrWhiteSpace(gameDir)
            ? "—"
            : Path.Combine(gameDir, PlaylistsFolderName, PlaylistFileName);

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
        CreatePlaylistCommand.NotifyCanExecuteChanged();
        CreatePlaylistAndSaveCommand.NotifyCanExecuteChanged();
        DeletePlaylistCommand.NotifyCanExecuteChanged();
        ReloadCommand.NotifyCanExecuteChanged();
        SaveCommand.NotifyCanExecuteChanged();
        DiscardChangesCommand.NotifyCanExecuteChanged();
        AddTrackCommand.NotifyCanExecuteChanged();
        MoveTrackUpCommand.NotifyCanExecuteChanged();
        MoveTrackDownCommand.NotifyCanExecuteChanged();
        RemoveTrackCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanInteract));
    }
}
