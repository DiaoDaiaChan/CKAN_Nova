using System.Runtime.InteropServices;

using CkanUI.Services.Abstractions.Input;

namespace CkanUI.Platform.Input;

/// <summary>
/// Windows 手柄来源：通过 XInput 的 <c>XInputGetState</c> 读第 0–3 号槽位里第一个在线的手柄。
/// </summary>
/// <remarks>
/// <para><b>为什么不用 <c>[DllImport]</c></b>：本仓库的 TFM 是跨平台的 <c>net10.0</c>（见 Directory.Build.props），
/// 静态绑定 <c>xinput1_4.dll</c> 会在 Linux / macOS 上一启动就 <c>DllNotFoundException</c>。
/// 这里用 <see cref="NativeLibrary.TryLoad(string, out IntPtr)"/> 延迟探测 + 函数指针委托，
/// 探不到就 <see cref="IsAvailable"/> 为 false，外壳据此整块不启轮询——手柄是加分项，不能变成启动阻塞项。</para>
/// <para>兼容层：<c>xinput1_4</c>（Win8+）优先，退到 <c>xinput9_1_0</c>（Win7 自带），再退 <c>xinput1_3</c>
/// （DirectX 运行库自带）。三者 <c>XInputGetState</c> 的参数与结构体布局一致。</para>
/// </remarks>
public sealed class XInputGamepadSource : IGamepadSource, IDisposable
{
    /// <summary>一次轮询最多探几个槽位（XInput 上限就是 4）。</summary>
    private const uint MaxSlots = 4;

    /// <summary>摇杆满量程（short 正半轴）。</summary>
    private const float ThumbRange = 32767f;

    /// <summary>扳机满量程（byte）。</summary>
    private const float TriggerRange = 255f;

    private static readonly string[] CandidateLibraries = ["xinput1_4.dll", "xinput9_1_0.dll", "xinput1_3.dll"];

    private readonly XInputGetStateDelegate? _getState;
    private IntPtr _library;
    private bool _disposed;

    /// <summary>探测本机 XInput；找不到任何一份库时构造出一个「永远离线」的来源。</summary>
    public XInputGamepadSource()
    {
        foreach (var name in CandidateLibraries)
        {
            if (!NativeLibrary.TryLoad(name, out var handle))
            {
                continue;
            }

            if (NativeLibrary.TryGetExport(handle, "XInputGetState", out var entry))
            {
                _library = handle;
                _getState = Marshal.GetDelegateForFunctionPointer<XInputGetStateDelegate>(entry);
                return;
            }

            NativeLibrary.Free(handle);
        }
    }

    /// <inheritdoc />
    public bool IsAvailable => _getState is not null;

    /// <inheritdoc />
    public GamepadSnapshot Poll()
    {
        if (_getState is null || _disposed)
        {
            return GamepadSnapshot.Disconnected;
        }

        for (uint slot = 0; slot < MaxSlots; slot++)
        {
            if (_getState(slot, out var state) != 0)
            {
                continue;
            }

            var pad = state.Gamepad;
            return new GamepadSnapshot(
                IsConnected: true,
                Buttons: (GamepadButtons)pad.Buttons,
                LeftThumbX: NormalizeAxis(pad.ThumbLX),
                LeftThumbY: NormalizeAxis(pad.ThumbLY),
                LeftTrigger: pad.LeftTrigger / TriggerRange,
                RightTrigger: pad.RightTrigger / TriggerRange);
        }

        return GamepadSnapshot.Disconnected;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_library != IntPtr.Zero)
        {
            NativeLibrary.Free(_library);
            _library = IntPtr.Zero;
        }
    }

    private static float NormalizeAxis(short value)
    {
        // -32768 除以 32767 会得到 -1.000031，夹一下保证归一到 [-1, 1]。
        var normalized = value / ThumbRange;
        return Math.Clamp(normalized, -1f, 1f);
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate uint XInputGetStateDelegate(uint userIndex, out XInputState state);

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputState
    {
        public uint PacketNumber;
        public XInputGamepad Gamepad;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputGamepad
    {
        public ushort Buttons;
        public byte LeftTrigger;
        public byte RightTrigger;
        public short ThumbLX;
        public short ThumbLY;
        public short ThumbRX;
        public short ThumbRY;
    }
}
