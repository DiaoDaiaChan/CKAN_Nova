using System.Globalization;
using System.Text;

namespace CkanUI.Tools.VerifyLayout;

/// <summary>运行模式（命令行里互斥，默认 --all）。</summary>
internal enum RunMode
{
    /// <summary>跑全部门禁（H-11 例外，只由 --prune 触发）。</summary>
    All,

    /// <summary>按 21 篇 §9 清理临时区。</summary>
    Prune,

    /// <summary>记录 tests/fixtures 的 sha256 清单。</summary>
    Record,

    /// <summary>只校验夹具清单。</summary>
    Verify,
}

/// <summary>命令行选项。</summary>
internal sealed class Options
{
    private static readonly string[] KnownGates =
        ["H-01", "H-02", "H-03", "H-04", "H-05", "H-06", "H-07", "H-08", "H-09", "H-10", "H-11", "H-12"];

    public RunMode Mode { get; private set; } = RunMode.All;

    public HashSet<string>? Only { get; private set; }

    public string? Root { get; private set; }

    public string Manifest { get; private set; } = H06FixtureHashes.DefaultManifest;

    public bool DryRun { get; private set; }

    public bool Verbose { get; private set; }

    /// <summary>警告级也算失败（<c>--strict</c>）：把 H-04/H-05/H-07 从"打印"升级为 exit 1。</summary>
    public bool Strict { get; private set; }

    public bool ShowHelp { get; private set; }

    public static Options Parse(IReadOnlyList<string> args)
    {
        var options = new Options();
        for (var index = 0; index < args.Count; index++)
        {
            var argument = args[index];
            switch (argument)
            {
                case "--all":
                    options.Mode = RunMode.All;
                    break;
                case "--prune":
                    options.Mode = RunMode.Prune;
                    break;
                case "--record":
                    options.Mode = RunMode.Record;
                    break;
                case "--verify":
                    options.Mode = RunMode.Verify;
                    break;
                case "--strict":
                    options.Strict = true;
                    break;
                case "--dry-run":
                    options.DryRun = true;
                    break;
                case "--verbose":
                case "-v":
                    options.Verbose = true;
                    break;
                case "-h":
                case "--help":
                    options.ShowHelp = true;
                    break;
                case "--only":
                    options.Only = ParseOnly(Next(args, ref index, argument));
                    break;
                case "--root":
                    options.Root = Next(args, ref index, argument);
                    break;
                case "--manifest":
                    options.Manifest = Next(args, ref index, argument);
                    break;
                default:
                    throw new ArgumentException($"未知参数: {argument}");
            }
        }

        return options;
    }

    private static HashSet<string> ParseOnly(string value)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var id = part.ToUpperInvariant();
            if (!KnownGates.Contains(id, StringComparer.Ordinal))
            {
                throw new ArgumentException($"未知门禁编号: {part}（可用 {string.Join('/', KnownGates)}）");
            }

            set.Add(id);
        }

        if (set.Count == 0)
        {
            throw new ArgumentException("--only 需要至少一个门禁编号");
        }

        return set;
    }

    private static string Next(IReadOnlyList<string> args, ref int index, string flag)
    {
        if (index + 1 >= args.Count)
        {
            throw new ArgumentException($"{flag} 缺少参数值");
        }

        return args[++index];
    }
}

/// <summary>
/// 卫生门禁入口（21 篇 §12，任务 ENV-04/ENV-05 配套）。
/// 退出码：0 = 通过（含只有警告），1 = 有阻断级违规，2 = 命令行用法错误。
/// </summary>
internal static class Program
{
    private const string Usage = """
        verify-layout —— CKAN UI 仓库卫生门禁（docs/plan/21-repo-layout.md §12）

        用法:
          dotnet run --project tools/verify-layout -- [模式] [选项]

        模式（互斥，默认 --all）:
          --all                跑全部门禁；H-11 只在 --prune 下执行
          --prune              按 21 篇 §9 清理临时区（artifacts 7 天 / spikes 14 天 / docs/_drafts 30 天）
          --record             记录 tests/fixtures 的 sha256 清单
          --verify             校验 tests/fixtures 与清单一致

        选项:
          --only H-01,H-04     只跑指定门禁（H-01..H-12）
          --root <路径>        显式指定仓库根（默认从当前目录向上找 .git）
          --manifest <路径>    H-06 清单路径（默认 tools/verify-layout/fixtures.sha256）
          --dry-run            --prune 只打印不删除
          --strict             警告级（H-04/H-05/H-07）也算失败，退出码 1
          -v, --verbose        打印每条门禁的统计说明
          -h, --help           显示本帮助

        分级（R-81）：H-01/H-02/H-03/H-09/H-10（任务书指定）+ H-06/H-08/H-12（客观违规）阻断；
                     H-04/H-05/H-07 只警告，加 --strict 才失败；H-11 独立模式（--prune）。
        """;

    private static int Main(string[] args)
    {
        Options options;
        try
        {
            options = Options.Parse(args);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine($"参数错误: {ex.Message}");
            Console.Error.WriteLine();
            Console.Error.WriteLine(Usage);
            return 2;
        }

        Console.OutputEncoding = Encoding.UTF8;
        if (options.ShowHelp)
        {
            Console.WriteLine(Usage);
            return 0;
        }

        var repo = Repo.Discover(options.Root, authoritative: options.Root is not null);
        var context = new GateContext(repo, options.Verbose);

        Console.WriteLine($"verify-layout · 仓库根 {repo.Root}");
        Console.WriteLine(repo.HasGit
            ? $"git 已跟踪 {repo.TrackedFiles().Count} 个文件"
            : "git 不可用：H-02/H-03/H-07/H-10 退化为空集合（请在仓库内运行）");
        Console.WriteLine();

        switch (options.Mode)
        {
            case RunMode.Record:
            {
                var count = H06FixtureHashes.Record(context, options.Manifest);
                Console.WriteLine($"PASS  [H-06] 夹具 sha256 清单已记录（{count} 个文件） → {options.Manifest}");
                return 0;
            }

            case RunMode.Verify:
                RunGate(context, H06FixtureHashes.Id, "夹具 sha256 一致", GateLevel.Blocking, () => H06FixtureHashes.Verify(context, options.Manifest));
                break;

            case RunMode.Prune:
                RunGate(context, H11PruneTemporaryAreas.Id, "临时区时效（--prune）", GateLevel.Warning, () => H11PruneTemporaryAreas.Run(context, options.DryRun));
                break;

            default:
                RunAll(context, options);
                break;
        }

        return Report(context, options);
    }

    private static void RunAll(GateContext context, Options options)
    {
        var gates = new (string Id, string Title, GateLevel Level, Action Run)[]
        {
            (H01RootWhitelist.Id, "根目录白名单", GateLevel.Blocking, () => H01RootWhitelist.Run(context)),
            (H02BuildArtifactsTracked.Id, "构建产物未被跟踪", GateLevel.Blocking, () => H02BuildArtifactsTracked.Run(context)),
            (H03ForbiddenFileTypes.Id, "禁止入库的文件类型", GateLevel.Blocking, () => H03ForbiddenFileTypes.Run(context)),
            (H04DocTemplates.Id, "文档模板与编号唯一", GateLevel.Warning, () => H04DocTemplates.Run(context)),
            (H05DocIndex.Id, "文档索引覆盖", GateLevel.Warning, () => H05DocIndex.Run(context)),
            (H06FixtureHashes.Id, "夹具 sha256 一致", GateLevel.Blocking, () => H06FixtureHashes.Verify(context, options.Manifest)),
            (H07LargeFiles.Id, "超大文件", GateLevel.Warning, () => H07LargeFiles.Run(context)),
            (H08AdapterSchema.Id, "adapter.json 过 schema", GateLevel.Blocking, () => H08AdapterSchema.Run(context)),
            (H09ThemeExecutables.Id, "主题不含可执行内容", GateLevel.Blocking, () => H09ThemeExecutables.Run(context)),
            (H10PersonalPaths.Id, "源码无个人绝对路径", GateLevel.Blocking, () => H10PersonalPaths.Run(context)),
            (H12DocLinks.Id, "文档相对链接可解析", GateLevel.Blocking, () => H12DocLinks.Run(context)),
        };

        foreach (var gate in gates)
        {
            if (options.Only is not null && !options.Only.Contains(gate.Id))
            {
                continue;
            }

            RunGate(context, gate.Id, gate.Title, gate.Level, gate.Run);
        }

        if (options.Only is null || options.Only.Contains(H11PruneTemporaryAreas.Id))
        {
            Console.WriteLine("SKIP  [H-11] 临时区时效（仅 --prune 模式执行）");
        }
    }

    private static void RunGate(GateContext context, string id, string title, GateLevel level, Action run)
    {
        var before = context.Findings.Count;
        try
        {
            run();
        }
        catch (Exception ex)
        {
            // 单条门禁自己炸掉不该吞掉其他门禁的结论：记成该级别的违规后继续跑。
            context.Add(id, level, $"门禁执行失败: {ex.GetType().Name}: {ex.Message}");
        }

        var newFindings = context.Findings.Skip(before).ToList();
        var label = level == GateLevel.Blocking ? "阻断" : "警告";
        if (newFindings.Count == 0)
        {
            Console.WriteLine($"PASS  [{id}] {title}（{label}）");
            return;
        }

        var status = level == GateLevel.Blocking ? "FAIL" : "WARN";
        Console.WriteLine($"{status}  [{id}] {title}（{label}） · {newFindings.Count} 项");
        foreach (var finding in newFindings.Take(20))
        {
            Console.WriteLine($"        - {finding.Message}");
        }

        if (newFindings.Count > 20)
        {
            Console.WriteLine($"        - … 另有 {newFindings.Count - 20} 项未显示");
        }
    }

    private static int Report(GateContext context, Options options)
    {
        var blocking = context.Findings.Count(finding => finding.Level == GateLevel.Blocking);
        var warnings = context.Findings.Count(finding => finding.Level == GateLevel.Warning);

        Console.WriteLine();
        Console.WriteLine($"汇总: 阻断级违规 {blocking} 项 · 警告 {warnings} 项");

        if (blocking > 0)
        {
            Console.WriteLine("结论: 红（阻断级门禁未通过，CI 会失败）");
            return 1;
        }

        // --strict 只对全量模式生效：--prune 的发现项天生是"已清理"的通知，不该因此失败。
        if (options.Mode == RunMode.All && options.Strict && warnings > 0)
        {
            Console.WriteLine("结论: 红（--strict：警告级违规也计为失败）");
            return 1;
        }

        if (options.Mode == RunMode.Prune)
        {
            Console.WriteLine("结论: 绿（临时区已按保留期处理）");
            return 0;
        }

        Console.WriteLine(warnings > 0 ? "结论: 绿（有警告，不阻断）" : "结论: 绿");
        return 0;
    }
}
