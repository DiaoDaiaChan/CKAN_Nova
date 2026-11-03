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
    private bool _isSelected;

    public SoundtrackPlaylistRowViewModel(SoundtrackPlaylist playlist, Action<SoundtrackPlaylistRowViewModel> changed)
    {
        Model = playlist ?? throw new ArgumentNullException(nameof(playlist));
        _changed = changed ?? throw new ArgumentNullException(nameof(changed));
    }

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
