namespace CkanUI.Services.Tests.ModConfig;

/// <summary>
/// fixtures 定位器：从测试程序集输出目录向上找 `ckan-ui.sln` 当作仓库根。
/// 为什么不复制到输出目录：测试项目 csproj 是共享文件（本任务禁改），
/// 而"向上找仓库根"已在本仓库别处（tests/CkanUI.Architecture.Tests/RepoRoot.cs）验证可行。
/// </summary>
internal static class ModConfigFixtures
{
    private static readonly Lazy<string> LazyRepoRoot = new(FindRepoRoot, LazyThreadSafetyMode.ExecutionAndPublication);

    private static readonly Lazy<string> LazyFixtureDirectory = new(
        () => Path.Combine(RepoRoot, "tests", "CkanUI.Services.Tests", "ModConfig", "Fixtures", "mod-config"),
        LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>仓库根目录。</summary>
    public static string RepoRoot => LazyRepoRoot.Value;

    /// <summary>cfg fixture 目录。</summary>
    public static string FixtureDirectory => LazyFixtureDirectory.Value;

    /// <summary>按文件名读原始字节（不经过任何文本解码，往返测试要比的就是这些字节）。</summary>
    public static byte[] ReadBytes(string fileName)
        => File.ReadAllBytes(Path.Combine(FixtureDirectory, fileName));

    /// <summary>按文件名读文本。</summary>
    public static string ReadText(string fileName)
        => File.ReadAllText(Path.Combine(FixtureDirectory, fileName));

    /// <summary>枚举全部 .cfg fixture（顺序稳定，失败信息可复现）。</summary>
    public static IReadOnlyList<string> EnumerateCfgFileNames()
    {
        var files = Directory.GetFiles(FixtureDirectory, "*.cfg");
        Array.Sort(files, StringComparer.Ordinal);
        var names = new List<string>(files.Length);
        foreach (var file in files)
        {
            names.Add(Path.GetFileName(file));
        }

        return names;
    }

    /// <summary>内建的 adapter 目录（仓库根 adapters/）。</summary>
    public static string BuiltinAdapterDirectory => Path.Combine(RepoRoot, "adapters");

    /// <summary>仓库内 adapter schema 的设计版路径（H-08 优先用它）。</summary>
    public static string AdapterSchemaPath => Path.Combine(RepoRoot, "docs", "design", "adapter.schema.json");

    /// <summary>造一个用完即删的临时目录。</summary>
    public static TempDirectory CreateTempDirectory(string label) => new(label);

    /// <summary>逐级向上找 ckan-ui.sln。</summary>
    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ckan-ui.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            "未找到仓库根：从 '" + AppContext.BaseDirectory + "' 向上找不到 ckan-ui.sln。");
    }
}

/// <summary>
/// 一次性临时目录：每个用例自己造 adapter 目录 / 游戏目录，避免依赖本机环境。
/// 为什么不复用 tests/CkanUI.TestSupport 的 TempWorkspace：那个类服务于 CKAN 类型夹具，
/// 而这里需要"造目录结构"的小工具，放在本任务自己的夹具文件里更好追溯。
/// </summary>
internal sealed class TempDirectory : IDisposable
{
    public TempDirectory(string label)
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "ckan-ui-modconfig-" + label + "-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path);
    }

    /// <summary>临时目录绝对路径（仅测试进程内使用，不进源码产物）。</summary>
    public string Path { get; }

    /// <summary>在临时目录下写文件（自动建父目录），返回绝对路径。</summary>
    public string File(string relativePath, string content)
    {
        var full = System.IO.Path.Combine(Path, relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));
        var parent = System.IO.Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(parent))
        {
            Directory.CreateDirectory(parent);
        }

        System.IO.File.WriteAllText(full, content);
        return full;
    }

    /// <summary>把 adapter.json 写进 &lt;临时目录&gt;/&lt;folder&gt;/adapter.json 并返回该路径。</summary>
    public string Adapter(string folder, string json) => File(folder + "/adapter.json", json);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
        catch (IOException)
        {
            // 临时目录清理失败不该让用例红。
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
