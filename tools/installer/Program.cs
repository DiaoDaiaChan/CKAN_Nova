using System.Runtime.Versioning;
using System.Text;

[assembly: SupportedOSPlatform("windows")]

namespace CkanUI.Tools.Installer;

public static class Program
{
    public static int Main(string[] args)
    {
        // 尝试附加到父进程控制台（cmd/powershell/CI 执行时可直接输出日志）
        var attached = NativeMethods.AttachConsole(NativeMethods.ATTACH_PARENT_PROCESS);

        // 无论是否真的挂上了控制台，输出都统一成 UTF-8：
        // 控制台存在时顺带把代码页切到 65001，否则 cmd 按 GBK 解释就是整屏乱码；
        // 被重定向到文件/管道时同样写 UTF-8，保证日志、issue 附件里的中文不再变形。
        try
        {
            if (attached)
            {
                NativeMethods.SetConsoleOutputCP(65001);
                NativeMethods.SetConsoleCP(65001);
            }

            var stdout = Console.OpenStandardOutput();
            var writer = new StreamWriter(stdout, new UTF8Encoding(false)) { AutoFlush = true };
            Console.SetOut(writer);
            Console.SetError(writer);
        }
        catch
        {
            // 无控制台句柄（双击启动的 WinExe）或重定向失败：忽略，界面照常走 MessageBox
        }

        var options = InstallerOptions.Parse(args);

        if (options.ShowHelp)
        {
            PrintHelp();
            return 0;
        }

        if (options.Diagnose)
        {
            // 纯读诊断：用户把这段贴出来，路径/权限/残留痕迹一目了然。
            Console.WriteLine(InstallLocation.Diagnose(options.InstallDir));
            return 0;
        }

        try
        {
            if (options.IsUninstall)
            {
                return UninstallerEngine.Run(options);
            }

            var installCode = InstallerEngine.Run(options);

            // 可降级失败（快捷方式没建成等）必须让用户看见，否则「装好了但开始菜单没有」永远说不清。
            foreach (var warning in options.Warnings)
            {
                Console.Error.WriteLine("警告: " + warning);
            }

            foreach (var detail in options.WarningDetails)
            {
                Console.Error.WriteLine("  详情: " + detail);
            }

            return installCode;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("致命错误: " + ex);
            if (!options.Quiet)
            {
                NativeMethods.MessageBox(
                    IntPtr.Zero,
                    "安装程序执行时发生异常：\n\n" + ex.Message,
                    "CKAN UI 安装程序错误",
                    NativeMethods.MB_OK | NativeMethods.MB_ICONERROR);
            }

            return 1;
        }
        finally
        {
            if (attached)
            {
                NativeMethods.FreeConsole();
            }
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine(@"CKAN UI 原生安装程序 (CkanUISetup.exe)

用法:
  CkanUISetup.exe [选项...]

安装选项:
  -InstallDir <path>   指定安装目标目录（默认: %LocalAppData%\CKAN UI）
  /DIR=<path>          同 -InstallDir
  -SourceDir <path>    指定待安装的文件源目录（开发/调试用）
  -Quiet, -Silent, /S  静默安装，不弹出对话框
  -NoShortcuts         不创建桌面与开始菜单快捷方式（等于同时关掉下面两项）
  -NoDesktopShortcut   不创建桌面快捷方式
  -NoStartMenuShortcut 不创建开始菜单快捷方式
  -NoAppPath           不注册 HKCU App Paths（CMD/运行里不能直接输 CkanUI）
  -NoRegistry          不写任何注册表（卸载项 + App Paths 全跳过；绿色安装）
  -ShortcutRoot <path> 快捷方式根目录覆盖（可移植版/自动化测试；默认写真实开始菜单与桌面）
  -NoLaunch            安装完成后不自动启动 CKAN UI
  -Force               检测到同名进程运行时继续安装（不强制关闭）

卸载选项:
  -Uninstall           执行卸载操作
  -RemoveUserData      卸载时同时删除独占的用户数据（日志与自有配置）
  -Force               卸载时自动终止正在运行的 CKAN UI 进程

其它:
  -Diagnose            只打印安装环境诊断（用户/提权/逐级写权限/上次安装痕迹），不改动任何文件
  -Help, /?            显示本帮助信息
");
    }
}
