using System.Runtime.Versioning;

namespace CkanUI.Services.Platform;

/// <summary>
/// 注册表底层读取适配器，按架构契约（02 篇 / NamespaceRulesTests）注册表访问只允许收敛在 Platform 命名空间内。
/// </summary>
public static class RegistryAccessor
{
    /// <summary>
    /// 读取字符串注册表项，失败或非 Windows 返回 null。
    /// </summary>
    public static string? GetString(string key, string? valueName)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        return ReadWindowsRegistry(key, valueName);
    }

    [SupportedOSPlatform("windows")]
    private static string? ReadWindowsRegistry(string key, string? valueName)
    {
        try
        {
            return Microsoft.Win32.Registry.GetValue(key, valueName, null) as string;
        }
        catch
        {
            return null;
        }
    }
}
