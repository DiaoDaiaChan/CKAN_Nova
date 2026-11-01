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
        // 1. 定位并解析安装目录
        var installDir = ResolveInstallDirectory(options);
        if (string.IsNullOrWhiteSpace(installDir))
        {
            Console.Error.WriteLine("错误：未找到有效的安装目录。");
            return 1;
        }

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

        // 3. 如果非静默且非强制，启动原生 GUI 卸载向导
        if (!options.Quiet)
        {
            return UninstallerWizard.Run(options, installDir);
        }

        // 静默模式直接执行
        return ExecuteUninstall(options, null);
    }

    public static int ExecuteUninstall(InstallerOptions options, Action<int, string>? progressCallback)
    {
        var installDir = ResolveInstallDirectory(options);
        if (string.IsNullOrWhiteSpace(installDir)) return 1;

        progressCallback?.Invoke(10, "检测运行中的进程...");
        var running = GetRunningProcesses();
        if (running.Count > 0)
        {
            foreach (var p in running)
            {
                try
                {
                    p.Kill();
                    p.WaitForExit(3000);
                }
                catch { }
            }
        }

        progressCallback?.Invoke(30, "清理系统快捷方式...");
        var programsDir = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
        var desktopDir = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

        if (!string.IsNullOrEmpty(programsDir))
        {
            ShortcutHelper.DeleteShortcut(Path.Combine(programsDir, "CKAN UI.lnk"));
            ShortcutHelper.DeleteShortcut(Path.Combine(programsDir, "CKAN UI", "卸载 CKAN UI.lnk"));
            var menuDir = Path.Combine(programsDir, "CKAN UI");
            if (Directory.Exists(menuDir))
            {
                try { Directory.Delete(menuDir, true); } catch { }
            }
        }

        if (!string.IsNullOrEmpty(desktopDir))
        {
            ShortcutHelper.DeleteShortcut(Path.Combine(desktopDir, "CKAN UI.lnk"));
        }

        progressCallback?.Invoke(50, "清理注册表项与路径...");
        RegistryHelper.UnregisterUninstallEntry(installDir);
        RegistryHelper.UnregisterAppPaths(ExeCandidates, installDir);

        progressCallback?.Invoke(70, "处理用户数据与配置...");
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var ckanDataDir = !string.IsNullOrEmpty(localAppData) ? Path.Combine(localAppData, "CKAN") : null;

        if (options.RemoveUserData && ckanDataDir != null && Directory.Exists(ckanDataDir))
        {
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
        }

        progressCallback?.Invoke(90, "清理安装目录文件...");
        if (Directory.Exists(installDir))
        {
            var selfExe = Environment.ProcessPath;
            var isRunningFromInstallDir = !string.IsNullOrEmpty(selfExe) &&
                                          selfExe.StartsWith(installDir, StringComparison.OrdinalIgnoreCase);

            if (!isRunningFromInstallDir)
            {
                try
                {
                    Directory.Delete(installDir, true);
                }
                catch { }
            }
            else
            {
                DeleteContentsExceptSelf(installDir, selfExe!);
                ScheduleSelfDeletion(installDir);
            }
        }

        progressCallback?.Invoke(100, "卸载完成");
        return 0;
    }

    private static string? ResolveInstallDirectory(InstallerOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.InstallDir))
        {
            return Path.GetFullPath(options.InstallDir).TrimEnd('\\', '/');
        }

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
            catch { }
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
            catch { }
        }
    }

    private static void ScheduleSelfDeletion(string installDir)
    {
        try
        {
            var cmd = $"/c ping 127.0.0.1 -n 2 > nul & rmdir /s /q \"{installDir}\"";
            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = cmd,
                CreateNoWindow = true,
                UseShellExecute = false
            });
        }
        catch { }
    }
}
