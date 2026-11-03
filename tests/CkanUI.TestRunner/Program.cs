using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using NUnitLite;

// 进程内跑测试程序集：每个测试项目仍是普通 NUnit 类库，CI 用 dotnet test，
// 本地用本入口（沙箱里 VSTest testhost 无法打开父进程句柄，见 docs/ops/engineering-conventions.md）。
//
// 本宿主不引用测试项目，而是从 tests/<proj>/bin/<config>/net10.0/ 发现已构建的程序集，
// 因此在并行开发时各 agent 只需构建自己的测试项目，宿主保持稳定。

var repoRoot = FindRepoRoot();
var config = ArgValue(args, "--config") ?? "Debug";

// 过滤词里要扣掉配置名本身：以前只写死 Debug/Release，用自定义配置（如 -c Verify，
// 绕开被别的进程锁住的 Debug 输出）时 `Verify` 会被当成过滤词，结果一个程序集都匹配不上，
// 宿主报「没有可运行的测试程序集」。
var filters = args
    .Where(a => !a.StartsWith("-", StringComparison.Ordinal))
    .Where(a => !a.Equals(config, StringComparison.OrdinalIgnoreCase))
    .ToArray();

// 性能用例（[Category("Perf")]）默认不跑：它们测的是毫秒级帧预算，宿主机一有别的进程在抢 CPU 就会假红，
// 规格 22 篇 §测试纪律也写明「性能用例默认 CI 不跑」。需要跑时显式加 --perf。
var includePerf = args.Any(a => a.Equals("--perf", StringComparison.OrdinalIgnoreCase));

// Avalonia 的 XAML 加载器用 Assembly.Load（落到默认 ALC）找控件包，不经测试程序集自己的 ALC。
// 这里让默认 ALC 也按「当前正在跑的测试程序集目录」兜底，否则 ViewLocator 造视图时会
// FileNotFoundException: Could not load file or assembly 'Avalonia.Controls.ColorPicker'。
var currentTestDirectory = (string?)null;
var testLocalAssemblies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
AssemblyLoadContext.Default.Resolving += (ctx, name) =>
{
    var directory = currentTestDirectory;
    // 测试程序集自己的 ALC 已经加载过的程序集（含测试程序集本身）不再在默认 ALC 里再加载一份，
    // 否则同一个类型会有两份身份，断言 expected/actual 打印出来一模一样却不相等。
    if (directory is null || testLocalAssemblies.Contains(name.Name!))
    {
        return null;
    }

    var candidate = Path.Combine(directory, name.Name + ".dll");
    return File.Exists(candidate) ? ctx.LoadFromAssemblyPath(candidate) : null;
};

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
    PrependNativeSearchPath(directory);
    currentTestDirectory = directory;
    var context = new AssemblyLoadContext(project, isCollectible: false);
    testLocalAssemblies.Clear();
    testLocalAssemblies.Add(Path.GetFileNameWithoutExtension(path));
    context.Resolving += (ctx, name) =>
    {
        var candidate = Path.Combine(directory, name.Name + ".dll");
        if (!File.Exists(candidate))
        {
            return null;
        }

        testLocalAssemblies.Add(name.Name!);
        return ctx.LoadFromAssemblyPath(candidate);
    };
    context.ResolvingUnmanagedDll += (assembly, dllName) =>
    {
        var name = dllName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? dllName : dllName + ".dll";
        var candidate = Path.Combine(directory, name);
        if (File.Exists(candidate))
        {
            return NativeLibrary.Load(candidate);
        }

        var nativePath = Path.Combine(directory, "runtimes", NativeRid(), "native", name);
        if (File.Exists(nativePath))
        {
            return NativeLibrary.Load(nativePath);
        }

        return IntPtr.Zero;
    };

    var assembly = context.LoadFromAssemblyPath(path);
    var hostArgs = new List<string> { "--noresult", "--labels=Off" };
    // 单点复现用：UI_TEST_WHERE 覆盖默认过滤（例如 class == SomeFixture），
    // 便于绕开与本次无关但会拖垮整轮的无头用例。
    var explicitWhere = Environment.GetEnvironmentVariable("UI_TEST_WHERE");
    if (!string.IsNullOrWhiteSpace(explicitWhere))
    {
        hostArgs.Add("--where");
        hostArgs.Add(explicitWhere);
    }
    else if (!includePerf)
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

static string NativeRid()
{
    return RuntimeInformation.ProcessArchitecture switch
    {
        Architecture.X64 => "win-x64",
        Architecture.Arm64 => "win-arm64",
        Architecture.X86 => "win-x86",
        _ => "win-x64"
    };
}

// 测试程序集里的原生库（如 SkiaSharp 的 libSkiaSharp.dll）位于 runtimes/<rid>/native/ 下，
// 而 SkiaSharp 3.x 自己用 NativeLibrary.Load 直接加载、不走 AssemblyLoadContext.ResolvingUnmanagedDll，
// 默认搜索路径里只有宿主目录。把该目录前置到 PATH，避免「native libSkiaSharp 版本 88.1 不兼容」这类假红。
static void PrependNativeSearchPath(string testAssemblyDirectory)
{
    var nativeDirectory = Path.Combine(testAssemblyDirectory, "runtimes", NativeRid(), "native");
    if (!Directory.Exists(nativeDirectory))
    {
        return;
    }

    var current = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
    if (current.Split(Path.PathSeparator).Contains(nativeDirectory, StringComparer.OrdinalIgnoreCase))
    {
        return;
    }

    Environment.SetEnvironmentVariable("PATH", nativeDirectory + Path.PathSeparator + current);
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
