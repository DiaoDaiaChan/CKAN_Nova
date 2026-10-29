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
        if (attached)
        {
            try
            {
                var stdout = Console.OpenStandardOutput();
                var writer = new StreamWriter(stdout, Encoding.UTF8) { AutoFlush = true };
                Console.SetOut(writer);
                Console.SetError(writer);
            }
            catch
            {
                // 忽略重定向失败
            }
        }

        var options = InstallerOptions.Parse(args);

        if (options.ShowHelp)
        {
            PrintHelp();
            return 0;
        }

        try
        {
            if (options.IsUninstall)
            {
                return UninstallerEngine.Run(options);
            }

            return InstallerEngine.Run(options);
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
  -NoShortcuts         不创建桌面与开始菜单快捷方式
  -NoLaunch            安装完成后不自动启动 CKAN UI
  -Force               检测到同名进程运行时继续安装（不强制关闭）

卸载选项:
  -Uninstall           执行卸载操作
  -RemoveUserData      卸载时同时删除独占的用户数据（日志与自有配置）
  -Force               卸载时自动终止正在运行的 CKAN UI 进程

其它:
  -Help, /?            显示本帮助信息
");
    }
}
