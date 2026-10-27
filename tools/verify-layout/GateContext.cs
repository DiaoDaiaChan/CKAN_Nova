namespace CkanUI.Tools.VerifyLayout;

/// <summary>门禁级别：阻断（退出码 1）或警告（打印但不失败，21 篇 §12 分级）。</summary>
internal enum GateLevel
{
    /// <summary>阻断：CI 红。</summary>
    Blocking,

    /// <summary>警告：只打印。</summary>
    Warning,
}

/// <summary>单条门禁的判定结果（一条消息 = 一个违规项）。</summary>
internal sealed record Finding(string GateId, GateLevel Level, string Message);

/// <summary>门禁上下文：所有门禁共享的路径集合与输出收集器。</summary>
internal sealed class GateContext
{
    private readonly List<Finding> _findings = [];

    public GateContext(Repo repo, bool verbose)
    {
        Repo = repo;
        Verbose = verbose;
    }

    public Repo Repo { get; }

    /// <summary>是否打印通过/跳过说明（<c>--verbose</c>）。</summary>
    public bool Verbose { get; }

    public IReadOnlyList<Finding> Findings => _findings;

    /// <summary>记录一个违规项。</summary>
    public void Add(string gateId, GateLevel level, string message)
    {
        _findings.Add(new Finding(gateId, level, message));
    }

    /// <summary>仅在 <c>--verbose</c> 下打印非违规的说明行。</summary>
    public void Note(string gateId, string message)
    {
        if (Verbose)
        {
            Console.WriteLine($"  · {gateId} {message}");
        }
    }

    /// <summary>把绝对路径枚举成相对路径（工作树里的真实文件，跳过 bin/obj/.git 等噪声目录）。</summary>
    public IEnumerable<string> EnumerateWorkingTreeFiles(string relativeDirectory)
    {
        var absolute = Path.Combine(Repo.Root, relativeDirectory);
        if (!Directory.Exists(absolute))
        {
            yield break;
        }

        foreach (var file in Directory.EnumerateFiles(absolute, "*", SearchOption.AllDirectories))
        {
            var relative = Repo.Relative(file);
            if (Noise.IsNoise(relative))
            {
                continue;
            }

            yield return relative;
        }
    }
}

/// <summary>噪声路径判定：构建产物、版本库内部、被 .gitignore 忽略的临时区一律不参与内容门禁。</summary>
internal static class Noise
{
    private static readonly string[] Prefixes =
    [
        ".git/", "bin/", "obj/", "_build/", ".vs/", "artifacts/", "spikes/", "docs/_drafts/", "_scratch/", "external/",
    ];

    private static readonly string[] Segments = ["/bin/", "/obj/", "/_build/", "/.vs/", "/node_modules/"];

    public static bool IsNoise(string relativePath)
    {
        var path = relativePath.Replace('\\', '/');
        foreach (var prefix in Prefixes)
        {
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        var probe = "/" + path;
        foreach (var segment in Segments)
        {
            if (probe.Contains(segment, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
