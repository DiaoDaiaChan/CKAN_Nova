using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using CkanUI.Plugin.Soundtrack.Playlists;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Audio;
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
    private readonly IMusicPlayerService? _musicPlayer;
    private readonly ISoundEffectService? _soundEffects;
    private readonly Action<string> _openPath;
    private readonly List<SoundtrackLibraryRowViewModel> _libraryAll = new();

    private SoundtrackDocument? _document;
    private SoundtrackPlaylistRowViewModel? _selectedPlaylist;
    private string _baselineSignature = string.Empty;
    private bool _isDirty;
    private bool _started;
    private bool _disposed;

    /// <summary>
    /// 宿主用 ActivatorUtilities 构造：服务都从 DI 取。参数允许为 null，是因为插件服务要由宿主注册；
    /// 没注册时页面至少能打开并说清缺什么，而不是让 PluginPageFactory 构造失败后静默地少一个页签。
    /// </summary>
    public SoundtrackPageViewModel(
        ISoundtrackService? soundtrack,
        IInstanceService? instances,
        IMusicPlayerService? musicPlayer = null,
        ISoundEffectService? soundEffects = null)
        : this(soundtrack, instances, musicPlayer, soundEffects, OpenWithShell)
    {
    }

    /// <summary>
    /// 宿主只注册了实例服务时的构造：播放列表服务由插件自己造——它读写的这个文件只有本插件懂，
    /// 让宿主去注册一个插件内部实现是本末倒置。
    /// </summary>
    public SoundtrackPageViewModel(
        IInstanceService instances,
        IMusicPlayerService? musicPlayer = null,
        ISoundEffectService? soundEffects = null)
        : this(new SoundtrackService(), instances, musicPlayer, soundEffects)
    {
    }

    private SoundtrackPageViewModel(
        ISoundtrackService? soundtrack,
        IInstanceService? instances,
        IMusicPlayerService? musicPlayer,
        ISoundEffectService? soundEffects,
        Action<string> openPath)
    {
        _soundtrack = soundtrack;
        _instances = instances;
        _musicPlayer = musicPlayer;
        _soundEffects = soundEffects;
        _openPath = openPath;

        if (_musicPlayer is not null)
        {
            _musicPlayer.PlaybackStateChanged += OnPlaybackStateChanged;
            _musicPlayer.TrackChanged += OnTrackChanged;
            _musicPlayer.PositionChanged += OnPositionChanged;
        }

        if (_instances is not null)
        {
            _instances.CurrentChanged += OnCurrentChanged;
        }

        RefreshBodyNameOptions();
        RebuildNewConditionEditor(NewConditionField);
    }

    /// <summary>
    /// 单测入口：正常路径只由宿主构造，测试不该为了拿到实例服务去实现整套 IInstanceService；
    /// 同时把“打开资源管理器”换成回调，用例可以断言失败提示而不用真的弹窗。
    /// </summary>
    public static SoundtrackPageViewModel CreateForTests(
        ISoundtrackService soundtrack,
        IInstanceService? instances = null,
        IMusicPlayerService? musicPlayer = null,
        ISoundEffectService? soundEffects = null,
        Action<string>? openPath = null)
        => new(soundtrack, instances, musicPlayer, soundEffects, openPath ?? OpenWithShell);

    /// <summary>节点里点一个播放列表：只切换选中，不改任何文档字段。</summary>
    [RelayCommand]
    private void SelectPlaylist(SoundtrackPlaylistRowViewModel? row)
    {
        if (row is not null)
        {
            SelectedPlaylist = row;
            _soundEffects?.Play(SoundEffectType.Select);
        }
    }

    /// <summary>展开/收起一个场景分组。展开状态只活在页面里，不写进 playlists.cfg。</summary>
    [RelayCommand]
    private void ToggleGroup(SoundtrackSceneGroupViewModel? group)
    {
        if (group is not null)
        {
            group.IsExpanded = !group.IsExpanded;
            _soundEffects?.Play(SoundEffectType.Click);
        }
    }

    /// <summary>展开/收起一个星球子分组。和场景组一样只影响界面，不改 playWhen。</summary>
    [RelayCommand]
    private void ToggleBodyGroup(SoundtrackBodyGroupViewModel? group)
    {
        if (group is not null)
        {
            group.IsExpanded = !group.IsExpanded;
            _soundEffects?.Play(SoundEffectType.Click);
        }
    }

    /// <summary>展开“添加条件”表单。默认收起，避免详情区一进来就多一排行。</summary>
    [RelayCommand]
    private void OpenTriggerEditor()
    {
        if (_selectedPlaylist is null)
        {
            ShowError("先在左栏选一个播放列表，再添加条件。");
            return;
        }

        NewConditionError = string.Empty;
        IsTriggerEditorOpen = true;
        _soundEffects?.Play(SoundEffectType.Click);
    }

    [RelayCommand]
    private void CancelTriggerEditor()
    {
        IsTriggerEditorOpen = false;
        NewConditionError = string.Empty;
        _soundEffects?.Play(SoundEffectType.Click);
    }

    /// <summary>
    /// 把表单里的键值加到选中列表的 playWhen。值和键都先过目录校验：
    /// Soundtrack 遇到不认识的枚举会抛异常并整份回落原版播放列表，所以宁可在这里拦住。
    /// </summary>
    [RelayCommand]
    private void AddCondition()
    {
        if (_selectedPlaylist is null)
        {
            ShowError("先在左栏选一个播放列表，再添加条件。");
            return;
        }

        var field = NewConditionField;
        if (field is null)
        {
            NewConditionError = "先选一个要添加的键。";
            return;
        }

        if (_selectedPlaylist.Model.PlayWhen.Any(pair =>
                pair.Key.Equals(field.Key, StringComparison.OrdinalIgnoreCase)))
        {
            NewConditionError = $"「{field.Label}」已经有一条了，直接改下面那条的值。";
            return;
        }

        if (!SoundtrackTriggerCatalog.TryNormalize(field, NewConditionValue, out var normalized, out var error))
        {
            NewConditionError = error;
            _soundEffects?.Play(SoundEffectType.Warning);
            return;
        }

        _selectedPlaylist.ApplyCondition(field.Key, normalized);
        IsTriggerEditorOpen = false;
        NewConditionError = string.Empty;
        RefreshConditionRows();
        _soundEffects?.Play(SoundEffectType.Check);
        ShowStatus($"已给「{_selectedPlaylist.Name}」加上条件 {field.Key} = {normalized}，保存后生效。");
    }

    /// <summary>应用一条条件的草稿值；同一行可以反复改，模型里同键只留一条。</summary>
    [RelayCommand]
    private void ApplyCondition(SoundtrackConditionRowViewModel? row)
    {
        if (row is null || _selectedPlaylist is null)
        {
            return;
        }

        if (!SoundtrackTriggerCatalog.TryNormalize(row.Field, row.Draft, out var normalized, out var error))
        {
            ShowError($"{row.Label}：{error}");
            _soundEffects?.Play(SoundEffectType.Warning);
            return;
        }

        _selectedPlaylist.ApplyCondition(row.Key, normalized);
        if (string.Equals(row.Key, "scene", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(row.Key, "bodyName", StringComparison.OrdinalIgnoreCase))
        {
            RebuildTriggerTree();
        }
        RefreshConditionRows();
        _soundEffects?.Play(SoundEffectType.Check);
        ShowStatus($"已把「{_selectedPlaylist.Name}」的 {row.Key} 改成 {normalized}，保存后生效。");
    }

    /// <summary>删掉一条条件。删掉 scene 会让列表落回「未限定场景」分组，树会跟着重排。</summary>
    [RelayCommand]
    private void RemoveCondition(SoundtrackConditionRowViewModel? row)
    {
        if (row is null || _selectedPlaylist is null)
        {
            return;
        }

        if (!_selectedPlaylist.RemoveCondition(row.Key))
        {
            return;
        }

        if (string.Equals(row.Key, "scene", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(row.Key, "bodyName", StringComparison.OrdinalIgnoreCase))
        {
            RebuildTriggerTree();
        }
        RefreshConditionRows();
        _soundEffects?.Play(SoundEffectType.Click);
        ShowStatus($"已从「{_selectedPlaylist.Name}」移除条件 {row.Key}，保存后生效。");
    }

    /// <summary>左栏的播放列表行，顺序即 playlists.cfg 里的节点顺序。</summary>
    public ObservableCollection<SoundtrackPlaylistRowViewModel> Playlists { get; } = new();

    /// <summary>
    /// 节点树：一级节点按 scene 分组，组内是播放列表。
    /// </summary>
    /// <remarks>
    /// 分组结果和 <see cref="Playlists"/> 是同一批行对象，不是副本——改名/开关时两边一起变，
    /// 否则节点树会显示旧名字，比不做树还糟。
    /// </remarks>
    public ObservableCollection<SoundtrackSceneGroupViewModel> TriggerGroups { get; } = new();

    /// <summary>右栏当前选中播放列表的曲目行，顺序即播放顺序。</summary>
    public ObservableCollection<SoundtrackTrackRowViewModel> SelectedTracks { get; } = new();

    /// <summary>乐曲库里当前可见的行（受 <see cref="LibrarySearch"/> 过滤）。</summary>
    public ObservableCollection<SoundtrackLibraryRowViewModel> LibraryRows { get; } = new();

    /// <summary>右栏可编辑的条件行，顺序即 playWhen 在文件里的顺序。</summary>
    public ObservableCollection<SoundtrackConditionRowViewModel> SelectedConditions { get; } = new();

    /// <summary>「添加条件」下拉里的全部键，顺序与触发器目录一致。</summary>
    public IReadOnlyList<SoundtrackTriggerField> TriggerFieldOptions => SoundtrackTriggerCatalog.Fields;

    /// <summary>bodyName 选择器的建议值：库存天体 + 当前文档里出现过的天体名。</summary>
    public ObservableCollection<string> BodyNameOptions { get; } = new();

    /// <summary>「添加条件」表单的选择器状态；与 NewConditionValue 双向同步，旧测试仍可直接给 NewConditionValue 赋值。</summary>
    [ObservableProperty]
    private SoundtrackConditionRowViewModel? _newConditionEditor;

    /// <summary>是否展开“添加条件”表单；收起时不占高度。</summary>
    [ObservableProperty]
    private bool _isTriggerEditorOpen;

    [ObservableProperty]
    private SoundtrackTriggerField? _newConditionField = SoundtrackTriggerCatalog.Fields[0];

    [ObservableProperty]
    private string _newConditionValue = SoundtrackTriggerCatalog.Fields[0].DefaultValue;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNewConditionError))]
    private string _newConditionError = string.Empty;

    public bool HasNewConditionError => NewConditionError.Length > 0;

    /// <summary>切换要添加的键时，把值重置成该键的合法默认值，避免留下上一条的枚举名。</summary>
    partial void OnNewConditionFieldChanged(SoundtrackTriggerField? value)
    {
        NewConditionError = string.Empty;
        NewConditionValue = value?.DefaultValue ?? string.Empty;
        RebuildNewConditionEditor(value);
        OnPropertyChanged(nameof(NewConditionHint));
        OnPropertyChanged(nameof(NewConditionOptions));
        OnPropertyChanged(nameof(HasNewConditionOptions));
    }

    /// <summary>把选择器的草稿同步回旧属性，保存/校验仍然只读 NewConditionValue 这一处。</summary>
    partial void OnNewConditionValueChanged(string value)
    {
        if (NewConditionEditor is { } editor
            && !string.Equals(editor.Draft, value, StringComparison.Ordinal))
        {
            editor.Draft = value;
        }
    }

    private void RebuildNewConditionEditor(SoundtrackTriggerField? field)
    {
        if (NewConditionEditor is not null)
        {
            NewConditionEditor.PropertyChanged -= OnNewConditionEditorPropertyChanged;
        }

        NewConditionEditor = field is null
            ? null
            : new SoundtrackConditionRowViewModel(null, field.Key, NewConditionValue);
        if (NewConditionEditor is not null)
        {
            NewConditionEditor.SetBodyOptions(BodyNameOptions);
            NewConditionEditor.PropertyChanged += OnNewConditionEditorPropertyChanged;
        }
    }

    private void OnNewConditionEditorPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SoundtrackConditionRowViewModel.Draft))
        {
            return;
        }

        if (NewConditionEditor is { } editor
            && !string.Equals(NewConditionValue, editor.Draft, StringComparison.Ordinal))
        {
            NewConditionValue = editor.Draft;
        }
    }

    public string NewConditionHint => NewConditionField?.ValueHint ?? string.Empty;

    public IReadOnlyList<SoundtrackTriggerValue> NewConditionOptions =>
        NewConditionField?.Options ?? Array.Empty<SoundtrackTriggerValue>();

    public bool HasNewConditionOptions => NewConditionOptions.Count > 0;

    public bool HasConditionRows => SelectedConditions.Count > 0;

    [ObservableProperty]
    private string _instanceName = "未选择实例";

    [ObservableProperty]
    private string _instanceGameDir = "—";

    [ObservableProperty]
    private string _playlistFilePath = "—";

    [ObservableProperty]
    private string _modStatusText = "未找到";

    /// <summary>页脚用的短状态，完整的模组路径太长，放在页脚会把整行撑成一个路径栏。</summary>
    [ObservableProperty]
    private string _modStatusShortText = "未找到 Soundtrack";

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

    public bool HasTriggerGroups => TriggerGroups.Count > 0;

    /// <summary>整份文档里是否有缺失曲目；页脚只在真缺歌时才挂那个红色徽标。</summary>
    public bool HasMissingTracks => (_document?.MissingTrackCount ?? 0) > 0;

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
                RevealSelection();
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

    /// <summary>详情区的条件原文；没有 playWhen 时也给出可读的替代说明，不留一片空白。</summary>
    public string SelectedPlayWhenText => _selectedPlaylist is null
        ? string.Empty
        : _selectedPlaylist.HasPlayWhen
            ? _selectedPlaylist.RawPlayWhenText
            : "playWhen 里没有写任何键";

    public bool SelectedHasPlayWhen => _selectedPlaylist?.HasPlayWhen == true;

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
        _soundEffects?.Play(SoundEffectType.Click);
        await LoadCoreAsync().ConfigureAwait(true);
    }

    /// <summary>新建一个空播放列表（只有名字，其余键用模型默认值，保存时才落盘）。</summary>
    [RelayCommand(CanExecute = nameof(CanCreatePlaylist))]
    public void CreatePlaylist()
    {
        if (_document is null)
        {
            ShowError("先选择一个游戏实例，才能新建播放列表。");
            _soundEffects?.Play(SoundEffectType.Warning);
            return;
        }

        var playlist = new SoundtrackPlaylist { Name = MakeUniquePlaylistName() };
        _document.Playlists.Add(playlist);
        var row = CreatePlaylistRow(playlist);
        Playlists.Add(row);
        RebuildTriggerTree();
        SelectedPlaylist = row;
        RefreshCounts();
        RefreshViewState();
        RefreshDirty();
        NotifyCommandStates();
        _soundEffects?.Play(SoundEffectType.Check);
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
            _soundEffects?.Play(SoundEffectType.Warning);
            return;
        }

        var removed = _selectedPlaylist;
        var index = Math.Max(0, Playlists.IndexOf(removed));
        _document.Playlists.Remove(removed.Model);
        Playlists.Remove(removed);
        RebuildTriggerTree();
        SelectedPlaylist = Playlists.Count == 0 ? null : Playlists[Math.Min(index, Playlists.Count - 1)];
        RefreshCounts();
        RefreshViewState();
        RefreshDirty();
        NotifyCommandStates();
        _soundEffects?.Play(SoundEffectType.Uncheck);
        ShowStatus($"已删除播放列表「{removed.Name}」，保存后才会从 playlists.cfg 消失。");
    }

    /// <summary>放弃未保存修改：直接按磁盘内容重读，不做内存回滚（服务才是文件的权威）。</summary>
    [RelayCommand(CanExecute = nameof(CanDiscardChanges))]
    public async Task DiscardChangesAsync()
    {
        _soundEffects?.Play(SoundEffectType.Click);
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
            _soundEffects?.Play(SoundEffectType.Warning);
            return;
        }

        if (_document is null)
        {
            ShowError("先选择一个游戏实例，才有地方保存播放列表。");
            _soundEffects?.Play(SoundEffectType.Warning);
            return;
        }

        if (IsBusy)
        {
            return;
        }

        // 先规范化再校验：旧文件里 situation 可能是逗号分隔，规范化后才是合法值。
        if (NormalizePlayWhen())
        {
            RefreshConditionRows();
            OnPropertyChanged(nameof(SelectedPlayWhenText));
        }

        if (!TryValidate(out var validationError))
        {
            ShowError(validationError);
            _soundEffects?.Play(SoundEffectType.Warning);
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
                _soundEffects?.Play(SoundEffectType.Error);
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

            _soundEffects?.Play(SoundEffectType.Ok);
            RefreshViewState();
            NotifyCommandStates();
        }
        catch (Exception ex)
        {
            ShowError($"保存失败：{ex.Message}");
            _soundEffects?.Play(SoundEffectType.Error);
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
        _soundEffects?.Play(SoundEffectType.Check);
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

        _soundEffects?.Play(SoundEffectType.Click);
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

        _soundEffects?.Play(SoundEffectType.Click);
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
        _soundEffects?.Play(SoundEffectType.Uncheck);
        ShowStatus($"已从「{_selectedPlaylist.Name}」移除「{row.Name}」，保存后生效。");
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_musicPlayer is not null)
        {
            _musicPlayer.PlaybackStateChanged -= OnPlaybackStateChanged;
            _musicPlayer.TrackChanged -= OnTrackChanged;
            _musicPlayer.PositionChanged -= OnPositionChanged;
        }

        if (_instances is not null)
        {
            _instances.CurrentChanged -= OnCurrentChanged;
        }
    }

    private void OnPlaybackStateChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(IsPlaying));
        OnPropertyChanged(nameof(IsPaused));
        OnPropertyChanged(nameof(PlayPauseButtonText));
        OnPropertyChanged(nameof(NowPlayingTrackName));
        OnPropertyChanged(nameof(NowPlayingPlaylistName));
        OnPropertyChanged(nameof(NowPlayingFullInfo));
        OnPropertyChanged(nameof(IsPlayerMuted));
        OnPropertyChanged(nameof(PlayerMuteButtonText));
        OnPropertyChanged(nameof(PlayerVolume));
    }

    private void OnTrackChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(NowPlayingTrackName));
        OnPropertyChanged(nameof(NowPlayingPlaylistName));
        OnPropertyChanged(nameof(NowPlayingFullInfo));
        OnPropertyChanged(nameof(Duration));
        OnPropertyChanged(nameof(ProgressText));
        OnPropertyChanged(nameof(ProgressValue));
    }

    private void OnPositionChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(CurrentPosition));
        OnPropertyChanged(nameof(ProgressText));
        OnPropertyChanged(nameof(ProgressValue));
    }

    public bool IsPlaying => _musicPlayer?.IsPlaying == true;
    public bool IsPaused => _musicPlayer?.IsPaused == true;
    public string PlayPauseButtonText => IsPlaying ? "暂停" : (IsPaused ? "继续" : "播放");

    public string NowPlayingTrackName => _musicPlayer?.CurrentTrackName ?? "未在播放";
    public string NowPlayingPlaylistName => !string.IsNullOrEmpty(_musicPlayer?.CurrentPlaylistName)
        ? $"[{_musicPlayer.CurrentPlaylistName}]"
        : string.Empty;
    public string NowPlayingFullInfo => IsPlaying || IsPaused
        ? $"{NowPlayingTrackName} {NowPlayingPlaylistName}".Trim()
        : "无正在播放的曲目";

    public TimeSpan CurrentPosition => _musicPlayer?.Position ?? TimeSpan.Zero;
    public TimeSpan Duration => _musicPlayer?.Duration ?? TimeSpan.Zero;
    public string ProgressText => Duration.TotalSeconds > 0
        ? $"{CurrentPosition:mm\\:ss} / {Duration:mm\\:ss}"
        : "--:-- / --:--";

    public double ProgressValue
    {
        get => _musicPlayer is { Duration.TotalSeconds: > 0 }
            ? Math.Clamp((_musicPlayer.Position.TotalSeconds / _musicPlayer.Duration.TotalSeconds) * 100.0, 0.0, 100.0)
            : 0.0;
        set
        {
            if (_musicPlayer is { Duration.TotalSeconds: > 0 })
            {
                var target = TimeSpan.FromSeconds(Math.Clamp(value / 100.0 * _musicPlayer.Duration.TotalSeconds, 0, _musicPlayer.Duration.TotalSeconds));
                _musicPlayer.Seek(target);
            }
        }
    }

    public double PlayerVolume
    {
        get => (_musicPlayer?.Volume ?? 0.7) * 100.0;
        set
        {
            if (_musicPlayer is not null)
            {
                _musicPlayer.Volume = Math.Clamp(value / 100.0, 0.0, 1.0);
                OnPropertyChanged();
            }
        }
    }

    public bool IsPlayerMuted
    {
        get => _musicPlayer?.IsMuted ?? false;
        set
        {
            if (_musicPlayer is not null)
            {
                _musicPlayer.IsMuted = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(PlayerMuteButtonText));
            }
        }
    }

    public string PlayerMuteButtonText => IsPlayerMuted ? "🔇" : "🔊";

    [RelayCommand]
    public void TogglePlayerMute()
    {
        if (_musicPlayer is not null)
        {
            _musicPlayer.IsMuted = !_musicPlayer.IsMuted;
            OnPropertyChanged(nameof(IsPlayerMuted));
            OnPropertyChanged(nameof(PlayerMuteButtonText));
            _soundEffects?.Play(IsPlayerMuted ? SoundEffectType.Uncheck : SoundEffectType.Check);
        }
    }

    [RelayCommand]
    public void TogglePlayPause()
    {
        if (_musicPlayer is null) return;
        if (_musicPlayer.IsPlaying)
        {
            _musicPlayer.Pause();
            _soundEffects?.Play(SoundEffectType.Click);
        }
        else if (_musicPlayer.IsPaused)
        {
            _musicPlayer.Resume();
            _soundEffects?.Play(SoundEffectType.Click);
        }
        else
        {
            PlaySelectedPlaylist();
        }
    }

    [RelayCommand]
    public void PreviousTrack()
    {
        _musicPlayer?.Previous();
        _soundEffects?.Play(SoundEffectType.Click);
    }

    [RelayCommand]
    public void NextTrack()
    {
        _musicPlayer?.Next();
        _soundEffects?.Play(SoundEffectType.Click);
    }

    [RelayCommand]
    public void StopMusic()
    {
        _musicPlayer?.Stop();
        _soundEffects?.Play(SoundEffectType.Click);
    }

    [RelayCommand]
    public void PlaySelectedPlaylist()
    {
        var validTracks = SelectedTracks.Where(t => t.Exists && !string.IsNullOrEmpty(t.Model.FilePath)).ToList();
        if (validTracks.Count > 0)
        {
            _musicPlayer?.PlayPlaylist(
                validTracks.Select(t => t.Model.FilePath!),
                validTracks.Select(t => t.Name),
                0,
                SelectedPlaylistName);
            _soundEffects?.Play(SoundEffectType.Ok);
            ShowStatus($"正在播放列表「{SelectedPlaylistName}」（共 {validTracks.Count} 首曲目）。");
        }
        else
        {
            _soundEffects?.Play(SoundEffectType.Warning);
            ShowStatus("当前选中的播放列表没有可在磁盘上找到的有效音频文件。");
        }
    }

    [RelayCommand]
    public void PlayTrack(SoundtrackTrackRowViewModel? track)
    {
        if (track is null || !track.Exists || string.IsNullOrEmpty(track.Model.FilePath))
        {
            _soundEffects?.Play(SoundEffectType.Warning);
            ShowStatus("此曲目文件在磁盘上不存在，无法播放。");
            return;
        }

        var validTracks = SelectedTracks.Where(t => t.Exists && !string.IsNullOrEmpty(t.Model.FilePath)).ToList();
        int index = validTracks.IndexOf(track);
        if (index >= 0)
        {
            _musicPlayer?.PlayPlaylist(
                validTracks.Select(t => t.Model.FilePath!),
                validTracks.Select(t => t.Name),
                index,
                SelectedPlaylistName);
        }
        else
        {
            _musicPlayer?.Play(track.Model.FilePath!, track.Name, SelectedPlaylistName);
        }

        _soundEffects?.Play(SoundEffectType.Select);
        ShowStatus($"正在播放曲目「{track.Name}」。");
    }

    [RelayCommand]
    public void PlayLibraryTrack(SoundtrackLibraryRowViewModel? row)
    {
        if (row is null || !File.Exists(row.FullPath))
        {
            _soundEffects?.Play(SoundEffectType.Warning);
            ShowStatus("所选音频文件不存在。");
            return;
        }

        _musicPlayer?.Play(row.FullPath, row.Name, "音乐库");
        _soundEffects?.Play(SoundEffectType.Select);
        ShowStatus($"正在播放「{row.Name}」。");
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
        RefreshBodyNameOptions();

        // 文档里的路径比页面拼出来的更权威：服务可能按 Core 的实例布局解析到别的位置。
        PlaylistFilePath = string.IsNullOrWhiteSpace(document.FilePath)
            ? ResolvePlaylistPath(document.GameDir)
            : document.FilePath;
        IsModInstalled = !string.IsNullOrWhiteSpace(document.ModDirectory);
        ModStatusText = IsModInstalled
            ? $"已安装（{document.ModDirectory}）"
            : "未找到（GameData 下没有 Soundtrack 目录）";
        ModStatusShortText = IsModInstalled ? "Soundtrack 已安装" : "未找到 Soundtrack";

        Playlists.Clear();
        foreach (var playlist in document.Playlists)
        {
            Playlists.Add(CreatePlaylistRow(playlist));
        }

        RebuildTriggerTree();

        _libraryAll.Clear();
        foreach (var file in document.MusicLibrary)
        {
            _libraryAll.Add(new SoundtrackLibraryRowViewModel(file));
        }

        _libraryAll.Sort((left, right) => string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase));
        ApplyLibraryFilter();

        // 初始选中取节点树第一行，而不是 cfg 里的第一个节点：树按场景重排过，
        // 按文件顺序选会让右栏编辑的列表落在树的中段，一进页面就看不见选中的是谁。
        SelectedPlaylist = TriggerGroups.SelectMany(group => group.RowsInDisplayOrder).FirstOrDefault()
            ?? (Playlists.Count > 0 ? Playlists[0] : null);
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
        ModStatusShortText = "未找到 Soundtrack";
    }

    private void ApplyServiceMissingState()
    {
        ResetToEmptyState(
            "播放列表服务未就绪",
            "音乐插件没有拿到 ISoundtrackService，暂时无法读写 playlists.cfg。这通常是宿主没有注册该服务。");
        ShowCreateEntry = false;
        IsModInstalled = false;
        ModStatusText = "未找到";
        ModStatusShortText = "未找到 Soundtrack";
    }

    private void ResetToEmptyState(string title, string detail)
    {
        _document = null;
        Playlists.Clear();
        RebuildTriggerTree();
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

    /// <summary>独立乐曲库窗口读取同一批行对象，加入后右键/列表里的「已加入」状态会同步回来。</summary>
    public IReadOnlyList<SoundtrackLibraryRowViewModel> AllLibraryRows => _libraryAll;

    /// <summary>天体名建议值：库存天体 + 文档里出现过的 bodyName；条件行和添加表单共用。</summary>
    private void RefreshBodyNameOptions()
    {
        var bodies = new List<string>(SoundtrackTriggerCatalog.BodyNames);
        if (_document is not null)
        {
            foreach (var playlist in _document.Playlists)
            {
                foreach (var pair in playlist.PlayWhen)
                {
                    if (pair.Key.Equals(SoundtrackTriggers.BodyNameKey, StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrWhiteSpace(pair.Value))
                    {
                        bodies.Add(pair.Value.Trim());
                    }
                }
            }
        }

        BodyNameOptions.Clear();
        foreach (var body in bodies
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(body => body, StringComparer.OrdinalIgnoreCase))
        {
            BodyNameOptions.Add(body);
        }

        NewConditionEditor?.SetBodyOptions(BodyNameOptions);
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
        OnPropertyChanged(nameof(SelectedPlayWhenText));
        OnPropertyChanged(nameof(SelectedHasPlayWhen));
        OnPropertyChanged(nameof(HasSelectedTracks));
        RefreshLibraryAddedFlags();
        RefreshConditionRows();
        IsTriggerEditorOpen = false;
        NewConditionError = string.Empty;
    }

    /// <summary>
    /// 按 scene 重建节点树。组内保持 cfg 顺序，因为 Soundtrack 同时命中多份时也是按文件顺序排队。
    /// </summary>
    /// <param name="preserveExpansion">条件编辑后重建时保留用户手动收起的分组，否则每次应用条件树都会全部弹开。</param>
    private void RebuildTriggerTree(bool preserveExpansion = false)
    {
        var collapsed = preserveExpansion
            ? TriggerGroups.Where(group => !group.IsExpanded).Select(group => group.Key).ToHashSet(StringComparer.Ordinal)
            : null;
        var collapsedBodies = preserveExpansion
            ? TriggerGroups.ToDictionary(
                group => group.Key,
                group => (ISet<string>)group.BodyGroups
                    .Where(body => !body.IsExpanded)
                    .Select(body => body.Key)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase),
                StringComparer.Ordinal)
            : null;

        TriggerGroups.Clear();
        foreach (var bucket in Playlists
                     .GroupBy(row => row.SceneGroupKey)
                     .OrderBy(bucket => SoundtrackScenes.GroupOrder(bucket.Key)))
        {
            var group = new SoundtrackSceneGroupViewModel(bucket.Key)
            {
                IsExpanded = collapsed is null || !collapsed.Contains(bucket.Key),
            };
            foreach (var row in bucket)
            {
                group.Playlists.Add(row);
            }

            group.RebuildBodyGroups(
                collapsedBodies is not null && collapsedBodies.TryGetValue(bucket.Key, out var bodyCollapsed)
                    ? bodyCollapsed
                    : null);
            TriggerGroups.Add(group);
        }

        OnPropertyChanged(nameof(HasTriggerGroups));
    }

    /// <summary>
    /// 按 playWhen 的当前内容重建右栏条件行。同一个键写了两遍时只显示一条（值取最后一条），
    /// 与 Soundtrack 的 Upsert 读取保持一致。
    /// </summary>
    private void RefreshConditionRows()
    {
        SelectedConditions.Clear();
        if (_selectedPlaylist is not null)
        {
            var order = new List<string>();
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in _selectedPlaylist.Model.PlayWhen)
            {
                if (!values.ContainsKey(pair.Key))
                {
                    order.Add(pair.Key);
                }

                values[pair.Key] = pair.Value;
            }

            foreach (var key in order)
            {
                var row = new SoundtrackConditionRowViewModel(_selectedPlaylist, key, values[key]);
                row.SetBodyOptions(BodyNameOptions);
                SelectedConditions.Add(row);
            }
        }

        OnPropertyChanged(nameof(HasConditionRows));
    }

    /// <summary>从详情/命令切到某个列表时，把它所在的分组展开，否则用户会以为点击没反应。</summary>
    private void RevealSelection()
    {
        if (_selectedPlaylist is null)
        {
            return;
        }

        foreach (var group in TriggerGroups)
        {
            if (!group.Playlists.Contains(_selectedPlaylist))
            {
                continue;
            }

            group.IsExpanded = true;
            foreach (var body in group.BodyGroups)
            {
                if (body.Playlists.Contains(_selectedPlaylist))
                {
                    body.IsExpanded = true;
                    break;
                }
            }

            return;
        }
    }

    private void RefreshCounts()
    {
        var playlistCount = _document?.Playlists.Count ?? 0;
        var trackCount = _document?.Playlists.Sum(playlist => playlist.Tracks.Count) ?? 0;
        var missing = _document?.MissingTrackCount ?? 0;
        PlaylistCountText = $"{playlistCount} 个";
        TrackCountText = $"{trackCount} 首";
        MissingTrackCountText = missing == 0 ? "无缺失" : $"{missing} 首缺失";
        OnPropertyChanged(nameof(HasMissingTracks));
        OnPropertyChanged(nameof(SelectedTrackCountText));
        foreach (var group in TriggerGroups)
        {
            group.NotifyCountsChanged();
        }
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
            // playWhen 也算用户可改的字段：少了它，改完条件页面会假装“已与磁盘一致”。
            foreach (var condition in playlist.PlayWhen)
            {
                builder.Append("[when]").Append(condition.Key).Append('=').Append(condition.Value).Append(';');
            }

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

        if (!TryValidatePlayWhen(out error))
        {
            return false;
        }

        error = string.Empty;
        return true;
    }

    /// <summary>
    /// 校验所有 playWhen 的值。Soundtrack 的 Enums.Parse 遇到不认识的枚举会抛异常，
    /// 外层 catch 会把整份文件回落到原版播放列表——所以这里必须拦，不能交给游戏去发现。
    /// </summary>
    private bool TryValidatePlayWhen(out string error)
    {
        error = string.Empty;
        if (_document is null)
        {
            return true;
        }

        foreach (var playlist in _document.Playlists)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in playlist.PlayWhen)
            {
                values[pair.Key] = pair.Value;
            }

            foreach (var pair in playlist.PlayWhen)
            {
                var field = SoundtrackTriggerCatalog.Find(pair.Key);
                if (field is null)
                {
                    // 不认识的键 Soundtrack 会忽略；页面原样保留，不拦保存。
                    continue;
                }

                if (!SoundtrackTriggerCatalog.TryNormalize(field, pair.Value, out _, out var reason))
                {
                    error = $"播放列表「{playlist.Name}」的条件 {pair.Key} 填得不对：{reason}";
                    return false;
                }
            }

            foreach (var range in SoundtrackTriggers.Ranges)
            {
                if (!values.TryGetValue(range.MinKey, out var minRaw)
                    || !values.TryGetValue(range.MaxKey, out var maxRaw)
                    || !float.TryParse(minRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out var min)
                    || !float.TryParse(maxRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out var max))
                {
                    continue;
                }

                if (min > max)
                {
                    error = $"播放列表「{playlist.Name}」的{range.Label}下限 {minRaw} 大于上限 {maxRaw}。";
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// 把可编辑键的值统一成 Soundtrack 能读的写法（多选一律 A | B）。
    /// 旧版 SoundtrackEditor 会把 situation 写成 "LANDED, ORBITING"，游戏读到就整份回落；
    /// 用户在页面里保存一次，顺手把这个坑修掉。
    /// </summary>
    private bool NormalizePlayWhen()
    {
        if (_document is null)
        {
            return false;
        }

        var changed = false;
        foreach (var playlist in _document.Playlists)
        {
            for (var index = 0; index < playlist.PlayWhen.Count; index++)
            {
                var pair = playlist.PlayWhen[index];
                var field = SoundtrackTriggerCatalog.Find(pair.Key);
                if (field is null
                    || !SoundtrackTriggerCatalog.TryNormalize(field, pair.Value, out var normalized, out _))
                {
                    continue;
                }

                if (!string.Equals(pair.Value, normalized, StringComparison.Ordinal))
                {
                    playlist.PlayWhen[index] = new KeyValuePair<string, string>(pair.Key, normalized);
                    changed = true;
                }
            }
        }

        return changed;
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
    {
        var row = new SoundtrackPlaylistRowViewModel(playlist, OnPlaylistRowChanged);
        row.TriggersChanged += OnPlaylistTriggersChanged;
        return row;
    }

    private void OnPlaylistRowChanged(SoundtrackPlaylistRowViewModel row)
    {
        RefreshDirty();
        RefreshCounts();
        if (ReferenceEquals(row, _selectedPlaylist))
        {
            OnPropertyChanged(nameof(SelectedPlaylistName));
        }
    }

    /// <summary>
    /// playWhen 改动后：分组可能变了（删掉 scene 会落回未限定组），所以要重建树；
    /// 选中列表的条件行和原文也要跟着换，否则用户看到的是改之前的照片。
    /// </summary>
    private void OnPlaylistTriggersChanged(object? sender, EventArgs e)
    {
        RebuildTriggerTree(preserveExpansion: true);
        RefreshCounts();
        RefreshDirty();
        if (ReferenceEquals(sender, _selectedPlaylist))
        {
            RefreshConditionRows();
            OnPropertyChanged(nameof(SelectedPlayWhenText));
            OnPropertyChanged(nameof(SelectedHasPlayWhen));
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
