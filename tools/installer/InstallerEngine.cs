using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;

namespace CkanUI.Tools.Installer;

internal static class InstallerEngine
{
    private static readonly string[] ExeCandidates = ["CkanUI.exe", "CkanUI.App.exe"];

    public static int Run(InstallerOptions options)
    {
        // 1. 如果非静默，直接进入 5 步现代化向导
        if (!options.Quiet)
        {
            return InstallerWizard.Run(options);
        }

        // 2. 静默模式直接执行安装逻辑
        return ExecuteInstall(options, null, out _, out _);
    }

    public static int ExecuteInstall(InstallerOptions options, Action<int, string>? progressCallback, out string installedAppExe, out string installedVersion)
    {
        installedAppExe = string.Empty;
        installedVersion = string.Empty;

        // 1. 确认安装目录
        var installDir = options.InstallDir;
        if (string.IsNullOrWhiteSpace(installDir))
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(localAppData))
            {
                Console.Error.WriteLine("错误：未找到 LOCALAPPDATA 环境变量，请指定 -InstallDir。");
                return 1;
            }

            installDir = Path.Combine(localAppData, "CKAN UI");
        }

        installDir = Path.GetFullPath(installDir).TrimEnd('\\', '/');
        if (Path.GetPathRoot(installDir) == installDir)
        {
            Console.Error.WriteLine("错误：安装目录不能为盘符根目录: " + installDir);
            return 1;
        }

        // 2. 检测运行中的进程
        progressCallback?.Invoke(10, "检测正在运行的进程...");
        var running = GetRunningProcesses();
        if (running.Count > 0 && !options.Force)
        {
            var tip = "检测到 CKAN UI 正在运行。请先关闭应用，再重新运行安装程序。";
            Console.Error.WriteLine("错误: " + tip);
            return 2;
        }

        // 3. 定位 Payload 源
        progressCallback?.Invoke(25, "准备安装数据源...");
        var payloadSource = ResolvePayloadSource(options);
        if (payloadSource == null)
        {
            var err = "未能找到有效的安装源文件（内嵌 payload.zip、-SourceDir 或本地安装包）。";
            Console.Error.WriteLine("错误: " + err);
            return 1;
        }

        // 4. 准备 Staging 目录并解压/复制
        var appRoot = Path.Combine(installDir, "app");
        var currentDir = Path.Combine(installDir, "current");
        var stagingDir = Path.Combine(appRoot, ".staging-" + Guid.NewGuid().ToString("N")[..8]);

        var rollbackActions = new List<Action>();
        try
        {
            Directory.CreateDirectory(appRoot);
            rollbackActions.Add(() =>
            {
                if (Directory.Exists(stagingDir))
                {
                    try { Directory.Delete(stagingDir, true); } catch { }
                }
            });

            progressCallback?.Invoke(40, "正在解压并写入应用程序核心文件...");
            ExtractOrCopyPayload(payloadSource, stagingDir);

            // 5. 确定主程序名与版本号
            var (mainExeName, version) = DetermineExeAndVersion(stagingDir);
            var targetVersionDir = Path.Combine(appRoot, version);
            var backupDir = string.Empty;

            if (Directory.Exists(targetVersionDir))
            {
                backupDir = Path.Combine(appRoot, $".backup-{version}-{DateTime.Now:yyyyMMddHHmmss}");
                Directory.Move(targetVersionDir, backupDir);
                rollbackActions.Add(() =>
                {
                    if (Directory.Exists(backupDir) && !Directory.Exists(targetVersionDir))
                    {
                        try { Directory.Move(backupDir, targetVersionDir); } catch { }
                    }
                });
            }

            // 6. 原子替换就位
            progressCallback?.Invoke(65, "正在更新版本目录与符号链接...");
            Directory.Move(stagingDir, targetVersionDir);

            if (!string.IsNullOrEmpty(backupDir) && Directory.Exists(backupDir))
            {
                try { Directory.Delete(backupDir, true); } catch { }
            }

            // 7. 写入 current 指针与说明
            Directory.CreateDirectory(currentDir);
            File.WriteAllText(Path.Combine(currentDir, "version.txt"), version + Environment.NewLine);
            File.WriteAllText(
                Path.Combine(currentDir, "README.txt"),
                "本目录是「当前版本」指针，不含程序文件。\r\n\r\n" +
                "  version.txt   当前生效的版本号\r\n" +
                "  程序本体      ..\\app\\<版本号>\\\r\n" +
                "  卸载入口      ..\\CkanUISetup.exe -Uninstall\r\n");

            // 复制许可证等附加文件
            foreach (var notice in new[] { "LICENSE.md", "THIRD-PARTY-NOTICES.txt" })
            {
                var srcFile = Path.Combine(targetVersionDir, notice);
                if (File.Exists(srcFile))
                {
                    try { File.Copy(srcFile, Path.Combine(installDir, notice), true); } catch { }
                }
            }

            // 8. 自克隆作为独立卸载器
            var selfExe = Environment.ProcessPath;
            var installerInTarget = Path.Combine(installDir, "CkanUISetup.exe");
            if (!string.IsNullOrEmpty(selfExe) && File.Exists(selfExe))
            {
                try
                {
                    File.Copy(selfExe, installerInTarget, true);
                }
                catch { }
            }

            var appExePath = Path.Combine(targetVersionDir, mainExeName);

            // 9. 创建快捷方式
            progressCallback?.Invoke(80, "正在注册桌面与开始菜单快捷方式...");
            if (!options.NoShortcuts)
            {
                var programsDir = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
                var desktopDir = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

                if (Directory.Exists(programsDir))
                {
                    var startMenuLink = Path.Combine(programsDir, "CKAN UI.lnk");
                    ShortcutHelper.CreateShortcut(startMenuLink, appExePath, targetVersionDir, "CKAN UI");

                    // 开始菜单增加独立卸载入口
                    var menuSubDir = Path.Combine(programsDir, "CKAN UI");
                    Directory.CreateDirectory(menuSubDir);
                    var uninstLink = Path.Combine(menuSubDir, "卸载 CKAN UI.lnk");
                    ShortcutHelper.CreateShortcut(uninstLink, installerInTarget, installDir, "卸载 CKAN UI", "-Uninstall");
                }

                if (Directory.Exists(desktopDir))
                {
                    var desktopLink = Path.Combine(desktopDir, "CKAN UI.lnk");
                    ShortcutHelper.CreateShortcut(desktopLink, appExePath, targetVersionDir, "CKAN UI");
                }
            }

            // 10. 写入 HKCU 卸载注册表与 App Paths
            progressCallback?.Invoke(90, "正在写入 Windows 注册表卸载项与路径...");
            var uninstallCmd = $"\"{installerInTarget}\" -Uninstall";
            var quietUninstallCmd = $"\"{installerInTarget}\" -Uninstall -Quiet -Force";
            var estimatedSizeKb = (int)Math.Min(int.MaxValue, CalculateDirectorySizeKb(appRoot));

            RegistryHelper.RegisterUninstallEntry(
                version,
                installDir,
                appExePath,
                uninstallCmd,
                quietUninstallCmd,
                estimatedSizeKb);

            RegistryHelper.RegisterAppPath(mainExeName, appExePath, targetVersionDir);
            RegistryHelper.CleanStaleAppPaths(ExeCandidates, mainExeName, installDir);

            installedAppExe = appExePath;
            installedVersion = version;
            progressCallback?.Invoke(100, "安装完成！");

            // 11. 启动应用
            if (!options.NoLaunch && options.Quiet)
            {
                try
                {
                    Process.Start(new ProcessStartInfo(appExePath)
                    {
                        WorkingDirectory = targetVersionDir,
                        UseShellExecute = true
                    });
                }
                catch { }
            }

            return 0;
        }
        catch (Exception ex)
        {
            installedAppExe = string.Empty;
            installedVersion = string.Empty;
            Console.Error.WriteLine("安装过程中发生错误: " + ex.Message);
            foreach (var rollback in rollbackActions)
            {
                try { rollback(); } catch { }
            }
            throw;
        }
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

    private static object? ResolvePayloadSource(InstallerOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.SourceDir) && Directory.Exists(options.SourceDir))
        {
            return new DirectoryInfo(options.SourceDir);
        }

        var embeddedStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip");
        if (embeddedStream != null)
        {
            return embeddedStream;
        }

        var baseDir = AppContext.BaseDirectory;
        var localZip = Path.Combine(baseDir, "payload.zip");
        if (File.Exists(localZip))
        {
            return new FileInfo(localZip);
        }

        var localAppDir = Path.Combine(baseDir, "app");
        if (Directory.Exists(localAppDir))
        {
            return new DirectoryInfo(localAppDir);
        }

        return null;
    }

    private static void ExtractOrCopyPayload(object source, string targetDir)
    {
        Directory.CreateDirectory(targetDir);

        if (source is Stream stream)
        {
            using (stream)
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
            {
                archive.ExtractToDirectory(targetDir, true);
            }

            return;
        }

        if (source is FileInfo fileInfo)
        {
            ZipFile.ExtractToDirectory(fileInfo.FullName, targetDir, true);
            return;
        }

        if (source is DirectoryInfo dirInfo)
        {
            CopyDirectory(dirInfo.FullName, targetDir);
            return;
        }

        throw new InvalidOperationException("不支持的 Payload 源类型: " + source?.GetType().FullName);
    }

    private static void CopyDirectory(string sourceDir, string targetDir)
    {
        Directory.CreateDirectory(targetDir);

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var dest = Path.Combine(targetDir, Path.GetFileName(file));
            File.Copy(file, dest, true);
        }

        foreach (var subDir in Directory.GetDirectories(sourceDir))
        {
            var destSub = Path.Combine(targetDir, Path.GetFileName(subDir));
            CopyDirectory(subDir, destSub);
        }
    }

    private static (string ExeName, string Version) DetermineExeAndVersion(string directory)
    {
        var exeName = ExeCandidates.FirstOrDefault(c => File.Exists(Path.Combine(directory, c)))
                      ?? "CkanUI.exe";

        var exePath = Path.Combine(directory, exeName);
        var version = "0.1.0";

        if (File.Exists(exePath))
        {
            try
            {
                var fvi = FileVersionInfo.GetVersionInfo(exePath);
                if (!string.IsNullOrWhiteSpace(fvi.ProductVersion))
                {
                    var clean = fvi.ProductVersion.Split('+')[0].Trim();
                    if (!string.IsNullOrWhiteSpace(clean))
                    {
                        version = clean;
                    }
                }
                else if (!string.IsNullOrWhiteSpace(fvi.FileVersion))
                {
                    version = fvi.FileVersion.Trim();
                }
            }
            catch { }
        }

        return (exeName, version);
    }

    private static long CalculateDirectorySizeKb(string path)
    {
        if (!Directory.Exists(path)) return 0;

        try
        {
            var di = new DirectoryInfo(path);
            var bytes = di.EnumerateFiles("*", SearchOption.AllDirectories).Sum(fi => fi.Length);
            return bytes / 1024;
        }
        catch
        {
            return 0;
        }
    }
}
