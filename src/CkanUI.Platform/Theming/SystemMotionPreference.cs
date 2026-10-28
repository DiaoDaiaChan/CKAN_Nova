using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace CkanUI.Platform.Theming;

/// <summary>
/// 读取系统的「减少动画」设置（20 篇 §11：默认跟随系统）。
/// </summary>
/// <remarks>
/// <para>Windows 上对应「设置 → 辅助功能 → 视觉效果 → 动画效果」，也就是
/// <c>SystemParametersInfo(SPI_GETCLIENTAREAANIMATION)</c>；关掉它时系统会取消控件的位移动画。</para>
/// <para>非 Windows（开发机上的 headless 测试、将来的 Linux 端口）一律按「允许动画」处理：
/// 读不到就当没关，比误关动效更不容易让人以为界面卡死。</para>
/// </remarks>
public static class SystemMotionPreference
{
    private const int SpiGetClientAreaAnimation = 0x1042;

    /// <summary>系统当前是否允许动画。</summary>
    public static bool AnimationsEnabled
    {
        get
        {
            if (!OperatingSystem.IsWindows())
            {
                return true;
            }

            try
            {
                return ReadClientAreaAnimation();
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or MarshalDirectiveException)
            {
                // 精简版/服务化 Windows 上 user32 可能缺方法：按允许处理，不因读偏好失败而改界面行为。
                return true;
            }
        }
    }

    [SupportedOSPlatform("windows")]
    private static bool ReadClientAreaAnimation()
    {
        var enabled = 0;
        var ok = SystemParametersInfo(SpiGetClientAreaAnimation, 0, ref enabled, 0);
        return ok == 0 || enabled != 0;
    }

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    [SupportedOSPlatform("windows")]
    private static extern int SystemParametersInfo(int action, int param, ref int value, int winIni);
}
