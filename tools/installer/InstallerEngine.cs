using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;

namespace CkanUI.Tools.Installer;

internal static class InstallerEngine
{
    private static readonly string[] ExeCandidates = ["CkanUI.exe", "CkanUI.App.exe"];

    public static int Run(InstallerOptions options)
    {
        Console.WriteLine("========================================");
        Console.WriteLine("        CKAN UI 原生安装程序            ");
        Console.WriteLine("========================================");

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

        Console.WriteLine("[1] 目标安装目录: " + installDir);

        // 2. 交互提示确认
        if (!options.Quiet)
        {
            var msg = "准备将 CKAN UI 安装到本地计算机。\n\n" +
                      "目标目录: " + installDir + "\n\n" +
                      "是否继续？";
            var result = NativeMethods.MessageBox(
                IntPtr.Zero,
                msg,
                "CKAN UI 安装向导",
                NativeMethods.MB_YESNO | NativeMethods.MB_ICONQUESTION);

            if (result != NativeMethods.IDYES)
            {
                Console.WriteLine("用户取消安装。");
                return 0;
            }
        }

        // 3. 检测同名进程
        Console.WriteLine("[2] 检测正在运行的进程...");
        var running = GetRunningProcesses();
        if (running.Count > 0)
        {
            foreach (var p in running)
            {
                Console.WriteLine($"    检测到进程在运行: {p.ProcessName} (PID {p.Id})");
            }

            if (!options.Force)
            {
                var tip = "检测到 CKAN UI 正在运行。请先关闭应用，再重新运行安装程序。";
                Console.Error.WriteLine("错误: " + tip);
                if (!options.Quiet)
                {
                    NativeMethods.MessageBox(
                        IntPtr.Zero,
                        tip,
                        "CKAN UI 安装错误",
                        NativeMethods.MB_OK | NativeMethods.MB_ICONERROR);
                }

                return 2;
            }

            Console.WriteLine("    注意：-Force 参数已指定，继续尝试安装（不主动强杀）。");
        }
        else
        {
            Console.WriteLine("    无正在运行的实例。");
        }

        // 4. 定位 Payload 源
        Console.WriteLine("[3] 定位并准备安装文件...");
        var payloadSource = ResolvePayloadSource(options);
        if (payloadSource == null)
        {
            var err = "未能找到有效的安装源文件（内嵌 payload.zip、-SourceDir 或本地安装包）。";
            Console.Error.WriteLine("错误: " + err);
            if (!options.Quiet)
            {
                NativeMethods.MessageBox(IntPtr.Zero, err, "安装失败", NativeMethods.MB_OK | NativeMethods.MB_ICONERROR);
            }

            return 1;
        }

        // 5. 准备 Staging 目录并解压/复制
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
                    Directory.Delete(stagingDir, true);
                }
            });

            Console.WriteLine("    解压/复制安装包内容至临时目录: " + stagingDir);
            ExtractOrCopyPayload(payloadSource, stagingDir);

            // 6. 确定主程序名与版本号
            var (mainExeName, version) = DetermineExeAndVersion(stagingDir);
            Console.WriteLine($"[4] 识别主程序: {mainExeName}, 版本: {version}");

            var targetVersionDir = Path.Combine(appRoot, version);
            var backupDir = string.Empty;

            if (Directory.Exists(targetVersionDir))
            {
                backupDir = Path.Combine(appRoot, $".backup-{version}-{DateTime.Now:yyyyMMddHHmmss}");
                Console.WriteLine("    版本目录已存在，临时备份至: " + backupDir);
                Directory.Move(targetVersionDir, backupDir);
                rollbackActions.Add(() =>
                {
                    if (Directory.Exists(backupDir) && !Directory.Exists(targetVersionDir))
                    {
                        Directory.Move(backupDir, targetVersionDir);
                    }
                });
            }

            // 7. 原子替换就位
            Console.WriteLine("    原子替换版本目录: " + targetVersionDir);
            Directory.Move(stagingDir, targetVersionDir);

            if (!string.IsNullOrEmpty(backupDir) && Directory.Exists(backupDir))
            {
                try
                {
                    Directory.Delete(backupDir, true);
                }
                catch
                {
                    // 备份删除失败不影响安装
                }
            }

            // 8. 写入 current 指针与 README
            Console.WriteLine("[5] 更新 current 指针与许可证...");
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
                    File.Copy(srcFile, Path.Combine(installDir, notice), true);
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
                    Console.WriteLine("    安装独立卸载器: " + installerInTarget);
                }
                catch
                {
                    // 忽略克隆自已的异常
                }
            }

            var appExePath = Path.Combine(targetVersionDir, mainExeName);

            // 10. 创建快捷方式
            if (!options.NoShortcuts)
            {
                Console.WriteLine("[6] 创建系统快捷方式...");
                var programsDir = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
                var desktopDir = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

                if (Directory.Exists(programsDir))
                {
                    var startMenuLink = Path.Combine(programsDir, "CKAN UI.lnk");
                    ShortcutHelper.CreateShortcut(startMenuLink, appExePath, targetVersionDir, "CKAN UI");
                    Console.WriteLine("    创建开始菜单快捷方式: " + startMenuLink);
                }

                if (Directory.Exists(desktopDir))
                {
                    var desktopLink = Path.Combine(desktopDir, "CKAN UI.lnk");
                    ShortcutHelper.CreateShortcut(desktopLink, appExePath, targetVersionDir, "CKAN UI");
                    Console.WriteLine("    创建桌面快捷方式: " + desktopLink);
                }
            }
            else
            {
                Console.WriteLine("[6] 跳过快捷方式创建 (-NoShortcuts)");
            }

            // 11. 写入 HKCU 卸载注册表与 App Paths
            Console.WriteLine("[7] 写入 HKCU 注册项与 App Paths...");
            var uninstallCmd = $"\"{installerInTarget}\" -Uninstall";
            var quietUninstallCmd = $"\"{installerInTarget}\" -Uninstall -Quiet -Force";
            var estimatedSizeKb = CalculateDirectorySizeKb(appRoot);

            RegistryHelper.RegisterUninstallEntry(
                version,
                installDir,
                appExePath,
                uninstallCmd,
                quietUninstallCmd,
                estimatedSizeKb);

            RegistryHelper.RegisterAppPath(mainExeName, appExePath, targetVersionDir);
            RegistryHelper.CleanStaleAppPaths(ExeCandidates, mainExeName, installDir);

            Console.WriteLine("========================================");
            Console.WriteLine("         CKAN UI 安装成功！             ");
            Console.WriteLine("========================================");
            Console.WriteLine("版本:     " + version);
            Console.WriteLine("程序:     " + appExePath);
            Console.WriteLine("安装目录: " + installDir);
            Console.WriteLine("卸载命令: " + uninstallCmd);

            // 12. 启动应用
            if (!options.NoLaunch)
            {
                if (!options.Quiet)
                {
                    var launchMsg = "CKAN UI 安装成功！\n\n是否立即启动 CKAN UI？";
                    var choice = NativeMethods.MessageBox(
                        IntPtr.Zero,
                        launchMsg,
                        "安装完成",
                        NativeMethods.MB_YESNO | NativeMethods.MB_ICONINFORMATION);

                    if (choice == NativeMethods.IDYES)
                    {
                        Process.Start(new ProcessStartInfo(appExePath)
                        {
                            WorkingDirectory = targetVersionDir,
                            UseShellExecute = true,
                        });
                    }
                }
                else
                {
                    Process.Start(new ProcessStartInfo(appExePath)
                    {
                        WorkingDirectory = targetVersionDir,
                        UseShellExecute = true,
                    });
                }
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("安装失败，正在回滚变更: " + ex.Message);
            for (var i = rollbackActions.Count - 1; i >= 0; i--)
            {
                try
                {
                    rollbackActions[i]();
                }
                catch
                {
                    // 忽略回滚异常
                }
            }

            if (!options.Quiet)
            {
                NativeMethods.MessageBox(
                    IntPtr.Zero,
                    "安装过程中发生错误，已回滚变更：\n\n" + ex.Message,
                    "CKAN UI 安装失败",
                    NativeMethods.MB_OK | NativeMethods.MB_ICONERROR);
            }

            return 3;
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

    private static PayloadSource? ResolvePayloadSource(InstallerOptions options)
    {
        // 1. 检查内嵌资源
        var asm = Assembly.GetExecutingAssembly();
        var manifestNames = asm.GetManifestResourceNames();
        if (manifestNames.Contains("payload.zip"))
        {
            Console.WriteLine("    使用内嵌安装包资源 (payload.zip)");
            return new PayloadSource(PayloadType.EmbeddedResource, "payload.zip");
        }

        // 2. 检查 -SourceDir
        if (!string.IsNullOrWhiteSpace(options.SourceDir) && Directory.Exists(options.SourceDir))
        {
            Console.WriteLine("    使用指定源目录: " + options.SourceDir);
            return new PayloadSource(PayloadType.Directory, Path.GetFullPath(options.SourceDir));
        }

        // 3. 检查当前运行目录下的包文件或 app 目录
        var currentBase = AppContext.BaseDirectory;
        var localZip = Path.Combine(currentBase, "payload.zip");
        if (File.Exists(localZip))
        {
            Console.WriteLine("    使用同目录 payload.zip: " + localZip);
            return new PayloadSource(PayloadType.ZipFile, localZip);
        }

        var localApp = Path.Combine(currentBase, "app");
        if (Directory.Exists(localApp))
        {
            Console.WriteLine("    使用同目录 app 目录: " + localApp);
            return new PayloadSource(PayloadType.Directory, localApp);
        }

        var zipMatches = Directory.GetFiles(currentBase, "ckan-ui-*.zip");
        if (zipMatches.Length > 0)
        {
            Console.WriteLine("    使用同目录便携包: " + zipMatches[0]);
            return new PayloadSource(PayloadType.ZipFile, zipMatches[0]);
        }

        return null;
    }

    private static void ExtractOrCopyPayload(PayloadSource source, string destDir)
    {
        Directory.CreateDirectory(destDir);

        switch (source.Type)
        {
            case PayloadType.EmbeddedResource:
            {
                var asm = Assembly.GetExecutingAssembly();
                using var stream = asm.GetManifestResourceStream(source.Path);
                if (stream == null)
                {
                    throw new InvalidOperationException("无法读取内嵌资源: " + source.Path);
                }

                using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
                archive.ExtractToDirectory(destDir, true);
                break;
            }

            case PayloadType.ZipFile:
            {
                using var stream = File.OpenRead(source.Path);
                using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
                archive.ExtractToDirectory(destDir, true);
                break;
            }

            case PayloadType.Directory:
            {
                CopyDirectoryRecursive(source.Path, destDir);
                break;
            }
        }
    }

    private static void CopyDirectoryRecursive(string fromDir, string toDir)
    {
        Directory.CreateDirectory(toDir);
        foreach (var dir in Directory.GetDirectories(fromDir, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(fromDir, dir);
            Directory.CreateDirectory(Path.Combine(toDir, rel));
        }

        foreach (var file in Directory.GetFiles(fromDir, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(fromDir, file);
            var target = Path.Combine(toDir, rel);
            var parent = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(parent) && !Directory.Exists(parent))
            {
                Directory.CreateDirectory(parent);
            }

            File.Copy(file, target, true);
        }
    }

    private static (string exeName, string version) DetermineExeAndVersion(string directory)
    {
        string? foundExe = null;
        foreach (var candidate in ExeCandidates)
        {
            var probe = Path.Combine(directory, candidate);
            if (File.Exists(probe))
            {
                foundExe = candidate;
                break;
            }
        }

        if (foundExe == null)
        {
            throw new FileNotFoundException("在安装内容中找不到主程序 (试过: " + string.Join(", ", ExeCandidates) + ")");
        }

        var exePath = Path.Combine(directory, foundExe);
        var version = "0.1.0";
        try
        {
            var info = FileVersionInfo.GetVersionInfo(exePath);
            if (!string.IsNullOrWhiteSpace(info.FileVersion))
            {
                version = info.FileVersion.Trim();
            }
        }
        catch
        {
            // 忽略文件信息提取错误
        }

        return (foundExe, version);
    }

    private static int CalculateDirectorySizeKb(string path)
    {
        if (!Directory.Exists(path))
        {
            return 0;
        }

        var sum = 0L;
        foreach (var file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
        {
            try
            {
                sum += new FileInfo(file).Length;
            }
            catch
            {
                // 忽略单个文件统计错误
            }
        }

        return (int)Math.Min(sum / 1024, int.MaxValue);
    }
}

internal enum PayloadType
{
    EmbeddedResource,
    ZipFile,
    Directory,
}

internal sealed record PayloadSource(PayloadType Type, string Path);
