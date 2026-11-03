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

        // 4. 开工前先确认这个目录真的能写：不然会在解压到一半才炸（非管理员 / 只读目录 /
        //    安全软件锁目录都表现为 ERROR_ACCESS_DENIED，越早说清楚越好）。
        progressCallback?.Invoke(30, "检查安装目录权限...");
        InstallLocation.EnsureWritable(installDir);

        // 5. 准备 Staging 目录并解压/复制
        var appRoot = Path.Combine(installDir, "app");
        var currentDir = Path.Combine(installDir, "current");
        // 不带前导点：点目录在资源管理器里默认隐藏，用户报障时根本找不到；
        // 少数安全软件的目录规则也会把点开头的新建目录当可疑对象拦一刀。
        var stagingDir = Path.Combine(appRoot, "staging-" + Guid.NewGuid().ToString("N")[..8]);

        var rollbackActions = new List<Action>();
        try
        {
            InstallLocation.CreateDirectoryWithRetry(appRoot);
            rollbackActions.Add(() =>
            {
                if (Directory.Exists(stagingDir))
                {
                    try { Directory.Delete(stagingDir, true); } catch { }
                }
            });

            progressCallback?.Invoke(40, "正在解压并写入应用程序核心文件...");
            ExtractWithRetry(payloadSource, stagingDir);

            // 6. 确定主程序名与版本号
            var (mainExeName, version) = DetermineExeAndVersion(stagingDir);
            var targetVersionDir = Path.Combine(appRoot, version);
            var backupDir = string.Empty;

            if (Directory.Exists(targetVersionDir))
            {
                backupDir = Path.Combine(appRoot, $"backup-{version}-{DateTime.Now:yyyyMMddHHmmss}");
                Directory.Move(targetVersionDir, backupDir);
                rollbackActions.Add(() =>
                {
                    if (Directory.Exists(backupDir) && !Directory.Exists(targetVersionDir))
                    {
                        try { Directory.Move(backupDir, targetVersionDir); } catch { }
                    }
                });
            }

            // 7. 原子替换就位
            progressCallback?.Invoke(65, "正在更新版本目录与符号链接...");
            Directory.Move(stagingDir, targetVersionDir);

            if (!string.IsNullOrEmpty(backupDir) && Directory.Exists(backupDir))
            {
                try { Directory.Delete(backupDir, true); } catch { }
            }

            // 8. 写入 current 指针与说明
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

            // 9. 自克隆作为独立卸载器
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

            // 10. 创建快捷方式
            progressCallback?.Invoke(80, "正在注册桌面与开始菜单快捷方式...");
            var programsDir = string.IsNullOrWhiteSpace(options.ShortcutRoot)
                ? Environment.GetFolderPath(Environment.SpecialFolder.Programs)
                : Path.Combine(options.ShortcutRoot!, "StartMenu");
            var desktopDir = string.IsNullOrWhiteSpace(options.ShortcutRoot)
                ? Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
                : Path.Combine(options.ShortcutRoot!, "Desktop");

            if (!options.NoStartMenuShortcut)
            {
                var startMenuLink = Path.Combine(programsDir, "CKAN UI.lnk");
                if (ShortcutHelper.CreateShortcut(startMenuLink, appExePath, targetVersionDir, out var startMenuError, "CKAN UI"))
                {
                    rollbackActions.Add(() => ShortcutHelper.DeleteShortcut(startMenuLink));
                }
                else
                {
                    // 建失败不能静默：用户会以为「装好了」，却在开始菜单里搜不到。
                    options.Warnings.Add("开始菜单快捷方式创建失败（" + startMenuError + "）");
                    options.WarningDetails.Add("开始菜单: " + startMenuLink + " — " + startMenuError);
                }

                // 开始菜单增加独立卸载入口
                var uninstLink = Path.Combine(programsDir, "CKAN UI", "卸载 CKAN UI.lnk");
                if (ShortcutHelper.CreateShortcut(uninstLink, installerInTarget, installDir, out var uninstError, "卸载 CKAN UI", "-Uninstall"))
                {
                    rollbackActions.Add(() => ShortcutHelper.DeleteShortcut(uninstLink));
                }
                else
                {
                    options.Warnings.Add("卸载快捷方式创建失败（" + uninstError + "）");
                    options.WarningDetails.Add("卸载入口: " + uninstLink + " — " + uninstError);
                }
            }

            if (!options.NoDesktopShortcut)
            {
                var desktopLink = Path.Combine(desktopDir, "CKAN UI.lnk");
                if (ShortcutHelper.CreateShortcut(desktopLink, appExePath, targetVersionDir, out var desktopError, "CKAN UI"))
                {
                    rollbackActions.Add(() => ShortcutHelper.DeleteShortcut(desktopLink));
                }
                else
                {
                    options.Warnings.Add("桌面快捷方式创建失败（" + desktopError + "）");
                    options.WarningDetails.Add("桌面: " + desktopLink + " — " + desktopError);
                }
            }

            // 11. 写入 HKCU 卸载注册表与 App Paths
            progressCallback?.Invoke(90, "正在写入 Windows 注册表卸载项与路径...");
            var uninstallCmd = $"\"{installerInTarget}\" -Uninstall";
            var quietUninstallCmd = $"\"{installerInTarget}\" -Uninstall -Quiet -Force";
            var estimatedSizeKb = (int)Math.Min(int.MaxValue, CalculateDirectorySizeKb(appRoot));

            if (options.NoRegistry)
            {
                // 绿色安装：不碰注册表，开始菜单里的卸载入口就是唯一的卸载路径。
                progressCallback?.Invoke(92, "已跳过注册表（绿色安装）...");
            }
            else
            {
                try
                {
                    RegistryHelper.RegisterUninstallEntry(
                        version,
                        installDir,
                        appExePath,
                        uninstallCmd,
                        quietUninstallCmd,
                        estimatedSizeKb);

                    if (!options.NoAppPath)
                    {
                        RegistryHelper.RegisterAppPath(mainExeName, appExePath, targetVersionDir);
                    }
                }
                catch (Exception regEx) when (regEx is UnauthorizedAccessException
                                              or System.Security.SecurityException
                                              or IOException)
                {
                    // 文件都装好了，只差注册表：说清楚是哪一项、给两条可行路径，
                    // 不要让用户对着「拒绝访问」猜是不是磁盘/杀软的问题。
                    throw new InvalidOperationException(
                        "无法写入注册表卸载信息（HKEY_CURRENT_USER\\Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\CkanUI）："
                        + regEx.Message
                        + "。请以管理员身份重新运行安装程序；若仍失败，检查组策略或安全软件是否禁止写入 HKCU 注册表。",
                        regEx);
                }

                RegistryHelper.CleanStaleAppPaths(ExeCandidates, mainExeName, installDir);
            }

            installedAppExe = appExePath;
            installedVersion = version;
            progressCallback?.Invoke(100, "安装完成！");

            // 12. 启动应用
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
            foreach (var rollback in rollbackActions)
            {
                try { rollback(); } catch { }
            }

            // 只有「目录写不进去」才翻成「换目录 / 提权 / 放行」——EnsureWritable 和
            // ExtractWithRetry 抛出的异常已经带着具体路径。其它异常（比如注册表被组策略
            // 拒绝）保留原貌：把注册表问题也说成「安装目录不可写」等于给用户指错方向。
            var friendly = ex as InstallLocationException;

            if (friendly != null)
            {
                Console.Error.WriteLine("安装过程中发生错误: " + friendly.Message);
                throw friendly;
            }

            Console.Error.WriteLine("安装过程中发生错误: " + ex.Message);
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

    /// <summary>
    /// 解压/复制带三次退避：实时防护扫描刚创建的目录时，首个文件写入会瞬时被拒，
    /// 重试即可过；真无权限时三次都会失败并保留原始异常。
    /// </summary>
    private static void ExtractWithRetry(object source, string targetDir, int attempts = 3)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                ExtractOrCopyPayload(source, targetDir);
                return;
            }
            catch (Exception ex) when (attempt < attempts && (ex is UnauthorizedAccessException or IOException))
            {
                try { if (Directory.Exists(targetDir)) Directory.Delete(targetDir, true); } catch { }
                Thread.Sleep(150 * attempt);
            }
        }
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
