using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Audio;
using log4net;

namespace CkanUI.Services.Audio;

/// <summary>
/// 统合音频中枢服务实现。
/// 管理全局快速静音状态，协调音效子系统与音乐播放器子系统。
/// </summary>
public sealed class AudioService : IAudioService
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(AudioService));

    private readonly ISoundEffectService _soundEffects;
    private readonly IMusicPlayerService _musicPlayer;
    private readonly IUiSettingsStore _settingsStore;
    private bool _masterMuted;

    public event EventHandler<bool>? MasterMuteChanged;

    public AudioService(
        ISoundEffectService soundEffects,
        IMusicPlayerService musicPlayer,
        IUiSettingsStore settingsStore)
    {
        _soundEffects = soundEffects;
        _musicPlayer = musicPlayer;
        _settingsStore = settingsStore;

        _masterMuted = _settingsStore.Current.MasterMuted;
        ApplyMasterMuteToSubsystems(_masterMuted);

        _settingsStore.Changed += OnSettingsChanged;
    }

    private void OnSettingsChanged(UiSettings settings)
    {
        if (_masterMuted != settings.MasterMuted)
        {
            _masterMuted = settings.MasterMuted;
            ApplyMasterMuteToSubsystems(_masterMuted);
            MasterMuteChanged?.Invoke(this, _masterMuted);
        }
    }

    public ISoundEffectService SoundEffects => _soundEffects;
    public IMusicPlayerService MusicPlayer => _musicPlayer;

    public bool MasterMuted
    {
        get => _masterMuted;
        set
        {
            if (_masterMuted == value) return;
            _masterMuted = value;
            ApplyMasterMuteToSubsystems(value);
            PersistMasterMute(value);
            MasterMuteChanged?.Invoke(this, value);
        }
    }

    public void ToggleMasterMute()
    {
        MasterMuted = !MasterMuted;
    }

    private void ApplyMasterMuteToSubsystems(bool muted)
    {
        _soundEffects.IsMuted = muted;
        _musicPlayer.IsMuted = muted;
    }

    private void PersistMasterMute(bool muted)
    {
        try
        {
            var updated = _settingsStore.Current with { MasterMuted = muted };
            _settingsStore.Save(updated);
        }
        catch (Exception ex)
        {
            Log.Warn($"持久化快速静音设置失败: {ex.Message}");
        }
    }

    public void Dispose()
    {
        _settingsStore.Changed -= OnSettingsChanged;
        _musicPlayer.Dispose();
    }
}
