using System.Reflection;
using System.Runtime.Versioning;
using log4net;

namespace CkanUI.Services.Audio;

/// <summary>
/// 针对 Windows Media Player COM 核心（WMPlayer.OCX）的类型安全反射封装。
/// 完全兼容 Windows 10/11 64位环境，支持播放 MP3、WAV、OGG、WMA、AAC、FLAC 等多种格式，
/// 免去引入重型互操作库，且不依赖外部播放器进程。
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsMediaPlayerWrapper : IDisposable
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(WindowsMediaPlayerWrapper));

    private readonly object _player;
    private readonly Type _playerType;
    private readonly object? _controls;
    private readonly Type? _controlsType;
    private readonly object? _settings;
    private readonly Type? _settingsType;

    private WindowsMediaPlayerWrapper(object player, Type playerType)
    {
        _player = player;
        _playerType = playerType;

        _controls = _playerType.InvokeMember("controls", BindingFlags.GetProperty, null, _player, null);
        _controlsType = _controls?.GetType();

        _settings = _playerType.InvokeMember("settings", BindingFlags.GetProperty, null, _player, null);
        _settingsType = _settings?.GetType();

        // 默认配置
        if (_settings != null && _settingsType != null)
        {
            try
            {
                _settingsType.InvokeMember("autoStart", BindingFlags.SetProperty, null, _settings, [true]);
            }
            catch (Exception ex)
            {
                Log.Debug($"配置 autoStart 异常: {ex.Message}");
            }

            // 关掉 WMP 自己的错误/格式确认框。对 .ogg 这类系统没注册解码器的扩展名，
            // WMP 会在设置 URL 时弹出「旧版 Windows Media Player」对话框打断整个界面；
            // 这里让它静默失败，由播放服务根据 PlayState 判断是否真的在播。
            try
            {
                _settingsType.InvokeMember("enableErrorDialogs", BindingFlags.SetProperty, null, _settings, [false]);
            }
            catch (Exception ex)
            {
                Log.Debug($"配置 enableErrorDialogs 异常: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// 尝试创建并初始化 Windows Media Player COM 实例。
    /// 若非 Windows 环境或 COM 被禁用，返回 null。
    /// </summary>
    public static WindowsMediaPlayerWrapper? TryCreate()
    {
        try
        {
            var type = Type.GetTypeFromProgID("WMPlayer.OCX");
            if (type == null)
            {
                Log.Warn("未找到 WMPlayer.OCX COM 注册项");
                return null;
            }

            object? instance = Activator.CreateInstance(type);
            if (instance == null)
            {
                Log.Warn("实例化 WMPlayer.OCX 失败");
                return null;
            }

            return new WindowsMediaPlayerWrapper(instance, type);
        }
        catch (Exception ex)
        {
            Log.Warn($"初始化 Windows Media Player COM 失败: {ex.Message}");
            return null;
        }
    }

    public void Open(string filePath)
    {
        try
        {
            _playerType.InvokeMember("URL", BindingFlags.SetProperty, null, _player, [filePath]);
        }
        catch (Exception ex)
        {
            Log.Warn($"打开音频文件失败 [{filePath}]: {ex.Message}");
        }
    }

    public void Play()
    {
        if (_controls == null || _controlsType == null) return;
        try
        {
            _controlsType.InvokeMember("play", BindingFlags.InvokeMethod, null, _controls, null);
        }
        catch (Exception ex)
        {
            Log.Debug($"调用 play 异常: {ex.Message}");
        }
    }

    public void Pause()
    {
        if (_controls == null || _controlsType == null) return;
        try
        {
            _controlsType.InvokeMember("pause", BindingFlags.InvokeMethod, null, _controls, null);
        }
        catch (Exception ex)
        {
            Log.Debug($"调用 pause 异常: {ex.Message}");
        }
    }

    public void Stop()
    {
        if (_controls == null || _controlsType == null) return;
        try
        {
            _controlsType.InvokeMember("stop", BindingFlags.InvokeMethod, null, _controls, null);
        }
        catch (Exception ex)
        {
            Log.Debug($"调用 stop 异常: {ex.Message}");
        }
    }

    public int PlayState
    {
        get
        {
            try
            {
                object? res = _playerType.InvokeMember("playState", BindingFlags.GetProperty, null, _player, null);
                return res is int state ? state : 0;
            }
            catch
            {
                return 0;
            }
        }
    }

    public double PositionSeconds
    {
        get
        {
            if (_controls == null || _controlsType == null) return 0;
            try
            {
                object? res = _controlsType.InvokeMember("currentPosition", BindingFlags.GetProperty, null, _controls, null);
                return res is double pos ? pos : 0;
            }
            catch
            {
                return 0;
            }
        }
        set
        {
            if (_controls == null || _controlsType == null) return;
            try
            {
                _controlsType.InvokeMember("currentPosition", BindingFlags.SetProperty, null, _controls, [value]);
            }
            catch (Exception ex)
            {
                Log.Debug($"设置 currentPosition 异常: {ex.Message}");
            }
        }
    }

    public double DurationSeconds
    {
        get
        {
            try
            {
                object? media = _playerType.InvokeMember("currentMedia", BindingFlags.GetProperty, null, _player, null);
                if (media == null) return 0;
                object? dur = media.GetType().InvokeMember("duration", BindingFlags.GetProperty, null, media, null);
                return dur is double d ? d : 0;
            }
            catch
            {
                return 0;
            }
        }
    }

    public int Volume
    {
        get
        {
            if (_settings == null || _settingsType == null) return 100;
            try
            {
                object? res = _settingsType.InvokeMember("volume", BindingFlags.GetProperty, null, _settings, null);
                return res is int vol ? vol : 100;
            }
            catch
            {
                return 100;
            }
        }
        set
        {
            if (_settings == null || _settingsType == null) return;
            try
            {
                int clamped = Math.Clamp(value, 0, 100);
                _settingsType.InvokeMember("volume", BindingFlags.SetProperty, null, _settings, [clamped]);
            }
            catch (Exception ex)
            {
                Log.Debug($"设置 volume 异常: {ex.Message}");
            }
        }
    }

    public bool Mute
    {
        get
        {
            if (_settings == null || _settingsType == null) return false;
            try
            {
                object? res = _settingsType.InvokeMember("mute", BindingFlags.GetProperty, null, _settings, null);
                return res is bool m && m;
            }
            catch
            {
                return false;
            }
        }
        set
        {
            if (_settings == null || _settingsType == null) return;
            try
            {
                _settingsType.InvokeMember("mute", BindingFlags.SetProperty, null, _settings, [value]);
            }
            catch (Exception ex)
            {
                Log.Debug($"设置 mute 异常: {ex.Message}");
            }
        }
    }

    public void Dispose()
    {
        try
        {
            Stop();
            _playerType.InvokeMember("close", BindingFlags.InvokeMethod, null, _player, null);
        }
        catch
        {
            // 忽略清理异常
        }
    }
}
