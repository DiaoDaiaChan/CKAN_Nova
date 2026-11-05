using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using CkanUI.Plugin.Soundtrack.Playlists;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CkanUI.Plugin.Soundtrack.UI;

/// <summary>
/// 左栏的播放列表行。
/// </summary>
/// <remarks>
/// 行不复制模型字段，只做转发：改开关/名字最终落在 <see cref="SoundtrackPlaylist"/> 上，
/// 页面才会用同一份对象去保存。行里保留一个 Changed 事件，是因为页面需要在任何一个可编辑字段变化时
/// 统一重算脏状态，而不是在每个输入框的 setter 里各写一遍。
/// </remarks>
public sealed class SoundtrackPlaylistRowViewModel : ObservableObject
{
    private readonly Action<SoundtrackPlaylistRowViewModel> _changed;
    private readonly bool _enabledUnspecified;
    private IReadOnlyList<SoundtrackFlowNode> _flowNodes = Array.Empty<SoundtrackFlowNode>();
    private bool _isSelected;
    private bool _isExpanded = true;

    public SoundtrackPlaylistRowViewModel(SoundtrackPlaylist playlist, Action<SoundtrackPlaylistRowViewModel> changed)
    {
        Model = playlist ?? throw new ArgumentNullException(nameof(playlist));
        _changed = changed ?? throw new ArgumentNullException(nameof(changed));
        _enabledUnspecified = playlist.Enabled is null;
        RefreshTriggerViews();
    }

    /// <summary>playWhen 被编辑后触发。页面靠它重建场景分组、刷新条件列表，而不是等下一次载入。</summary>
    public event EventHandler? TriggersChanged;

    public SoundtrackPlaylist Model { get; }

    public string Name
    {
        get => Model.Name;
        set
        {
            if (string.Equals(Model.Name, value, StringComparison.Ordinal))
            {
                return;
            }

            Model.Name = value;
            OnPropertyChanged();
            _changed(this);
        }
    }

    /// <summary>和原文件保持三态：null 表示 cfg 里没写这个键，界面不要擅自写成 False。</summary>
    public bool? Enabled
    {
        get => Model.Enabled;
        set
        {
            if (Model.Enabled == value)
            {
                return;
            }

            Model.Enabled = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsEnabled));
            _changed(this);
        }
    }

    /// <summary>
    /// 卡片上的开关用两态显示。cfg 没写 enabled 时 Soundtrack 按开启处理（Playlist.enabled 默认 true），
    /// 所以这一格显示为开；只有用户真的拨动，模型才写成显式的 True/False。
    /// </summary>
    public bool IsEnabled
    {
        get => Model.Enabled ?? true;
        set
        {
            if ((Model.Enabled ?? true) == value)
            {
                return;
            }

            // 原本没写 enabled 的列表：关掉写 False；再打开则回到 null，保持「没写」的原样，
            // 否则用户来回拨一下就会平白多出一行 enabled = True，脏标记也回不到基线。
            Model.Enabled = value ? (_enabledUnspecified ? null : true) : false;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Enabled));
            _changed(this);
        }
    }

    public bool Loop
    {
        get => Model.Loop;
        set
        {
            if (Model.Loop == value)
            {
                return;
            }

            Model.Loop = value;
            OnPropertyChanged();
            _changed(this);
        }
    }

    public bool Shuffle
    {
        get => Model.Shuffle;
        set
        {
            if (Model.Shuffle == value)
            {
                return;
            }

            Model.Shuffle = value;
            OnPropertyChanged();
            _changed(this);
        }
    }

    public bool PauseOnGamePause
    {
        get => Model.PauseOnGamePause;
        set
        {
            if (Model.PauseOnGamePause == value)
            {
                return;
            }

            Model.PauseOnGamePause = value;
            OnPropertyChanged();
            _changed(this);
        }
    }

    public bool DisableAfterPlay
    {
        get => Model.DisableAfterPlay;
        set
        {
            if (Model.DisableAfterPlay == value)
            {
                return;
            }

            Model.DisableAfterPlay = value;
            OnPropertyChanged();
            _changed(this);
        }
    }

    public int TrackCount => Model.Tracks.Count;

    public string TrackCountText => $"{Model.Tracks.Count} 首";

    public string SceneSummary => Model.SceneSummary;

    /// <summary>节点行内的条件徽标：scene 已经是分组标题，这里不再重复，只显示“除此之外还有什么”。</summary>
    public IReadOnlyList<SoundtrackCondition> Conditions { get; private set; } = Array.Empty<SoundtrackCondition>();

    /// <summary>详情区的完整条件链，含 scene——用户点开一个列表时要能一眼看全判定条件。</summary>
    public IReadOnlyList<SoundtrackCondition> AllConditions { get; private set; } = Array.Empty<SoundtrackCondition>();

    public bool HasConditions => Conditions.Count > 0;

    /// <summary>
    /// 流程画布上的一条链：曲目 → 星球 → 其余条件。scene 是画布上的根节点，链上不重复；
    /// 条件多于三条时收成一条「还有 N 项」，避免一条链把整行撑爆。
    /// </summary>
    public IReadOnlyList<SoundtrackFlowNode> FlowNodes => _flowNodes;

    /// <summary>
    /// 没有额外条件时的一句话。场景已经是分组标题，所以这里不能笼统说“任何场景”——
    /// 挂在「主菜单」组下的列表其实只在该场景播。
    /// </summary>
    public string ConditionFallbackText => SceneGroupKey.Length == 0
        ? "没有其他条件：任何场景、任何时段都会播"
        : "只看场景，不限时段";

    /// <summary>cfg 里是否真的写了 playWhen；用来区分“任何场景都播”是用户写的，还是根本没配条件。</summary>
    public bool HasPlayWhen => Model.PlayWhen.Count > 0;

    public string SceneGroupKey { get; private set; } = SoundtrackScenes.UnrestrictedKey;

    public string SceneGroupTitle { get; private set; } = "未限定场景";

    /// <summary>
    /// playWhen 的 bodyName（星球）。空串表示没限定星球，画布上归到「任何星球」。
    /// scene 已经是第一级分组，这里只负责第二级，不去改写条件链里的「星球」节点。
    /// </summary>
    public string BodyNameKey { get; private set; } = string.Empty;

    public string BodyNameTitle => BodyNameKey.Length == 0 ? "任何星球" : BodyNameKey;

    public string BodySubtitle => BodyNameKey.Length == 0 ? "bodyName 未写" : "bodyName = " + BodyNameKey;

    public string RawPlayWhenText => SoundtrackTriggers.DescribeRaw(Model);

    /// <summary>节点上直接给出“哪几首歌”，点开之前就能判断这个列表值不值得看。</summary>
    public string TrackPreviewText
    {
        get
        {
            if (Model.Tracks.Count == 0)
            {
                return "还没有曲目";
            }

            var preview = string.Join(" · ", Model.Tracks.Take(3).Select(track => track.Name));
            return Model.Tracks.Count > 3 ? preview + $" 等 {Model.Tracks.Count} 首" : preview;
        }
    }

    /// <summary>分组节点的展开状态；播放列表节点本身不折叠。</summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    public bool HasMissingTracks => Model.Tracks.Any(track => !track.Exists);

    public string MissingBadgeText => $"{Model.Tracks.Count(track => !track.Exists)} 首缺失";

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    /// <summary>曲目增删后由页面调用：只刷新这一行的计数与缺失徽标，不重建整行。</summary>
    public void NotifyTracksChanged()
    {
        OnPropertyChanged(nameof(TrackCount));
        OnPropertyChanged(nameof(TrackCountText));
        OnPropertyChanged(nameof(HasMissingTracks));
        OnPropertyChanged(nameof(MissingBadgeText));
        OnPropertyChanged(nameof(TrackPreviewText));
        RebuildFlowNodes();
        OnPropertyChanged(nameof(FlowNodes));
    }

    /// <summary>
    /// playWhen 改动后重算这一行的派生视图：条件 chips、场景分组、流程链、原文。
    /// 只刷新派生值，不动用户正在编辑的草稿，避免输入框被自己的写回清空。
    /// </summary>
    public void RefreshTriggerViews()
    {
        Conditions = SoundtrackTriggers.Describe(Model, includeScene: false);
        AllConditions = SoundtrackTriggers.Describe(Model);
        SceneGroupKey = SoundtrackScenes.GroupKey(Model);
        SceneGroupTitle = SoundtrackScenes.GroupTitle(SceneGroupKey);
        BodyNameKey = SoundtrackTriggers.BodyNameOf(Model);
        RebuildFlowNodes();

        OnPropertyChanged(nameof(Conditions));
        OnPropertyChanged(nameof(AllConditions));
        OnPropertyChanged(nameof(HasConditions));
        OnPropertyChanged(nameof(FlowNodes));
        OnPropertyChanged(nameof(SceneGroupKey));
        OnPropertyChanged(nameof(SceneGroupTitle));
        OnPropertyChanged(nameof(BodyNameKey));
        OnPropertyChanged(nameof(BodyNameTitle));
        OnPropertyChanged(nameof(BodySubtitle));
        OnPropertyChanged(nameof(SceneSummary));
        OnPropertyChanged(nameof(HasPlayWhen));
        OnPropertyChanged(nameof(RawPlayWhenText));
        OnPropertyChanged(nameof(ConditionFallbackText));
    }

    /// <summary>把一条条件写回模型：同键覆盖（保留原位置），没有就追加；值由页面校验并规范化后传入。</summary>
    public void ApplyCondition(string key, string normalizedValue)
    {
        for (var index = 0; index < Model.PlayWhen.Count; index++)
        {
            if (!Model.PlayWhen[index].Key.Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (string.Equals(Model.PlayWhen[index].Value, normalizedValue, StringComparison.Ordinal))
            {
                return;
            }

            Model.PlayWhen[index] = new KeyValuePair<string, string>(Model.PlayWhen[index].Key, normalizedValue);
            RefreshTriggerViews();
            OnTriggerViewsChanged();
            return;
        }

        Model.PlayWhen.Add(new KeyValuePair<string, string>(key, normalizedValue));
        RefreshTriggerViews();
        OnTriggerViewsChanged();
    }

    /// <summary>移除一条条件；返回是否真的删掉了。</summary>
    public bool RemoveCondition(string key)
    {
        var removed = Model.PlayWhen.RemoveAll(pair =>
            pair.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (removed == 0)
        {
            return false;
        }

        RefreshTriggerViews();
        OnTriggerViewsChanged();
        return true;
    }

    /// <summary>条件原值；编辑框用它做归一化比较。</summary>
    public string RawValueOf(string key)
        => Model.PlayWhen.FirstOrDefault(pair =>
            pair.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).Value ?? string.Empty;

    /// <summary>条件的中文预览（未知键返回原文）。</summary>
    public string DescribeCondition(string key)
        => SoundtrackTriggers.Describe(Model).FirstOrDefault(condition =>
            condition.Key.Equals(key, StringComparison.OrdinalIgnoreCase))?.Value ?? string.Empty;

    private void OnTriggerViewsChanged()
    {
        _changed(this);
        TriggersChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RebuildFlowNodes()
    {
        var stages = new List<SoundtrackFlowNode>
        {
            new("曲目", TrackHeadline, IsLast: false, TrackPreviewText),
        };

        foreach (var condition in Conditions.Take(3))
        {
            stages.Add(new SoundtrackFlowNode(condition.Label, condition.Value, IsLast: false, condition.Tooltip));
        }

        if (Conditions.Count == 0)
        {
            stages.Add(new SoundtrackFlowNode("条件", "任意", IsLast: false));
        }
        else if (Conditions.Count > 3)
        {
            stages.Add(new SoundtrackFlowNode("还有", $"{Conditions.Count - 3} 项", IsLast: false));
        }

        // 只有链尾不接箭头，否则最后一节后面会飘一个指向空气的箭头。
        stages[^1] = stages[^1] with { IsLast = true };
        _flowNodes = stages;
    }

    /// <summary>链头写具体曲名而不是「N 首」——用户问的第一个问题就是“哪首歌”。</summary>
    private string TrackHeadline => Model.Tracks.Count switch
    {
        0 => "空",
        1 => Model.Tracks[0].Name,
        _ => Model.Tracks[0].Name + $" +{Model.Tracks.Count - 1}",
    };
}

/// <summary>
/// 选择器里的一个可选值。勾选/取消时由所属条件行重新拼出 Drat，而不是让每个 CheckBox 直接写 playWhen。
/// </summary>
public sealed class SoundtrackTriggerOptionViewModel : ObservableObject
{
    private readonly Action? _changed;
    private bool _isSelected;
    private bool _suppress;

    public SoundtrackTriggerOptionViewModel(string value, string label, Action? changed = null)
    {
        Value = value;
        Label = label;
        _changed = changed;
    }

    public string Value { get; }

    public string Label { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetProperty(ref _isSelected, value) && !_suppress)
            {
                _changed?.Invoke();
            }
        }
    }

    /// <summary>由 Draft 反推勾选状态时使用，不触发回写，避免 setter 互相递归。</summary>
    internal void SetSelectedSilently(bool selected)
    {
        _suppress = true;
        IsSelected = selected;
        _suppress = false;
    }
}

/// <summary>
/// 右栏可编辑的一条 playWhen 条件。
/// </summary>
/// <remarks>
/// 草稿是选择器/输入框的当前值，点「应用」才落到模型：每敲一个字就重建场景分组会让左栏在输入时跳动，
/// 而且半截值（如把 VAB 删成 V）会被当成非法值弹错。枚举键在界面上是勾选/下拉，不再要求用户背枚举名。
/// </remarks>
public sealed class SoundtrackConditionRowViewModel : ObservableObject
{
    private readonly SoundtrackPlaylistRowViewModel? _owner;
    private string _draft;
    private bool _syncingOptions;

    public SoundtrackConditionRowViewModel(SoundtrackPlaylistRowViewModel? owner, string key, string value)
    {
        _owner = owner;
        Key = key;
        Value = value ?? string.Empty;
        _draft = Value;
        Field = SoundtrackTriggerCatalog.Find(key);
        Label = Field?.Label ?? key;
        Hint = Field?.Hint ?? "页面不认识这个键；改值会原样写回 playWhen。";

        if (Field is not null && Field.HasOptions)
        {
            foreach (var option in Field.Options)
            {
                OptionItems.Add(new SoundtrackTriggerOptionViewModel(option.Key, option.Label, OnOptionChanged));
            }
        }

        SyncOptionsFromDraft();
    }

    public string Key { get; }

    public string Label { get; }

    /// <summary>键的编辑描述；未知键为 null（只能按文本改）。</summary>
    public SoundtrackTriggerField? Field { get; }

    public bool IsKnown => Field is not null;

    public bool IsFlags => Field?.Kind == SoundtrackTriggerKind.Flags;

    public bool IsBoolean => Field?.Kind == SoundtrackTriggerKind.Boolean;

    public bool IsNumber => Field?.Kind == SoundtrackTriggerKind.Number;

    public bool IsBodyName => Key.Equals(SoundtrackTriggers.BodyNameKey, StringComparison.OrdinalIgnoreCase);

    /// <summary>除天体名以外的自由文本键；当前目录里没有，但保留给未知键/后续扩展。</summary>
    public bool IsOtherText => Field?.Kind == SoundtrackTriggerKind.Text && !IsBodyName;

    public string Hint { get; }

    /// <summary>输入框的 tooltip：枚举键在这里列出全部合法取值，正文只留一句短提示。</summary>
    public string ValueHint => Field?.ValueHint ?? Hint;

    /// <summary>这一行创建时模型里的原值；用于判断草稿是否有未应用的改动。</summary>
    public string Value { get; }

    /// <summary>枚举键的可选值；布尔键也用这一组（是/否）。</summary>
    public ObservableCollection<SoundtrackTriggerOptionViewModel> OptionItems { get; } = new();

    /// <summary>天体名建议：库存天体 + 当前文档里已经出现过的 bodyName。</summary>
    public ObservableCollection<string> BodyOptions { get; } = new();

    public string FlagsSummary
    {
        get
        {
            var selected = OptionItems.Where(option => option.IsSelected).Select(option => option.Label).ToList();
            selected.AddRange(ExtraFlags.Select(flag => flag + "（未识别）"));
            return selected.Count == 0 ? "未选择" : string.Join("、", selected);
        }
    }

    /// <summary>
    /// 草稿里目录不认识的枚举值。切勾选时不能把它们丢掉：cfg 里可能有模组场景，
    /// 页面看不懂也要原样保留，保存校验会拦住，而不是静默删值。
    /// </summary>
    private IEnumerable<string> ExtraFlags => IsFlags
        ? SoundtrackTriggers.SplitFlags(Draft)
            .Where(flag => !OptionItems.Any(option => option.Value.Equals(flag, StringComparison.OrdinalIgnoreCase)))
        : [];

    public SoundtrackTriggerOptionViewModel? SelectedOption
    {
        get => OptionItems.FirstOrDefault(option => option.IsSelected);
        set
        {
            if (value is null || _syncingOptions)
            {
                return;
            }

            foreach (var option in OptionItems)
            {
                option.SetSelectedSilently(ReferenceEquals(option, value));
            }

            Draft = value.Value;
        }
    }

    public decimal? NumberDraft
    {
        get => decimal.TryParse(Draft, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            ? number
            : null;
        set
        {
            if (value is { } number)
            {
                Draft = number.ToString("0.###", CultureInfo.InvariantCulture);
            }
        }
    }

    public string Draft
    {
        get => _draft;
        set
        {
            if (SetProperty(ref _draft, value ?? string.Empty))
            {
                SyncOptionsFromDraft();
                OnPropertyChanged(nameof(HasPendingEdit));
                OnPropertyChanged(nameof(PendingText));
                OnPropertyChanged(nameof(NumberDraft));
            }
        }
    }

    public bool HasPendingEdit => !string.Equals(Draft.Trim(), Value.Trim(), StringComparison.OrdinalIgnoreCase);

    public string PendingText => HasPendingEdit ? "未应用" : string.Empty;

    /// <summary>中文预览，帮助用户确认自己填的枚举到底对应什么。</summary>
    public string Preview => _owner?.DescribeCondition(Key) ?? string.Empty;

    public void SetBodyOptions(IEnumerable<string> bodies)
    {
        BodyOptions.Clear();
        foreach (var body in bodies
                     .Where(body => !string.IsNullOrWhiteSpace(body))
                     .Select(body => body.Trim())
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(body => body, StringComparer.OrdinalIgnoreCase))
        {
            BodyOptions.Add(body);
        }
    }

    public void ResetDraft(string value)
    {
        _draft = value ?? string.Empty;
        SyncOptionsFromDraft();
        OnPropertyChanged(nameof(Draft));
        OnPropertyChanged(nameof(HasPendingEdit));
        OnPropertyChanged(nameof(PendingText));
        OnPropertyChanged(nameof(NumberDraft));
    }

    private void OnOptionChanged()
    {
        if (IsFlags)
        {
            var values = OptionItems.Where(option => option.IsSelected).Select(option => option.Value);
            Draft = string.Join(" | ", values.Concat(ExtraFlags));
        }
        else if (IsBoolean && SelectedOption is { } selected)
        {
            Draft = selected.Value;
        }
    }

    private void SyncOptionsFromDraft()
    {
        if (OptionItems.Count == 0)
        {
            return;
        }

        _syncingOptions = true;
        try
        {
            if (IsFlags)
            {
                var selected = new HashSet<string>(
                    SoundtrackTriggers.SplitFlags(Draft),
                    StringComparer.OrdinalIgnoreCase);
                foreach (var option in OptionItems)
                {
                    option.SetSelectedSilently(selected.Contains(option.Value));
                }
            }
            else
            {
                foreach (var option in OptionItems)
                {
                    option.SetSelectedSilently(
                        option.Value.Equals(Draft.Trim(), StringComparison.OrdinalIgnoreCase));
                }
            }
        }
        finally
        {
            _syncingOptions = false;
        }

        OnPropertyChanged(nameof(FlagsSummary));
        OnPropertyChanged(nameof(SelectedOption));
    }
}

/// <summary>
/// 流程画布的第三级节点：一个场景下按 playWhen 的 bodyName 分出来的星球。
/// </summary>
/// <remarks>
/// 只有组内至少一条播放列表真的写了 bodyName 时才细分；否则「任何星球」这一层
/// 只是给每个场景平白多加一次点击，不如维持场景 → 列表的两级结构。
/// </remarks>
public sealed class SoundtrackBodyGroupViewModel : ObservableObject
{
    private bool _isExpanded;

    public SoundtrackBodyGroupViewModel(string key)
    {
        Key = key ?? string.Empty;
        Title = Key.Length == 0 ? "任何星球" : Key;
        Subtitle = Key.Length == 0 ? "bodyName 未写" : "bodyName = " + Key;
    }

    public string Key { get; }

    public string Title { get; }

    public string Subtitle { get; }

    public ObservableCollection<SoundtrackPlaylistRowViewModel> Playlists { get; } = new();

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    public string CountText => $"{Playlists.Count} 个列表 · {Playlists.Sum(row => row.TrackCount)} 首";

    public bool HasMissing => Playlists.Any(row => row.HasMissingTracks);

    public string MissingText => HasMissing
        ? Playlists.Sum(row => row.Model.Tracks.Count(track => !track.Exists)) + " 首缺失"
        : string.Empty;

    public void NotifyCountsChanged()
    {
        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(HasMissing));
        OnPropertyChanged(nameof(MissingText));
    }
}

/// <summary>
/// 节点树的一级节点：一个场景（或一组场景）下挂着的播放列表。
/// </summary>
/// <remarks>
/// 分组键按 scene 的集合而不是单个 scene：`VAB | SPH` 是一条播放列表，硬拆成两棵子树会让同一个列表出现两次，
/// 用户会以为配了两遍。组内保持 cfg 顺序，因为 Soundtrack 同时命中多份时也按文件顺序排队。
/// </remarks>
public sealed class SoundtrackSceneGroupViewModel : ObservableObject
{
    private bool _isExpanded = true;

    public SoundtrackSceneGroupViewModel(string key)
    {
        Key = key;
        Title = SoundtrackScenes.GroupTitle(key);
        Subtitle = SoundtrackScenes.GroupSubtitle(key);
    }

    public string Key { get; }

    public string Title { get; }

    /// <summary>cfg 里的原始枚举名，方便和文件逐字对照。</summary>
    public string Subtitle { get; }

    public ObservableCollection<SoundtrackPlaylistRowViewModel> Playlists { get; } = new();

    /// <summary>按 bodyName 分出来的星球节点；没有一条写了 bodyName 时为空，画布维持两级。</summary>
    public ObservableCollection<SoundtrackBodyGroupViewModel> BodyGroups { get; } = new();

    public bool HasBodyGroups => BodyGroups.Count > 0;

    /// <summary>画布从上到下的实际顺序：有星球分组时先走星球，否则直接走列表。</summary>
    public IEnumerable<SoundtrackPlaylistRowViewModel> RowsInDisplayOrder
        => HasBodyGroups ? BodyGroups.SelectMany(group => group.Playlists) : Playlists;

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    public string CountText => $"{Playlists.Count} 个列表 · {Playlists.Sum(row => row.TrackCount)} 首";

    public bool HasMissing => Playlists.Any(row => row.HasMissingTracks);

    public string MissingText => HasMissing
        ? Playlists.Sum(row => row.Model.Tracks.Count(track => !track.Exists)) + " 首缺失"
        : string.Empty;

    /// <summary>
    /// 按 bodyName 重建星球节点。组内一条都没写 bodyName 时保持平铺，避免每个场景都多一层
    /// 「任何星球」。星球多的时候默认收起，先给用户一张星球目录，而不是把 100 多个列表一次铺开。
    /// </summary>
    /// <param name="collapsedKeys">重建前用户手动收起的星球；null 表示这是首次载入。</param>
    public void RebuildBodyGroups(ISet<string>? collapsedKeys = null)
    {
        BodyGroups.Clear();
        if (!Playlists.Any(row => row.BodyNameKey.Length > 0))
        {
            OnPropertyChanged(nameof(HasBodyGroups));
            OnPropertyChanged(nameof(RowsInDisplayOrder));
            return;
        }

        var buckets = Playlists
            .GroupBy(row => row.BodyNameKey, StringComparer.OrdinalIgnoreCase)
            .OrderBy(bucket => bucket.Key.Length == 0 ? 0 : 1)
            .ThenBy(bucket => bucket.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var defaultExpanded = buckets.Count <= 4;

        foreach (var bucket in buckets)
        {
            var group = new SoundtrackBodyGroupViewModel(bucket.Key)
            {
                IsExpanded = collapsedKeys is null
                    ? defaultExpanded
                    : !collapsedKeys.Contains(bucket.Key),
            };
            foreach (var row in bucket)
            {
                group.Playlists.Add(row);
            }

            BodyGroups.Add(group);
        }

        OnPropertyChanged(nameof(HasBodyGroups));
        OnPropertyChanged(nameof(RowsInDisplayOrder));
    }

    /// <summary>曲目数/缺失数变化后由页面调用，避免整棵树重建把展开状态和滚动位置弄丢。</summary>
    public void NotifyCountsChanged()
    {
        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(HasMissing));
        OnPropertyChanged(nameof(MissingText));
        foreach (var group in BodyGroups)
        {
            group.NotifyCountsChanged();
        }
    }
}

/// <summary>
/// 右栏曲目列表的一行。它是某次载入/选择时的快照，行内不可编辑（增删改都走页面命令），
/// 所以不需要属性通知；重新载入或换播放列表时整行重建。
/// </summary>
public sealed class SoundtrackTrackRowViewModel
{
    public SoundtrackTrackRowViewModel(SoundtrackTrackRef track, Action<SoundtrackTrackRowViewModel> open)
    {
        Model = track ?? throw new ArgumentNullException(nameof(track));
        OpenInExplorerCommand = new RelayCommand(
            () => open(this),
            () => Exists);
    }

    public SoundtrackTrackRef Model { get; }

    public string Name => Model.Name;

    public string FilePathText => Model.FilePath ?? string.Empty;

    public bool Exists => Model.Exists;

    public bool HasMissing => !Exists;

    /// <summary>缺失是事实，不是过滤条件：这一行照样留着，只是播放列表点它时播不出来。</summary>
    public string MissingText => Exists ? string.Empty : "缺失";

    public string SizeText => Model.SizeBytes is { } bytes ? SoundtrackSizeText.Format(bytes) : string.Empty;

    public ICommand OpenInExplorerCommand { get; }
}

/// <summary>
/// 乐曲库的一行。IsAdded 由页面在选中播放列表变化/曲目增删时刷新，
/// 让用户先看到“已经在列表里”，而不是点了添加才收到一句报错。
/// </summary>
public sealed class SoundtrackLibraryRowViewModel : ObservableObject
{
    private bool _isAdded;

    public SoundtrackLibraryRowViewModel(SoundtrackMusicFile file)
    {
        Model = file ?? throw new ArgumentNullException(nameof(file));
    }

    public SoundtrackMusicFile Model { get; }

    public string Name => Model.Name;

    public string FullPath => Model.FullPath;

    public string SizeText => SoundtrackSizeText.Format(Model.SizeBytes);

    public string Extension => Model.Extension;

    public bool IsAdded
    {
        get => _isAdded;
        set
        {
            if (SetProperty(ref _isAdded, value))
            {
                OnPropertyChanged(nameof(AddedText));
            }
        }
    }

    public string AddedText => IsAdded ? "已加入" : string.Empty;
}

/// <summary>
/// 体积文案只在曲目行和乐曲库行出现，格式集中在一处，避免两处算出不同的“MB”。
/// </summary>
internal static class SoundtrackSizeText
{
    public static string Format(long bytes)
    {
        if (bytes >= 1024L * 1024L)
        {
            return (bytes / (1024d * 1024d)).ToString("0.#", CultureInfo.InvariantCulture) + " MB";
        }

        if (bytes >= 1024L)
        {
            return (bytes / 1024d).ToString("0.#", CultureInfo.InvariantCulture) + " KB";
        }

        return bytes.ToString(CultureInfo.InvariantCulture) + " B";
    }
}
