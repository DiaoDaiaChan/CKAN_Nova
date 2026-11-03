using CkanUI.Services.Abstractions;

namespace CkanUI.Plugin.Soundtrack.Playlists;

/// <summary>播放列表里的一条曲目引用（只记名字，实际文件由乐曲库解析）。</summary>
public sealed class SoundtrackTrackRef
{
    /// <summary>曲目名，对应 playlists.cfg 里的 track = 值。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>解析到的音频文件全路径；没找到为 null（文件仍在播放列表里，只是播不出来）。</summary>
    public string? FilePath { get; set; }

    /// <summary>文件字节数；没找到为 null。</summary>
    public long? SizeBytes { get; set; }

    /// <summary>磁盘上是否存在对应音频文件。</summary>
    public bool Exists => !string.IsNullOrEmpty(FilePath);
}

/// <summary>一个 playlist 节点。</summary>
public sealed class SoundtrackPlaylist
{
    /// <summary>name 键（必填）。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>enabled 键；原文件没写这个键时为 null（保留“没写”和“写了 False”的区别）。</summary>
    public bool? Enabled { get; set; }

    /// <summary>loop。</summary>
    public bool Loop { get; set; } = true;

    /// <summary>shuffle。</summary>
    public bool Shuffle { get; set; }

    /// <summary>pauseOnGamePause。</summary>
    public bool PauseOnGamePause { get; set; } = true;

    /// <summary>disableAfterPlay。</summary>
    public bool DisableAfterPlay { get; set; }

    /// <summary>playNext。</summary>
    public string PlayNext { get; set; } = string.Empty;

    /// <summary>playBefore。</summary>
    public string PlayBefore { get; set; } = string.Empty;

    /// <summary>playAfter。</summary>
    public string PlayAfter { get; set; } = string.Empty;

    /// <summary>channel 键；没写为 null。</summary>
    public int? Channel { get; set; }

    /// <summary>preloadTime 键；没写为 null。</summary>
    public double? PreloadTime { get; set; }

    /// <summary>tracks 节点的曲目顺序。</summary>
    public List<SoundtrackTrackRef> Tracks { get; } = new();

    /// <summary>playWhen 节点的键值（保持原顺序，值原样保留，如 scene = VAB | SPH）。</summary>
    public List<KeyValuePair<string, string>> PlayWhen { get; } = new();

    /// <summary>本插件不认识的标量键，原样保留，避免保存时丢数据。</summary>
    public List<KeyValuePair<string, string>> ExtraValues { get; } = new();

    /// <summary>本插件不认识的子节点，按原始文本保留（含大括号），保存时原样写回。</summary>
    public List<string> UnknownNodes { get; } = new();

    /// <summary>界面上显示的一行场景摘要。</summary>
    public string SceneSummary => PlayWhen.Count == 0
        ? "任何场景"
        : string.Join("、", PlayWhen.Where(pair => pair.Key is "scene" or "inAtmosphere" or "timeOfDay").Select(pair => pair.Value));
}

/// <summary>乐曲库里的一个文件。</summary>
public sealed record SoundtrackMusicFile(string Name, string FullPath, long SizeBytes, string Extension);

/// <summary>一次读盘的完整结果。</summary>
public sealed class SoundtrackDocument
{
    /// <summary>实例根目录。</summary>
    public string GameDir { get; set; } = string.Empty;

    /// <summary>Playlists/playlists.cfg 的全路径（文件可能不存在）。</summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>播放列表文件是否存在。</summary>
    public bool FileExists { get; set; }

    /// <summary>Playlists 目录是否存在。</summary>
    public bool DirectoryExists { get; set; }

    /// <summary>找到的 Soundtrack 模组目录（用于界面说明“装没装”）；没找到为 null。</summary>
    public string? ModDirectory { get; set; }

    /// <summary>文件换行风格（保存时沿用，默认 CRLF）。</summary>
    public string NewLine { get; set; } = "\r\n";

    /// <summary>读到的播放列表。</summary>
    public List<SoundtrackPlaylist> Playlists { get; } = new();

    /// <summary>扫描到的乐曲文件。</summary>
    public List<SoundtrackMusicFile> MusicLibrary { get; } = new();

    /// <summary>读盘过程中的可读诊断（跳过哪些目录、哪个文件读不动）。</summary>
    public List<string> Diagnostics { get; } = new();

    /// <summary>解析不到文件的曲目条数。</summary>
    public int MissingTrackCount => Playlists.Sum(playlist => playlist.Tracks.Count(track => !track.Exists));
}

/// <summary>保存结果。</summary>
public sealed record SoundtrackSaveOutcome(string FilePath, string? BackupPath, int PlaylistCount, int TrackCount);

/// <summary>播放列表读写服务（实现见 <see cref="SoundtrackService"/>）。</summary>
public interface ISoundtrackService
{
    /// <summary>读一个实例的播放列表与乐曲库（不抛异常，问题进 Diagnostics）。</summary>
    SoundtrackDocument Load(string gameDir);

    /// <summary>把内存里的文档写回 playlists.cfg（先备份再原子替换）。</summary>
    OperationResult<SoundtrackSaveOutcome> Save(SoundtrackDocument document);

    /// <summary>重新解析曲目到文件（换实例、刷新乐曲库后调用）。</summary>
    void ResolveTracks(SoundtrackDocument document);
}
