namespace CkanUI.Services.Abstractions.Audio;

/// <summary>
/// 界面交互音效播放服务。
/// </summary>
public interface ISoundEffectService
{
    /// <summary>是否启用音效反馈。</summary>
    bool IsEnabled { get; set; }

    /// <summary>是否静音。</summary>
    bool IsMuted { get; set; }

    /// <summary>音效音量（0.0 至 1.0）。</summary>
    double Volume { get; set; }

    /// <summary>播放预设的交互音效。</summary>
    void Play(SoundEffectType type);

    /// <summary>播放自定义 PCM WAV 音频字节。</summary>
    void PlayCustom(byte[] wavBytes);
}
