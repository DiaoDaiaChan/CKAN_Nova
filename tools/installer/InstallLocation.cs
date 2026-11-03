namespace CkanUI.Tools.Installer;

/// <summary>安装目录不可用（无写权限 / 被占用 / 路径非法）时抛出的错误，消息直接面向用户。</summary>
internal sealed class InstallLocationException : Exception
{
    public InstallLocationException(string directory, Exception inner)
        : base(BuildMessage(directory, inner), inner)
    {
        Directory = directory;
    }

    public string Directory { get; }

    private static string BuildMessage(string directory, Exception inner)
    {
        var reason = inner is UnauthorizedAccessException
            ? "没有写权限（拒绝访问）"
            : inner.Message;

        return "安装目录不可写：" + directory + "\r\n" +
               "原因：" + reason + "\r\n\r\n" +
               "请按顺序尝试：\r\n" +
               "  1) 返回「上一步」换一个你有写权限的目录（例如「文档」下的文件夹）；\r\n" +
               "  2) 右键安装程序 →「以管理员身份运行」；\r\n" +
               "  3) 安全软件/勒索防护临时锁定了该目录时，放行本程序后重试。";
    }
}

/// <summary>目录写入探测：把「装到一半才炸」变成「开工前就说清楚」。</summary>
internal static class InstallLocation
{
    /// <summary>
    /// 确保目录存在且当前进程真的能写（建目录 + 落一个 DeleteOnClose 探针文件）。
    /// 失败抛 <see cref="InstallLocationException"/>，消息里带整改建议。
    /// </summary>
    public static void EnsureWritable(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var probe = Path.Combine(directory, "ckanui-write-probe-" + Guid.NewGuid().ToString("N")[..8] + ".tmp");
            using var fs = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or NotSupportedException or ArgumentException)
        {
            throw new InstallLocationException(directory, ex);
        }
    }

    /// <summary>
    /// 建目录并带重试：刚创建的目录可能正被实时防护扫描，此时 CreateDirectory / 解压首个文件
    /// 会瞬时返回 ERROR_ACCESS_DENIED。三次退避能把这类假失败吃掉，真无权限仍会抛。
    /// </summary>
    public static void CreateDirectoryWithRetry(string directory, int attempts = 3)
    {
        if (Directory.Exists(directory))
        {
            return;
        }

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                Directory.CreateDirectory(directory);
                return;
            }
            catch (Exception ex) when (attempt < attempts && (ex is UnauthorizedAccessException or IOException))
            {
                Thread.Sleep(120 * attempt);
            }
        }
    }

    /// <summary>一次性的环境诊断，用于让用户把「装不上」变成可定位的证据。</summary>
    public static string Diagnose(string? requestedDir)
    {
        var sb = new System.Text.StringBuilder();
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var installDir = string.IsNullOrWhiteSpace(requestedDir)
            ? Path.Combine(localAppData, "CKAN UI")
            : requestedDir!;

        sb.AppendLine("CKAN UI 安装环境诊断");
        sb.AppendLine("  用户          : " + Environment.UserDomainName + "\\" + Environment.UserName);
        sb.AppendLine("  管理员        : " + IsElevated());
        sb.AppendLine("  进程位数      : " + (Environment.Is64BitProcess ? "x64" : "x86"));
        sb.AppendLine("  操作系统      : " + Environment.OSVersion.VersionString);
        sb.AppendLine("  LocalAppData  : " + localAppData);
        sb.AppendLine("  TEMP          : " + Path.GetTempPath());
        sb.AppendLine("  安装目录      : " + installDir);
        sb.AppendLine();
        sb.AppendLine("逐级写入探测（每级都是「建目录 + 落探针文件」）：");

        var probes = new List<string>();
        foreach (var part in new[] { Path.GetPathRoot(installDir), Path.GetDirectoryName(installDir), installDir })
        {
            if (!string.IsNullOrEmpty(part) && !probes.Contains(part, StringComparer.OrdinalIgnoreCase))
            {
                probes.Add(part);
            }
        }

        foreach (var probe in probes)
        {
            sb.AppendLine("  " + probe + "  ->  " + Probe(probe));
        }

        sb.AppendLine();
        sb.AppendLine("上一次安装痕迹：");
        var appRoot = Path.Combine(installDir, "app");
        if (Directory.Exists(appRoot))
        {
            foreach (var dir in Directory.GetDirectories(appRoot))
            {
                sb.AppendLine("  " + Path.GetFileName(dir) + "  (" + Directory.GetFileSystemEntries(dir).Length + " 项)");
            }
        }
        else
        {
            sb.AppendLine("  （无）");
        }

        return sb.ToString();
    }

    private static string Probe(string path)
    {
        try
        {
            if (!Directory.Exists(path))
            {
                return "不存在（父级不可写或尚未创建）";
            }

            var probe = Path.Combine(path, "ckanui-write-probe-" + Guid.NewGuid().ToString("N")[..8] + ".tmp");
            using (var fs = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose))
            {
            }

            return "可写";
        }
        catch (Exception ex)
        {
            return "不可写 · " + ex.GetType().Name + ": " + ex.Message;
        }
    }

    private static string IsElevated()
    {
        try
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            var principal = new System.Security.Principal.WindowsPrincipal(identity);
            return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator) ? "是" : "否";
        }
        catch
        {
            return "未知";
        }
    }
}
