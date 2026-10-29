using System.Diagnostics;

namespace CkanUI.Tools.Installer;

internal static class UninstallerEngine
{
    private static readonly string[] ExeCandidates = ["CkanUI.exe", "CkanUI.App.exe"];

    private static readonly HashSet<string> ForbiddenLeaves = new(StringComparer.OrdinalIgnoreCase)
    {
        "windows", "program files", "program files (x86)", "programdata", "users",
        "appdata", "localappdata", "roaming", "system32", "syswow64", "temp", "tmp",
        "downloads", "documents", "desktop", "start menu", "ckan",
    };

    public static int Run(InstallerOptions options)
    {
        Console.WriteLine("========================================");
        Console.WriteLine("        CKAN UI 原生卸载向导            ");
        Console.WriteLine("========================================");

        // 1. 定位并解析安装目录
        var installDir = ResolveInstallDirectory(options);
        if (string.IsNullOrWhiteSpace(installDir))
        {
            Console.Error.WriteLine("错误：未找到有效的安装目录。");
            return 1;
        }

        Console.WriteLine("[1] 目标卸载目录: " + installDir);

        // 2. 安全守卫检查
        var guardError = ValidateInstallDirectoryGuard(installDir);
        if (guardError != null)
        {
            Console.Error.WriteLine("安全守卫拦截: " + guardError);
            if (!options.Quiet)
            {
                NativeMethods.MessageBox(IntPtr.Zero, guardError, "卸载中止", NativeMethods.MB_OK | NativeMethods.MB_ICONERROR);
            }

            return 3;
        }

        // 3. 交互确认
        if (!options.Quiet)
        {
            var msg = "是否确定从本地计算机完全卸载 CKAN UI？\n\n" +
                      "安装目录: " + installDir + "\n" +
                      "（默认保留游戏 Mod 数据与共享配置）";
            var result = NativeMethods.MessageBox(
                IntPtr.Zero,
                msg,
                "CKAN UI 卸载确认",
                NativeMethods.MB_YESNO | NativeMethods.MB_ICONQUESTION);

            if (result != NativeMethods.IDYES)
            {
                Console.WriteLine("用户取消卸载。");
                return 0;
            }
        }

        // 4. 检测并结束运行中的进程
        Console.WriteLine("[2] 检测运行中的进程...");
        var running = GetRunningProcesses();
        if (running.Count > 0)
        {
            foreach (var p in running)
            {
                Console.WriteLine($"    检测到进程在运行: {p.ProcessName} (PID {p.Id})");
            }

            var stopAllowed = options.Force;
            if (!stopAllowed && !options.Quiet)
            {
                var prompt = "CKAN UI 正在运行。是否结束该进程并继续卸载？";
                var ask = NativeMethods.MessageBox(
                    IntPtr.Zero,
                    prompt,
                    "进程正在运行",
                    NativeMethods.MB_YESNO | NativeMethods.MB_ICONWARNING);

                if (ask == NativeMethods.IDYES)
                {
                    stopAllowed = true;
                }
            }

            if (!stopAllowed)
            {
                Console.Error.WriteLine("卸载已取消：请先关闭 CKAN UI。");
                return 2;
            }

            foreach (var p in running)
            {
                try
                {
                    p.Kill();
                    p.WaitForExit(3000);
                    Console.WriteLine($"    已结束进程 PID {p.Id}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"    结束进程 PID {p.Id} 失败: {ex.Message}");
                }
            }
        }

        // 5. 删除快捷方式
        Console.WriteLine("[3] 清理系统快捷方式...");
        var programsDir = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
        var desktopDir = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

        if (!string.IsNullOrEmpty(programsDir))
        {
            ShortcutHelper.DeleteShortcut(Path.Combine(programsDir, "CKAN UI.lnk"));
            ShortcutHelper.DeleteShortcut(Path.Combine(programsDir, "CKAN UI", "卸载 CKAN UI.lnk"));
        }

        if (!string.IsNullOrEmpty(desktopDir))
        {
            ShortcutHelper.DeleteShortcut(Path.Combine(desktopDir, "CKAN UI.lnk"));
        }

        // 6. 清理 HKCU 注册表
        Console.WriteLine("[4] 清理 HKCU 卸载项与 App Paths...");
        RegistryHelper.UnregisterUninstallEntry(installDir);
        RegistryHelper.UnregisterAppPaths(ExeCandidates, installDir);

        // 7. 处理用户数据
        Console.WriteLine("[5] 处理用户数据...");
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var ckanDataDir = !string.IsNullOrEmpty(localAppData) ? Path.Combine(localAppData, "CKAN") : null;

        if (options.RemoveUserData && ckanDataDir != null && Directory.Exists(ckanDataDir))
        {
            Console.WriteLine("    按 -RemoveUserData 清理独占配置与日志...");
            foreach (var file in new[] { "ckan-ui.json", "ckan-ui.secrets", "ckan-ui.json.bak" })
            {
                var p = Path.Combine(ckanDataDir, file);
                if (File.Exists(p))
                {
                    try { File.Delete(p); } catch { }
                }
            }

            try
            {
                foreach (var log in Directory.GetFiles(ckanDataDir, "ckanui-*.log"))
                {
                    try { File.Delete(log); } catch { }
                }
            }
            catch { }

            Console.WriteLine("    保留共享数据（config.json、repos、CKAN.log）。");
        }
        else
        {
            Console.WriteLine(@"    保留用户数据（%LocalAppData%\CKAN）。");
        }

        // 8. 删除安装目录
        Console.WriteLine("[6] 删除安装目录文件: " + installDir);
        if (Directory.Exists(installDir))
        {
            var selfExe = Environment.ProcessPath;
            var isRunningFromInstallDir = !string.IsNullOrEmpty(selfExe) &&
                                          selfExe.StartsWith(installDir, StringComparison.OrdinalIgnoreCase);

            if (!isRunningFromInstallDir)
            {
                // 自身在外部运行（例如临时安装包或测试），直接整目录删除
                try
                {
                    Directory.Delete(installDir, true);
                    Console.WriteLine("    已完全删除安装目录。");
                }
                catch (Exception ex)
                {
                    Console.WriteLine("    删除安装目录时发生部分异常: " + ex.Message);
                }
            }
            else
            {
                // 自身在安装目录下运行，先删除所有子目录和非自身文件，再通过延迟自删清理剩余
                Console.WriteLine("    正在清理安装目录子项目...");
                DeleteContentsExceptSelf(installDir, selfExe!);

                Console.WriteLine("    调度自清理任务...");
                ScheduleSelfDeletion(installDir);
            }
        }

        Console.WriteLine("========================================");
        Console.WriteLine("         CKAN UI 卸载完成！             ");
        Console.WriteLine("========================================");

        if (!options.Quiet)
        {
            NativeMethods.MessageBox(
                IntPtr.Zero,
                "CKAN UI 已成功从计算机中卸载。",
                "卸载完成",
                NativeMethods.MB_OK | NativeMethods.MB_ICONINFORMATION);
        }

        return 0;
    }

    private static string? ResolveInstallDirectory(InstallerOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.InstallDir))
        {
            return Path.GetFullPath(options.InstallDir).TrimEnd('\\', '/');
        }

        // 从自身所在目录推断
        var selfDir = AppContext.BaseDirectory.TrimEnd('\\', '/');
        if (Directory.Exists(Path.Combine(selfDir, "app")) || Directory.Exists(Path.Combine(selfDir, "current")))
        {
            return Path.GetFullPath(selfDir);
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            return Path.Combine(localAppData, "CKAN UI");
        }

        return null;
    }

    private static string? ValidateInstallDirectoryGuard(string installDir)
    {
        if (Path.GetPathRoot(installDir) == installDir)
        {
            return "拒绝删除盘符根目录: " + installDir;
        }

        var leaf = Path.GetFileName(installDir);
        var parent = Path.GetDirectoryName(installDir);

        if (string.IsNullOrEmpty(leaf) || string.IsNullOrEmpty(parent))
        {
            return "无法解析安装目录路径结构: " + installDir;
        }

        if (ForbiddenLeaves.Contains(leaf))
        {
            return "拒绝删除系统或共享保留目录: " + installDir;
        }

        if (Directory.Exists(installDir))
        {
            var hasMarker = Directory.Exists(Path.Combine(installDir, "app")) ||
                            Directory.Exists(Path.Combine(installDir, "current")) ||
                            File.Exists(Path.Combine(installDir, "CkanUISetup.exe")) ||
                            File.Exists(Path.Combine(installDir, "uninstall.ps1"));

            if (!hasMarker)
            {
                return "目录中未检测到 CKAN UI 的安装标记（app/、current/、CkanUISetup.exe），拒绝删除: " + installDir;
            }
        }

        return null;
    }

    private static List<Process> GetRunningProcesses()
    {
        var list = new List<Process>();
        foreach (var candidate in ExeCandidates)
        {
            var name = Path.GetFileNameWithoutExtension(candidate);
            list.AddRange(Process.GetProcessesByName(name));
        }

        return list;
    }

    private static void DeleteContentsExceptSelf(string rootDir, string selfExePath)
    {
        foreach (var dir in Directory.GetDirectories(rootDir))
        {
            try
            {
                Directory.Delete(dir, true);
            }
            catch
            {
                // 忽略重试
            }
        }

        foreach (var file in Directory.GetFiles(rootDir))
        {
            if (string.Equals(file, selfExePath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                File.Delete(file);
            }
            catch
            {
                // 忽略占用
            }
        }
    }

    private static void ScheduleSelfDeletion(string installDir)
    {
        try
        {
            var cmd = $"/c ping 127.0.0.1 -n 2 > nul & rmdir /s /q \"{installDir}\"";
            Process.Start(new ProcessStartInfo("cmd.exe", cmd)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden,
            });
        }
        catch
        {
            // 忽略延迟删除调度错误
        }
    }
}
