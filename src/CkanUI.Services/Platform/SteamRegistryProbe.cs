using System.Runtime.Versioning;
using System.Security;

using log4net;

using Microsoft.Win32;

namespace CkanUI.Services.Platform;

/// <summary>
/// 从注册表取 Steam 安装根（03 篇 §5，与上游 <c>external/CKAN/Core/IO/SteamLibrary.cs:145-151</c> 一致）。
/// </summary>
/// <remarks>
/// <para>只读 <c>HKCU\Software\Valve\Steam</c> 的 <c>SteamPath</c>：上游就只查这一处，没有 HKLM 32/64 位回退，
/// 这里也不自作聪明地多查——多查的结果是「用户换了绿色版 Steam，我们却指向一个已卸载路径」。</para>
/// <para>读注册表在非 Windows 上必然抛，所以先看 <see cref="OperatingSystem.IsWindows"/>；
/// 任何权限/IO 异常都吞成 <c>null</c>（探测失败等于没找到，不该让「添加实例」整页报错）。</para>
/// </remarks>
public sealed class SteamRegistryProbe
{
    /// <summary>Steam 的注册表键（HKCU，字符串形式便于日志与测试断言）。</summary>
    public const string SteamKey = @"HKEY_CURRENT_USER\Software\Valve\Steam";

    /// <summary>存放 Steam 安装根的键值名。</summary>
    public const string SteamPathValue = "SteamPath";

    private static readonly ILog Log = LogManager.GetLogger(typeof(SteamRegistryProbe));

    private readonly Func<string, string?, string?>? _readValue;
    private readonly bool _isWindows;

    /// <summary>构造探测器。</summary>
    /// <param name="readValue">键值读取器，签名同 <c>Registry.GetValue(key, valueName, default)</c>；留给测试注入。</param>
    /// <param name="isWindows">是否按 Windows 处理；<c>null</c> = 看当前系统。</param>
    public SteamRegistryProbe(Func<string, string?, string?>? readValue = null, bool? isWindows = null)
    {
        _readValue = readValue;
        _isWindows = isWindows ?? OperatingSystem.IsWindows();
    }

    /// <summary>返回 Steam 安装根；非 Windows、键不存在或读取失败时返回 <c>null</c>。</summary>
    public string? TryGetSteamPath()
    {
        if (!_isWindows)
        {
            return null;
        }

        // 用运行时判断（而不是只看字段）满足 CA1416：注入的读取器仍然是 Windows-only 的调用路径。
        return OperatingSystem.IsWindows() ? ReadSteamPath() : null;
    }

    [SupportedOSPlatform("windows")]
    private string? ReadSteamPath()
    {
        try
        {
            var reader = _readValue ?? ReadRegistryValue;
            var raw = reader(SteamKey, SteamPathValue);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            // 老版 Steam 写的是正斜杠（C:/Program Files/Steam），统一成反斜杠再拼子路径。
            return raw.Trim().Replace('/', '\\');
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            Log.Warn("读取 Steam 注册表失败，按未安装处理。", ex);
            return null;
        }
    }

    [SupportedOSPlatform("windows")]
    // 全限定：本程序集自己有个 CkanUI.Services.Registry 命名空间，会遮蔽 Microsoft.Win32.Registry。
    private static string? ReadRegistryValue(string key, string? valueName) =>
        Microsoft.Win32.Registry.GetValue(key, valueName, null) as string;
}
