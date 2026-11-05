namespace CkanUI.Services.Abstractions.Audio;

/// <summary>
/// 统合音频中枢服务。
/// 管理全局快速静音状态，协调音效子系统与音乐播放器子系统。
/// </summary>
public interface IAudioService : IDisposable
{
    /// <summary>交互音效子服务。</summary>
    ISoundEffectService SoundEffects { get; }

    /// <summary>背景音乐播放器子服务。</summary>
    IMusicPlayerService MusicPlayer { get; }

    /// <summary>全局快速静音状态（影响音效与音乐）。</summary>
    bool MasterMuted { get; set; }

    /// <summary>切换全局快速静音（供右上角小喇叭按钮调用）。</summary>
    void ToggleMasterMute();

    /// <summary>全局静音状态改变事件。</summary>
    event EventHandler<bool>? MasterMuteChanged;
}
