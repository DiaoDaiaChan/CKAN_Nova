using System.Globalization;
using System.Text.RegularExpressions;

namespace CkanUI.Tools.VerifyLayout;

/// <summary>H-01：仓库根只允许 21 篇 §4 白名单里的条目。</summary>
internal static class H01RootWhitelist
{
    public const string Id = "H-01";

    /// <summary>21 篇 §4 权威目录树的根层条目（任务书 task-4 已逐项列出，未登记即违规）。</summary>
    private static readonly HashSet<string> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        "ckan-ui.sln",
        "Directory.Build.props",
        "Directory.Build.targets",
        "Directory.Packages.props",
        "global.json",
        ".editorconfig",
        ".gitattributes",
        ".gitignore",
        "README.md",
        "CHANGELOG.md",
        "LICENSE.md",
        "external",
        "src",
        "tests",
        "adapters",
        "themes",
        "i18n",
        "docs",
        "tools",
        "spikes",
        "artifacts",
        ".github",
    };

    public static void Run(GateContext context)
    {
        var root = context.Repo.Root;
        var entries = Directory.EnumerateFileSystemEntries(root)
            .Select(Path.GetFileName)
            .Where(name => name is not null && !string.Equals(name, ".git", StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        foreach (var name in entries)
        {
            if (name is null || Allowed.Contains(name))
            {
                continue;
            }

            var hint = Directory.Exists(Path.Combine(root, name)) ? "/（目录）" : "（文件）";
            context.Add(Id, GateLevel.Blocking, $"根目录出现未登记条目: {name}{hint}");
        }

        context.Note(Id, $"根层条目 {entries.Count} 个，白名单 {Allowed.Count} 项");
    }
}

/// <summary>H-02：不得把 bin/obj/.vs/_build 提交进版本库。</summary>
internal static class H02BuildArtifactsTracked
{
    public const string Id = "H-02";

    private static readonly string[] ForbiddenDirectories = ["bin", "obj", ".vs", "_build"];

    public static void Run(GateContext context)
    {
        foreach (var path in context.Repo.TrackedFiles().OrderBy(p => p, StringComparer.Ordinal))
        {
            var segments = path.Split('/');
            foreach (var segment in segments)
            {
                if (ForbiddenDirectories.Contains(segment, StringComparer.OrdinalIgnoreCase))
                {
                    context.Add(Id, GateLevel.Blocking, $"构建产物被提交: {path}");
                    break;
                }
            }
        }
    }
}

/// <summary>H-03：禁止跟踪日志、dump、压缩包、临时文件、二进制；&gt;256KB 的 .sfs 也不许入库。</summary>
internal static class H03ForbiddenFileTypes
{
    public const string Id = "H-03";

    private const long SfsLimitBytes = 256 * 1024;

    private static readonly string[] ForbiddenExtensions = [".log", ".dmp", ".zip", ".tmp", ".dll"];

    public static void Run(GateContext context)
    {
        foreach (var path in context.Repo.TrackedFiles().OrderBy(p => p, StringComparer.Ordinal))
        {
            var extension = Path.GetExtension(path).ToLowerInvariant();
            if (ForbiddenExtensions.Contains(extension, StringComparer.Ordinal))
            {
                context.Add(Id, GateLevel.Blocking, $"禁止入库的文件类型: {path}");
                continue;
            }

            if (extension == ".sfs")
            {
                var size = SizeOf(context.Repo, path);
                if (size > SfsLimitBytes)
                {
                    context.Add(
                        Id,
                        GateLevel.Blocking,
                        $"禁止入库的文件类型: {path}（.sfs {Format(size)}，上限 256 KB，改用 gen-fixtures 生成）");
                }
            }
        }
    }

    private static long SizeOf(Repo repo, string relativePath)
    {
        var full = Path.Combine(repo.Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(full) ? new FileInfo(full).Length : 0;
    }

    private static string Format(long bytes) => (bytes / 1024.0).ToString("0.#", CultureInfo.InvariantCulture) + " KB";
}

/// <summary>H-07：单个文件 &gt;256 KB 且非 LFS、非生成物 → 警告（21 篇 §12 分级：不阻断）。</summary>
internal static class H07LargeFiles
{
    public const string Id = "H-07";

    private const long LimitBytes = 256 * 1024;

    /// <summary>生成物：由脚本产出、不入库证据链，允许超过阈值。</summary>
    private static readonly string[] GeneratedPrefixes =
    [
        "artifacts/", "spikes/", "docs/research/i18n-evidence/",
    ];

    public static void Run(GateContext context)
    {
        foreach (var path in context.Repo.TrackedFiles().OrderBy(p => p, StringComparer.Ordinal))
        {
            if (GeneratedPrefixes.Any(prefix => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            if (path.EndsWith(".min.js", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".min.css", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var full = Path.Combine(context.Repo.Root, path.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(full))
            {
                continue;
            }

            var size = new FileInfo(full).Length;
            if (size <= LimitBytes)
            {
                continue;
            }

            if (context.Repo.IsLfsTracked(path))
            {
                continue;
            }

            context.Add(
                Id,
                GateLevel.Warning,
                $"超大文件: {path} ({(size / 1024.0).ToString("0.#", CultureInfo.InvariantCulture)} KB)，请走 LFS 或改生成");
        }
    }
}

/// <summary>H-09：themes/ 只能是声明式资源，不得夹带可执行内容（20 篇 §12）。</summary>
internal static class H09ThemeExecutables
{
    public const string Id = "H-09";

    private static readonly string[] ForbiddenExtensions = [".axaml", ".js", ".dll"];

    public static void Run(GateContext context)
    {
        foreach (var path in context.EnumerateWorkingTreeFiles("themes").OrderBy(p => p, StringComparer.Ordinal))
        {
            var extension = Path.GetExtension(path).ToLowerInvariant();
            if (ForbiddenExtensions.Contains(extension, StringComparer.Ordinal))
            {
                context.Add(Id, GateLevel.Blocking, $"主题包含可执行内容: {path}");
            }
        }
    }
}

/// <summary>H-10：源码里不得出现个人绝对路径（21 篇 §12 的反向检查，吃过亏的一条）。</summary>
internal static class H10PersonalPaths
{
    public const string Id = "H-10";

    /// <summary>
    /// 模式用片段拼装：门禁自己的源码不能包含被匹配的字面量，否则扫描自己就永远红
    /// （21 篇 §12 对 H-10 的特别要求）。
    /// </summary>
    private static readonly string[] Patterns =
    [
        "[A-Z]:" + new string('\\', 2) + "Users" + new string('\\', 2),
        "Kerbal Space Program" + "_newmod",
    ];

    private static readonly string[] SourceExtensions =
    [
        ".cs", ".axaml", ".xaml", ".csproj", ".props", ".targets", ".json", ".yml", ".yaml",
        ".ps1", ".sh", ".cmd", ".bat", ".py",
    ];

    private const long MaxScanBytes = 2 * 1024 * 1024;

    public static void Run(GateContext context)
    {
        var regexes = Patterns.Select(pattern => new Regex(pattern, RegexOptions.CultureInvariant)).ToList();
        var scanned = 0;

        foreach (var path in context.Repo.TrackedFiles().OrderBy(p => p, StringComparer.Ordinal))
        {
            if (Noise.IsNoise(path))
            {
                continue;
            }

            var extension = Path.GetExtension(path).ToLowerInvariant();
            if (!SourceExtensions.Contains(extension, StringComparer.Ordinal))
            {
                continue;
            }

            var full = Path.Combine(context.Repo.Root, path.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(full))
            {
                continue;
            }

            var info = new FileInfo(full);
            if (info.Length > MaxScanBytes)
            {
                continue;
            }

            scanned++;
            var lineNumber = 0;
            foreach (var line in File.ReadLines(full))
            {
                lineNumber++;
                foreach (var regex in regexes)
                {
                    if (regex.IsMatch(line))
                    {
                        context.Add(Id, GateLevel.Blocking, $"疑似本机路径: {path}:{lineNumber}");
                        break;
                    }
                }
            }
        }

        context.Note(Id, $"扫描源码文件 {scanned} 个");
    }
}
