namespace CkanUI.Tools.VerifyLayout;

/// <summary>
/// 仓库根定位与 git 查询（21 篇 §12 的每条门禁都按"相对仓库根"判定）。
/// 只做只读查询：门禁不允许因为自身运行而改动工作树。
/// </summary>
internal sealed class Repo
{
    private readonly string? _gitRoot;

    private Repo(string root, string? gitRoot)
    {
        Root = root;
        _gitRoot = gitRoot;
    }

    /// <summary>判定用的仓库根（若 git 顶层目录有同名上级则取外层，见 <see cref="Discover"/>）。</summary>
    public string Root { get; }

    /// <summary>git 是否可用；不可用时 H-02/H-03 退化为目录扫描，并打印提示。</summary>
    public bool HasGit => _gitRoot is not null;

    /// <summary>
    /// 从当前工作目录向上找 <c>.git</c>。若找到的顶层目录的父目录还有同名目录（例如 ckan-ui 里套着
    /// 规格仓 CKAN 的 <c>doc/uir</c>），说明当前目录只是嵌套仓库，门禁应作用于外层工作树。
    /// 两种情况都返回外层，保证门禁在任意子目录调用时结论一致。
    /// </summary>
    /// <param name="startDirectory">起始目录；为空则用当前工作目录。</param>
    /// <param name="authoritative">
    /// true 表示调用方用 <c>--root</c> 显式钉死了仓库根：此时不再向上爬（自测/沙箱目录常常就嵌在真实仓库里），
    /// 只有该目录自己带 <c>.git</c> 才启用 git 查询，否则 H-02/H-03/H-07/H-10 退化为空集合。
    /// </param>
    public static Repo Discover(string? startDirectory = null, bool authoritative = false)
    {
        var directory = Path.GetFullPath(startDirectory ?? Environment.CurrentDirectory);
        if (authoritative)
        {
            var hasOwnGit = Directory.Exists(Path.Combine(directory, ".git")) ||
                            File.Exists(Path.Combine(directory, ".git"));
            return new Repo(directory, hasOwnGit ? directory : null);
        }

        string? gitRoot = null;
        for (var probe = directory; probe is not null; probe = Path.GetDirectoryName(probe))
        {
            if (Directory.Exists(Path.Combine(probe, ".git")) || File.Exists(Path.Combine(probe, ".git")))
            {
                gitRoot = probe;
                break;
            }
        }

        var root = gitRoot ?? directory;
        var parent = Path.GetDirectoryName(root);
        if (parent is not null && !string.Equals(Path.GetFileName(parent), Path.GetFileName(root), StringComparison.OrdinalIgnoreCase))
        {
            var outer = Path.Combine(parent, Path.GetFileName(root));
            if (Directory.Exists(outer) && Directory.Exists(Path.Combine(outer, ".git")))
            {
                root = outer;
            }
        }

        return new Repo(root, gitRoot);
    }

    /// <summary>把绝对路径转成相对仓库根的斜杠路径（门禁输出与 git 路径保持一致）。</summary>
    public string Relative(string absolutePath)
    {
        var full = Path.GetFullPath(absolutePath);
        var relative = Path.GetRelativePath(Root, full);
        return relative.Replace('\\', '/');
    }

    /// <summary>列出 git 已跟踪的文件（相对路径，斜杠分隔）。git 不可用时返回空集合。</summary>
    public HashSet<string> TrackedFiles()
    {
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (_gitRoot is null)
        {
            return files;
        }

        var (exitCode, output) = Git(_gitRoot, "ls-files -z");
        if (exitCode != 0)
        {
            return files;
        }

        foreach (var item in output.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            files.Add(item.Replace('\\', '/'));
        }

        return files;
    }

    /// <summary>列出未跟踪但未被忽略的文件（门禁需要提醒"新文件还没登记"，例如未入库的 docs）。</summary>
    public List<string> UntrackedFiles()
    {
        var files = new List<string>();
        if (_gitRoot is null)
        {
            return files;
        }

        var (exitCode, output) = Git(_gitRoot, "ls-files -z --others --exclude-standard");
        if (exitCode != 0)
        {
            return files;
        }

        foreach (var item in output.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            files.Add(item.Replace('\\', '/'));
        }

        return files;
    }

    /// <summary>
    /// 判断某文件是否走 Git LFS（.gitattributes 里 `filter=lfs`）。H-07 只对大文件报警，
    /// 走了 LFS 的文件本来就不该进普通对象库。git 不可用时一律返回 false。
    /// </summary>
    public bool IsLfsTracked(string relativePath)
    {
        if (_gitRoot is null)
        {
            return false;
        }

        var (exitCode, output) = Git(_gitRoot, $"check-attr --cached filter -- \"{relativePath}\"");
        return exitCode == 0 && output.Contains("filter: lfs", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>跑一条 git 命令并返回退出码与标准输出（不抛异常：git 缺失时门禁要能降级）。</summary>
    private static (int ExitCode, string Output) Git(string workingDirectory, string arguments)
    {
        var info = new System.Diagnostics.ProcessStartInfo("git", arguments)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        try
        {
            using var process = System.Diagnostics.Process.Start(info);
            if (process is null)
            {
                return (1, string.Empty);
            }

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return (process.ExitCode, output);
        }
        catch (Exception)
        {
            return (1, string.Empty);
        }
    }
}
