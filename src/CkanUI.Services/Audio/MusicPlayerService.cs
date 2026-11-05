using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Audio;
using log4net;

namespace CkanUI.Services.Audio;

/// <summary>
/// GUI 背景音乐播放服务实现。
/// 支持单曲播放、Soundtrack 播放列表多曲目队列顺序/循环播放、进度获取与快进。
/// </summary>
public sealed class MusicPlayerService : IMusicPlayerService
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(MusicPlayerService));

    private readonly IUiSettingsStore _settingsStore;
    private readonly WindowsMediaPlayerWrapper? _backend;
    private readonly Timer _pollTimer;

    private readonly List<string> _playlistFilePaths = new();
    private readonly List<string> _playlistTrackNames = new();
    private int _currentTrackIndex = -1;

    private bool _isPlaying;
    private bool _isPaused;
    private bool _isMuted;
    private double _volume = 0.7;

    private string? _currentFilePath;
    private string? _currentTrackName;
    private string? _currentPlaylistName;
    private TimeSpan _position = TimeSpan.Zero;
    private TimeSpan _duration = TimeSpan.Zero;
    private int _lastObservedPlayState;

    public event EventHandler? PlaybackStateChanged;
    public event EventHandler? TrackChanged;
    public event EventHandler? PositionChanged;

    public MusicPlayerService(IUiSettingsStore settingsStore)
    {
        _settingsStore = settingsStore;
        var settings = _settingsStore.Current;
        _volume = Math.Clamp(settings.MusicVolume, 0.0, 1.0);
        _isMuted = settings.MasterMuted;

        if (OperatingSystem.IsWindows())
        {
            _backend = WindowsMediaPlayerWrapper.TryCreate();
            if (_backend != null)
            {
                _backend.Volume = (int)(_volume * 100);
                _backend.Mute = _isMuted;
            }
        }

        _settingsStore.Changed += OnSettingsChanged;

        // 500ms 定时轮询播放进度与曲目结束状态
        _pollTimer = new Timer(OnPollTimerTick, null, 500, 500);
    }

    private void OnSettingsChanged(UiSettings settings)
    {
        _volume = Math.Clamp(settings.MusicVolume, 0.0, 1.0);
        _isMuted = settings.MasterMuted;
        if (OperatingSystem.IsWindows() && _backend != null)
        {
            _backend.Volume = (int)(_volume * 100);
            _backend.Mute = _isMuted;
        }
    }

    public bool IsPlaying => _isPlaying;
    public bool IsPaused => _isPaused;

    public bool IsMuted
    {
        get => _isMuted;
        set
        {
            _isMuted = value;
            if (OperatingSystem.IsWindows() && _backend != null)
            {
                _backend.Mute = value;
            }
            PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public double Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0.0, 1.0);
            if (OperatingSystem.IsWindows() && _backend != null)
            {
                _backend.Volume = (int)(_volume * 100);
            }
            PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public string? CurrentFilePath => _currentFilePath;
    public string? CurrentTrackName => _currentTrackName;
    public string? CurrentPlaylistName => _currentPlaylistName;
    public TimeSpan Position => _position;
    public TimeSpan Duration => _duration;
    public IReadOnlyList<string> CurrentPlaylistTracks => _playlistFilePaths;
    public int CurrentTrackIndex => _currentTrackIndex;

    public void Play(string filePath, string? trackName = null, string? playlistName = null)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            Log.Warn($"音频文件不存在或路径无效: {filePath}");
            return;
        }

        _currentFilePath = filePath;
        _currentTrackName = trackName ?? Path.GetFileNameWithoutExtension(filePath);
        _currentPlaylistName = playlistName;

        // 若不是来自当前队列，则创建单曲队列
        if (_currentTrackIndex < 0 || _currentTrackIndex >= _playlistFilePaths.Count || _playlistFilePaths[_currentTrackIndex] != filePath)
        {
            _playlistFilePaths.Clear();
            _playlistTrackNames.Clear();
            _playlistFilePaths.Add(filePath);
            _playlistTrackNames.Add(_currentTrackName);
            _currentTrackIndex = 0;
        }

        if (OperatingSystem.IsWindows() && _backend != null)
        {
            _backend.Open(filePath);
            _backend.Play();
        }

        _isPlaying = true;
        _isPaused = false;
        _position = TimeSpan.Zero;
        _duration = TimeSpan.Zero;

        TrackChanged?.Invoke(this, EventArgs.Empty);
        PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void PlayPlaylist(IEnumerable<string> filePaths, IEnumerable<string>? trackNames = null, int startIndex = 0, string? playlistName = null)
    {
        var validFiles = new List<string>();
        var validNames = new List<string>();

        var fileList = filePaths.ToList();
        var nameList = trackNames?.ToList();

        for (int i = 0; i < fileList.Count; i++)
        {
            string f = fileList[i];
            if (File.Exists(f))
            {
                validFiles.Add(f);
                string n = (nameList != null && i < nameList.Count && !string.IsNullOrWhiteSpace(nameList[i]))
                    ? nameList[i]
                    : Path.GetFileNameWithoutExtension(f);
                validNames.Add(n);
            }
        }

        if (validFiles.Count == 0)
        {
            Log.Warn("播放列表中无有效且存在的音频文件");
            return;
        }

        _playlistFilePaths.Clear();
        _playlistTrackNames.Clear();
        _playlistFilePaths.AddRange(validFiles);
        _playlistTrackNames.AddRange(validNames);

        int targetIndex = Math.Clamp(startIndex, 0, _playlistFilePaths.Count - 1);
        _currentTrackIndex = targetIndex;
        _currentPlaylistName = playlistName;

        Play(_playlistFilePaths[targetIndex], _playlistTrackNames[targetIndex], playlistName);
    }

    public void Pause()
    {
        if (!_isPlaying || _isPaused) return;

        if (OperatingSystem.IsWindows() && _backend != null)
        {
            _backend.Pause();
        }

        _isPlaying = false;
        _isPaused = true;
        PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Resume()
    {
        if (!_isPaused) return;

        if (OperatingSystem.IsWindows() && _backend != null)
        {
            _backend.Play();
        }

        _isPlaying = true;
        _isPaused = false;
        PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Stop()
    {
        if (OperatingSystem.IsWindows() && _backend != null)
        {
            _backend.Stop();
        }

        _isPlaying = false;
        _isPaused = false;
        _position = TimeSpan.Zero;
        PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Next()
    {
        if (_playlistFilePaths.Count == 0) return;

        int nextIndex = _currentTrackIndex + 1;
        if (nextIndex >= _playlistFilePaths.Count)
        {
            nextIndex = 0; // 列表循环
        }

        _currentTrackIndex = nextIndex;
        Play(_playlistFilePaths[nextIndex], _playlistTrackNames[nextIndex], _currentPlaylistName);
    }

    public void Previous()
    {
        if (_playlistFilePaths.Count == 0) return;

        int prevIndex = _currentTrackIndex - 1;
        if (prevIndex < 0)
        {
            prevIndex = _playlistFilePaths.Count - 1; // 环回最后一首
        }

        _currentTrackIndex = prevIndex;
        Play(_playlistFilePaths[prevIndex], _playlistTrackNames[prevIndex], _currentPlaylistName);
    }

    public void Seek(TimeSpan position)
    {
        if (OperatingSystem.IsWindows() && _backend != null)
        {
            _backend.PositionSeconds = Math.Clamp(position.TotalSeconds, 0, _duration.TotalSeconds);
        }
        _position = position;
        PositionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnPollTimerTick(object? state)
    {
        if (!OperatingSystem.IsWindows() || _backend == null) return;

        try
        {
            int playState = _backend.PlayState;
            double posSec = _backend.PositionSeconds;
            double durSec = _backend.DurationSeconds;

            if (durSec > 0)
            {
                _duration = TimeSpan.FromSeconds(durSec);
            }
            if (posSec >= 0)
            {
                _position = TimeSpan.FromSeconds(posSec);
                PositionChanged?.Invoke(this, EventArgs.Empty);
            }

            // 监听曲目播放完毕自动下一首 (playState: 1 = Stopped, 8 = MediaEnded)
            if (_isPlaying && (_lastObservedPlayState == 3 || _lastObservedPlayState == 9))
            {
                if (playState is 1 or 8 && _duration.TotalSeconds > 1 && _position.TotalSeconds >= (_duration.TotalSeconds - 1.5))
                {
                    Log.Info($"曲目播放完毕 [{_currentTrackName}]，自动播放下一首");
                    Next();
                }
            }

            _lastObservedPlayState = playState;
        }
        catch
        {
            // 轮询异常不抛出
        }
    }

    public void Dispose()
    {
        _pollTimer.Dispose();
        _settingsStore.Changed -= OnSettingsChanged;
        if (OperatingSystem.IsWindows())
        {
            _backend?.Dispose();
        }
    }
}
