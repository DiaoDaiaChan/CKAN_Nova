using System.Reflection;
using System.Runtime.Loader;
using NUnitLite;

// 进程内跑测试程序集：每个测试项目仍是普通 NUnit 类库，CI 用 dotnet test，
// 本地用本入口（沙箱里 VSTest testhost 无法打开父进程句柄，见 docs/ops/engineering-conventions.md）。
//
// 本宿主不引用测试项目，而是从 tests/<proj>/bin/<config>/net10.0/ 发现已构建的程序集，
// 因此在并行开发时各 agent 只需构建自己的测试项目，宿主保持稳定。

var repoRoot = FindRepoRoot();
var config = ArgValue(args, "--config") ?? "Debug";

var filters = args
    .Where(a => !a.StartsWith("-", StringComparison.Ordinal))
    .Where(a => !a.Equals("Debug", StringComparison.OrdinalIgnoreCase)
                && !a.Equals("Release", StringComparison.OrdinalIgnoreCase))
    .ToArray();

// 性能用例（[Category("Perf")]）默认不跑：它们测的是毫秒级帧预算，宿主机一有别的进程在抢 CPU 就会假红，
// 规格 22 篇 §测试纪律也写明「性能用例默认 CI 不跑」。需要跑时显式加 --perf。
var includePerf = args.Any(a => a.Equals("--perf", StringComparison.OrdinalIgnoreCase));

var candidates = Discover(repoRoot, config);
var selected = filters.Length == 0
    ? candidates
    : candidates.Where(c => filters.Any(f => c.Project.Contains(f, StringComparison.OrdinalIgnoreCase))).ToArray();

if (selected.Length == 0)
{
    Console.WriteLine("没有可运行的测试程序集。");
    Console.WriteLine("已构建的：" + (candidates.Length == 0 ? "(无)" : string.Join(", ", candidates.Select(c => c.Project))));
    Console.WriteLine("用法：dotnet run --project tests/CkanUI.TestRunner [-- 过滤词 ...] [--config Release] [--perf]");
    return 2;
}

var exitCode = 0;
foreach (var (project, path) in selected)
{
    Console.WriteLine($"== {project} ==");
    var directory = Path.GetDirectoryName(path)!;
    var context = new AssemblyLoadContext(project, isCollectible: false);
    context.Resolving += (ctx, name) =>
    {
        var candidate = Path.Combine(directory, name.Name + ".dll");
        return File.Exists(candidate) ? ctx.LoadFromAssemblyPath(candidate) : null;
    };

    var assembly = context.LoadFromAssemblyPath(path);
    var hostArgs = new List<string> { "--noresult", "--labels=Off" };
    if (!includePerf)
    {
        hostArgs.Add("--where");
        hostArgs.Add("cat != Perf");
    }

    var result = new AutoRun(assembly).Execute(hostArgs.ToArray());
    if (result != 0)
    {
        exitCode = result;
    }
}

return exitCode;

static (string Project, string Path)[] Discover(string repoRoot, string config)
{
    var testsDir = Path.Combine(repoRoot, "tests");
    if (!Directory.Exists(testsDir))
    {
        return Array.Empty<(string, string)>();
    }

    return Directory.EnumerateDirectories(testsDir)
        .Select(dir => new { Project = Path.GetFileName(dir), Dir = dir })
        .Where(x => x.Project.EndsWith(".Tests", StringComparison.Ordinal))
        .Select(x => new
        {
            x.Project,
            Path = Path.Combine(x.Dir, "bin", config, "net10.0", x.Project + ".dll"),
        })
        .Where(x => File.Exists(x.Path))
        .OrderBy(x => x.Project, StringComparer.Ordinal)
        .Select(x => (x.Project, x.Path))
        .ToArray();
}

static string? ArgValue(string[] argv, string name)
{
    for (var i = 0; i < argv.Length - 1; i++)
    {
        if (argv[i].Equals(name, StringComparison.OrdinalIgnoreCase))
        {
            return argv[i + 1];
        }
    }

    return null;
}

static string FindRepoRoot()
{
    var current = AppContext.BaseDirectory;
    while (current is not null)
    {
        if (File.Exists(Path.Combine(current, "ckan-ui.sln")))
        {
            return current;
        }

        current = Path.GetDirectoryName(current.TrimEnd(Path.DirectorySeparatorChar));
    }

    throw new InvalidOperationException("找不到 ckan-ui.sln，请从仓库内的目录运行本测试宿主。");
}
