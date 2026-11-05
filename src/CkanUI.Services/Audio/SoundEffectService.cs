using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Audio;
using log4net;

namespace CkanUI.Services.Audio;

/// <summary>
/// 界面交互音效播放服务实现。
/// 在 Windows 平台上使用 winmm!PlaySound 异步零开销播放，
/// 结合 SoundEffectSynthesizer 提供的程序化内存音频，无磁盘 I/O 阻塞。
/// </summary>
public sealed class SoundEffectService : ISoundEffectService
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(SoundEffectService));

    private const uint SND_SYNC = 0x0000;
    private const uint SND_NODEFAULT = 0x0002;
    private const uint SND_MEMORY = 0x0004;

    [SupportedOSPlatform("windows")]
    [DllImport("winmm.dll", EntryPoint = "PlaySoundW", SetLastError = true)]
    private static extern bool PlaySound(nint ptrToSound, nint hmod, uint fdwSound);

    private readonly IUiSettingsStore _settingsStore;
    private bool _isEnabled = true;
    private bool _isMuted;
    private double _volume = 0.8;

    public SoundEffectService(IUiSettingsStore settingsStore)
    {
        _settingsStore = settingsStore;
        var settings = _settingsStore.Current;
        _isEnabled = settings.SoundEffectsEnabled;
        _volume = Math.Clamp(settings.SoundEffectsVolume, 0.0, 1.0);
        _isMuted = settings.MasterMuted;

        _settingsStore.Changed += OnSettingsChanged;
    }

    private void OnSettingsChanged(UiSettings settings)
    {
        _isEnabled = settings.SoundEffectsEnabled;
        _volume = Math.Clamp(settings.SoundEffectsVolume, 0.0, 1.0);
        _isMuted = settings.MasterMuted;
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        set => _isEnabled = value;
    }

    public bool IsMuted
    {
        get => _isMuted;
        set => _isMuted = value;
    }

    public double Volume
    {
        get => _volume;
        set => _volume = Math.Clamp(value, 0.0, 1.0);
    }

    public void Play(SoundEffectType type)
    {
        if (!_isEnabled || _isMuted || _volume <= 0.001)
        {
            return;
        }

        // 音效精简架构：仅保留基础轻微触觉音（Select / Click），全面屏蔽与移除复杂、刺耳的长音效（Error/Warning/Ok/Hover/Check/Uncheck）
        if (type is not (SoundEffectType.Select or SoundEffectType.Click))
        {
            return;
        }

        try
        {
            byte[] wavBytes = SoundEffectSynthesizer.GetWavBytes(type);
            PlayBytesInternal(wavBytes);
        }
        catch (Exception ex)
        {
            Log.Debug($"播放音效 {type} 失败: {ex.Message}");
        }
    }

    public void PlayCustom(byte[] wavBytes)
    {
        if (!_isEnabled || _isMuted || _volume <= 0.001 || wavBytes.Length == 0)
        {
            return;
        }

        try
        {
            PlayBytesInternal(wavBytes);
        }
        catch (Exception ex)
        {
            Log.Debug($"播放自定义音效失败: {ex.Message}");
        }
    }

    private void PlayBytesInternal(byte[] wavBytes)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        // 如果音量小于 1.0，动态缩放 PCM 采样
        byte[] bufferToPlay = wavBytes;
        if (_volume < 0.99)
        {
            bufferToPlay = ApplyVolume(wavBytes, _volume);
        }

        ThreadPool.QueueUserWorkItem(_ =>
        {
            GCHandle handle = default;
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    handle = GCHandle.Alloc(bufferToPlay, GCHandleType.Pinned);
                    PlaySound(handle.AddrOfPinnedObject(), nint.Zero, SND_SYNC | SND_MEMORY | SND_NODEFAULT);
                }
            }
            catch (Exception ex)
            {
                Log.Debug($"WinMM PlaySound 异常: {ex.Message}");
            }
            finally
            {
                if (handle.IsAllocated)
                {
                    handle.Free();
                }
            }
        });
    }

    private static byte[] ApplyVolume(byte[] wavBytes, double volume)
    {
        if (wavBytes.Length <= 44)
        {
            return wavBytes;
        }

        byte[] copy = new byte[wavBytes.Length];
        Array.Copy(wavBytes, copy, 44);

        int sampleCount = (wavBytes.Length - 44) / 2;
        for (int i = 0; i < sampleCount; i++)
        {
            int offset = 44 + (i * 2);
            short sample = (short)(wavBytes[offset] | (wavBytes[offset + 1] << 8));
            short scaled = (short)Math.Clamp(sample * volume, short.MinValue, short.MaxValue);
            copy[offset] = (byte)(scaled & 0xFF);
            copy[offset + 1] = (byte)((scaled >> 8) & 0xFF);
        }

        return copy;
    }
}
