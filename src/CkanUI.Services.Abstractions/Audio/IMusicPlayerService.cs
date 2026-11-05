namespace CkanUI.Services.Abstractions.Audio;

/// <summary>
/// GUI 背景音乐播放服务。
/// 支持单曲播放、播放列表顺序/上一首/下一首切换、音量、静音与进度回传。
/// </summary>
public interface IMusicPlayerService : IDisposable
{
    /// <summary>当前是否正在播放。</summary>
    bool IsPlaying { get; }

    /// <summary>当前是否已暂停。</summary>
    bool IsPaused { get; }

    /// <summary>是否静音。</summary>
    bool IsMuted { get; set; }

    /// <summary>播放音量（0.0 至 1.0）。</summary>
    double Volume { get; set; }

    /// <summary>当前曲目文件路径。</summary>
    string? CurrentFilePath { get; }

    /// <summary>当前曲目名称。</summary>
    string? CurrentTrackName { get; }

    /// <summary>当前所在播放列表名称。</summary>
    string? CurrentPlaylistName { get; }

    /// <summary>当前播放进度。</summary>
    TimeSpan Position { get; }

    /// <summary>当前曲目总时长。</summary>
    TimeSpan Duration { get; }

    /// <summary>当前队列中的曲目文件列表。</summary>
    IReadOnlyList<string> CurrentPlaylistTracks { get; }

    /// <summary>当前曲目在队列中的索引（无曲目时为 -1）。</summary>
    int CurrentTrackIndex { get; }

    /// <summary>播放单首曲目（文件路径）。</summary>
    void Play(string filePath, string? trackName = null, string? playlistName = null);

    /// <summary>设置并播放一个队列（可来自 Soundtrack 播放列表）。</summary>
    void PlayPlaylist(IEnumerable<string> filePaths, IEnumerable<string>? trackNames = null, int startIndex = 0, string? playlistName = null);

    /// <summary>暂停播放。</summary>
    void Pause();

    /// <summary>继续播放。</summary>
    void Resume();

    /// <summary>停止播放。</summary>
    void Stop();

    /// <summary>下一首。</summary>
    void Next();

    /// <summary>上一首。</summary>
    void Previous();

    /// <summary>跳转到指定进度位置。</summary>
    void Seek(TimeSpan position);

    /// <summary>播放状态变更事件（播放、暂停、停止）。</summary>
    event EventHandler? PlaybackStateChanged;

    /// <summary>当前曲目切换事件。</summary>
    event EventHandler? TrackChanged;

    /// <summary>播放进度推进事件（轮询更新）。</summary>
    event EventHandler? PositionChanged;
}
