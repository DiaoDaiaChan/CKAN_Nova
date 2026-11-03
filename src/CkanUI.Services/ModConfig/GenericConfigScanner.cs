using System.Text;
using CkanUI.Sfs;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.ModConfig;

namespace CkanUI.Services.ModConfig;

/// <summary>
/// 通用 ConfigNode 扫描器：在 <c>&lt;gameDir&gt;/GameData</c> 里找「像是某个 mod 自己的配置文件」的
/// <c>*.cfg</c>，不依赖任何 adapter 包。用于「Mod 配置」页在没有适配器时的兜底路径。
/// <para>
/// <b>性能权衡（PairCount / RootNode 的来源）</b>：扫描是纯静态、无状态的，一次可能过 2 万个文件。
/// KSP 生态里绝大多数 cfg 是原版定义文件（PART/RESOURCE/...），对它们做完整 ConfigNode 解析纯属浪费；
/// 因此这里先只读首 4 KB 做轻量探测（跳过注释/空行/花括号，取第一行有效内容当节点名，
/// 出现 <c>=</c> 就当扁平键值文件、根节点为 null），并用同样的 4 KB 判断 ModuleManager 补丁语法；
/// <b>只有最终命中的候选文件</b>才 <see cref="ConfigNodeDocument.ParseFile"/> 全量解析，从而拿到准确的
/// <see cref="GenericConfigFile.PairCount"/>、根节点与可写性（解析错误/只读属性）。
/// 这样 <see cref="GenericConfigFile.PairCount"/> 永远是真值而不是占位 0，代价是候选文件会被读两遍
/// （首 4 KB + 全文），候选集远小于全集，实测可接受。
/// </para>
/// </summary>
public static class GenericConfigScanner
{
    /// <summary><c>GameData</c> 目录名（KSP 固定）。</summary>
    public const string GameDataDirectoryName = "GameData";

    /// <summary>默认最多检查的 cfg 文件数，超过就记诊断并停止。</summary>
    public const int DefaultMaxFiles = 20000;

    /// <summary>默认最大递归深度（<c>GameData</c> 自身为 0）。</summary>
    public const int DefaultMaxDepth = 12;

    private const int HeadByteLength = 4096;
    private const int MaxDiagnostics = 64;

    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

    /// <summary>原版定义节点：出现在 mod 目录里也是原版内容（CKAN 安装的原版数据副本）。</summary>
    private static readonly HashSet<string> VanillaRootNodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "PART", "RESOURCE", "EXPERIMENT_DEFINITION", "PROP", "INTERNAL", "SCIENCE_DEFINITION",
        "ASTEROID", "CELESTIAL_BODY", "CONTRACT", "AGENT", "STRATEGY",
    };

    /// <summary>按文件名认候选：这些名字基本只有 mod 自己的配置会用。</summary>
    private static readonly HashSet<string> CandidateFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "settings.cfg", "config.cfg", "options.cfg", "user.cfg", "defaults.cfg",
    };

    /// <summary>按根节点认候选：插件的持久化/设置惯用节点。</summary>
    private static readonly HashSet<string> CandidateRootNodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "SETTINGS", "PARAMETERS", "CONFIG", "KSP_SETTINGS", "PERSISTENT",
    };

    /// <summary>
    /// 宽松模式也不收的内容定义节点：它们描述"东西是什么"（零件、合同、效果模板、本地化文本……），
    /// 不是"这个 mod 怎么运行"。把它们塞进通用配置页只会让用户以为能调。
    /// </summary>
    private static readonly HashSet<string> DefinitionRootNodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "PART", "RESOURCE", "EXPERIMENT_DEFINITION", "PROP", "INTERNAL", "SCIENCE_DEFINITION",
        "ASTEROID", "CELESTIAL_BODY", "CONTRACT", "CONTRACT_TYPE", "AGENT", "STRATEGY",
        "EFFECTTEMPLATE", "LOCALIZATION", "CATEGORY", "ORGANIZERSUBCATEGORY", "KERBALCHANGELOG",
        "KSP_TEXTURE_SET", "SHABBY_MATERIAL_DEF", "TEXTURE", "MATERIAL", "MODEL", "DEPENDENCY",
        "KSP_MODULEMANAGER", "PARTUPGRADE", "ROL_MODEL", "RPM_CUSTOM_VARIABLE", "RPM_SELECT_VARIABLE",
        "SUBCATEGORY", "SUBTYPE", "RESOURCE_DEFINITION", "CONTRACT_DEFINITION", "RPM_MATH_VARIABLE",
    };

    /// <summary>宽松模式仍然跳过的路径段：本地化文本与补丁目录不属于"可编辑配置"。</summary>
    private static readonly string[] SkippedPathSegments =
    [
        "/Localization/", "/Patches/", "/MMPatches/", "/ModuleManagerPatches/",
    ];

    /// <summary>原版目录：CKAN 安装的原版数据，不算 mod 配置。</summary>
    private static readonly HashSet<string> SkippedDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Squad", "SquadExpansion",
    };

    /// <summary>
    /// 扫描一个游戏目录下的候选 cfg。
    /// </summary>
    /// <param name="gameDir">游戏根目录（其下必须有 <c>GameData</c>）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <param name="maxFiles">最多检查的 cfg 文件数（测试用它压小，默认 <see cref="DefaultMaxFiles"/>）。</param>
    /// <param name="maxDepth">最大递归深度（测试用它压小，默认 <see cref="DefaultMaxDepth"/>）。</param>
    /// <returns>成功时带目录与命中列表；目录不存在/不可读时失败。除取消外不抛异常。</returns>
    public static OperationResult<GenericConfigCatalog> Scan(
        string gameDir,
        CancellationToken cancellationToken = default,
        int maxFiles = DefaultMaxFiles,
        int maxDepth = DefaultMaxDepth,
        GenericConfigScanOptions? options = null)
    {
        if (!TryNormalizeDirectory(gameDir, out var root, out var directoryError))
        {
            return OperationResult<GenericConfigCatalog>.Fail(directoryError);
        }

        var gameDataDirectory = Path.Combine(root, GameDataDirectoryName);
        if (!Directory.Exists(gameDataDirectory))
        {
            return OperationResult<GenericConfigCatalog>.Fail(
                $"游戏目录下没有 {GameDataDirectoryName} 目录：{gameDataDirectory}");
        }

        var state = new ScanState(
            gameDataDirectory,
            Math.Max(1, maxFiles),
            Math.Max(0, maxDepth),
            cancellationToken,
            options ?? GenericConfigScanOptions.Automatic);

        try
        {
            state.Visit(gameDataDirectory, 0);
        }
        catch (OperationCanceledException)
        {
            return OperationResult<GenericConfigCatalog>.Fail("扫描已取消。", detail: "cancelled");
        }
        catch (Exception ex) when (IsExpectedIo(ex))
        {
            return OperationResult<GenericConfigCatalog>.Fail(
                $"扫描 {GameDataDirectoryName} 失败：{ex.Message}",
                detail: ex.GetType().Name);
        }

        if (state.FileLimitReached)
        {
            state.AddDiagnostic($"cfg 文件数超过上限 {state.MaxFiles}，扫描提前停止；未检查到的文件没有列出。");
        }

        var files = state.Files
            .OrderBy(static file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return OperationResult<GenericConfigCatalog>.Ok(new GenericConfigCatalog(
            root,
            files,
            state.SkippedFileCount,
            state.Diagnostics.ToArray(),
            state.HiddenWideCandidateCount));
    }

    private static bool IsExpectedIo(Exception exception)
        => exception is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException
            or System.Security.SecurityException;

    private static bool TryNormalizeDirectory(string path, out string normalized, out string error)
    {
        normalized = string.Empty;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(path))
        {
            error = "游戏目录为空。";
            return false;
        }

        try
        {
            normalized = Path.GetFullPath(path.Trim());
        }
        catch (Exception ex) when (IsExpectedIo(ex))
        {
            error = $"游戏目录不合法：{path}（{ex.Message}）";
            return false;
        }

        normalized = normalized.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!Directory.Exists(normalized))
        {
            error = $"游戏目录不存在：{path}";
            return false;
        }

        return true;
    }

    /// <summary>一次扫描的可变状态（避免把 8 个参数传来传去）。</summary>
    private sealed class ScanState(
        string gameDataDirectory,
        int maxFiles,
        int maxDepth,
        CancellationToken cancellationToken,
        GenericConfigScanOptions options)
    {
        private int _examinedFiles;

        public List<GenericConfigFile> Files { get; } = [];

        public List<string> Diagnostics { get; } = [];

        public int SkippedFileCount { get; private set; }

        /// <summary>保守模式下被范围挡掉、但宽松模式会列出的文件数。</summary>
        public int HiddenWideCandidateCount { get; private set; }

        public bool FileLimitReached { get; private set; }

        public int MaxFiles => maxFiles;

        private bool IsWide => options.Scope == GenericConfigScanScope.Wide;

        public void AddDiagnostic(string message)
        {
            if (Diagnostics.Count >= MaxDiagnostics)
            {
                return;
            }

            Diagnostics.Add(message);
        }

        public void Visit(string directory, int depth)
        {
            if (FileLimitReached)
            {
                return;
            }

            cancellationToken.ThrowIfCancellationRequested();

            string[] files;
            try
            {
                files = Directory.GetFiles(directory, "*.cfg", SearchOption.TopDirectoryOnly);
            }
            catch (Exception ex) when (IsExpectedIo(ex))
            {
                AddDiagnostic($"无法列出目录 {ToRelative(directory)}：{ex.Message}");
                return;
            }

            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            foreach (var path in files)
            {
                if (!path.EndsWith(".cfg", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                _examinedFiles++;
                if (_examinedFiles > maxFiles)
                {
                    FileLimitReached = true;
                    return;
                }

                Consider(path);
            }

            if (depth >= maxDepth)
            {
                if (HasSubdirectory(directory))
                {
                    AddDiagnostic($"目录 {ToRelative(directory)} 已到递归深度上限 {maxDepth}，下级目录未扫描。");
                }

                return;
            }

            string[] directories;
            try
            {
                directories = Directory.GetDirectories(directory, "*", SearchOption.TopDirectoryOnly);
            }
            catch (Exception ex) when (IsExpectedIo(ex))
            {
                AddDiagnostic($"无法列出目录 {ToRelative(directory)}：{ex.Message}");
                return;
            }

            Array.Sort(directories, StringComparer.OrdinalIgnoreCase);
            foreach (var child in directories)
            {
                if (FileLimitReached)
                {
                    return;
                }

                if (SkippedDirectoryNames.Contains(Path.GetFileName(child)))
                {
                    continue;
                }

                Visit(child, depth + 1);
            }
        }

        private bool HasSubdirectory(string directory)
        {
            try
            {
                foreach (var _ in Directory.EnumerateDirectories(directory, "*", SearchOption.TopDirectoryOnly))
                {
                    return true;
                }
            }
            catch (Exception ex) when (IsExpectedIo(ex))
            {
                return false;
            }

            return false;
        }

        private void Consider(string path)
        {
            var fileName = Path.GetFileName(path);

            // 备份文件：mod 自己/我们自己的历史备份都不是可编辑配置。
            if (fileName.StartsWith(".bak", StringComparison.OrdinalIgnoreCase)
                || fileName.Contains(".bak-", StringComparison.OrdinalIgnoreCase))
            {
                SkippedFileCount++;
                return;
            }

            var relativePath = ToRelative(path);
            var head = ReadHead(path);
            if (head is null)
            {
                SkippedFileCount++;
                return;
            }

            var detectedRoot = DetectRootNode(head);
            var isPartUnderParts = relativePath.Contains("/Parts/", StringComparison.OrdinalIgnoreCase)
                && string.Equals(detectedRoot, "PART", StringComparison.OrdinalIgnoreCase);
            var isVanillaRoot = detectedRoot is not null && VanillaRootNodes.Contains(detectedRoot);
            if (isPartUnderParts || isVanillaRoot || LooksLikeModuleManagerPatch(head))
            {
                SkippedFileCount++;
                return;
            }

            var isCandidate = relativePath.Contains("/PluginData/", StringComparison.OrdinalIgnoreCase)
                || CandidateFileNames.Contains(fileName)
                || (detectedRoot is not null && CandidateRootNodes.Contains(detectedRoot));
            if (!isCandidate)
            {
                // 宽松模式：排除内容定义/patch/本地化之后，剩下的 cfg 都当"这个 mod 自己的配置"。
                // 代价是可能列出视觉/行为配置（TUFX、EVE 云层、Parallax 等）；它们确实可改，
                // 只是没有 adapter 的语义与风险说明，页面会明确说"这里只有键与值"。
                if (IsWideCandidate(relativePath, detectedRoot))
                {
                    if (!IsWide)
                    {
                        HiddenWideCandidateCount++;
                        SkippedFileCount++;
                        return;
                    }
                }
                else
                {
                    SkippedFileCount++;
                    return;
                }
            }

            FileInfo info;
            DateTime lastWriteUtc;
            long sizeBytes;
            bool isReadOnly;
            try
            {
                info = new FileInfo(path);
                lastWriteUtc = info.LastWriteTimeUtc;
                sizeBytes = info.Length;
                isReadOnly = (info.Attributes & FileAttributes.ReadOnly) != 0;
            }
            catch (Exception ex) when (IsExpectedIo(ex))
            {
                AddDiagnostic($"无法读取文件信息 {relativePath}：{ex.Message}");
                SkippedFileCount++;
                return;
            }

            ConfigNodeDocument? document = null;
            string? unreadableReason = null;
            try
            {
                document = ConfigNodeDocument.ParseFile(path);
            }
            catch (Exception ex) when (IsExpectedIo(ex) || ex is DecoderFallbackException)
            {
                unreadableReason = $"无法解析：{ex.Message}";
            }

            var rootNode = NormalizeRootNode(document?.RootNodeName ?? detectedRoot);

            // 首 4 KB 的探测可能被畸形首行骗过（现实里有文件第一行是 `P// 注释`）。
            // 宽松模式也不能把解析后确认是内容定义的文件列成"配置"。
            if (!isCandidate && rootNode is not null
                && (DefinitionRootNodes.Contains(rootNode) || IsPatchRoot(rootNode)))
            {
                SkippedFileCount++;
                return;
            }

            var notWritableReason = DetermineNotWritableReason(isReadOnly, unreadableReason, document);

            Files.Add(new GenericConfigFile(
                path,
                relativePath,
                ModNameOf(relativePath),
                DisplayNameOf(relativePath),
                rootNode,
                document?.Pairs.Count ?? 0,
                sizeBytes,
                lastWriteUtc,
                notWritableReason is null,
                notWritableReason));
        }

        /// <summary>ModuleManager 的节点操作符：解析后的根节点带这些前缀就是补丁，不是配置。</summary>
        private static bool IsPatchRoot(string root)
            => root.Length > 0 && root[0] is '@' or '+' or '-' or '!' or '$' or '%';

        /// <summary>宽松模式还愿意收的文件：不是内容定义、不在本地化/补丁目录里。</summary>
        private static bool IsWideCandidate(string relativePath, string? detectedRoot)
        {
            if (detectedRoot is not null && (DefinitionRootNodes.Contains(detectedRoot) || IsPatchRoot(detectedRoot)))
            {
                return false;
            }

            foreach (var segment in SkippedPathSegments)
            {
                if (relativePath.Contains(segment, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }

        private static string? DetermineNotWritableReason(
            bool isReadOnly,
            string? unreadableReason,
            ConfigNodeDocument? document)
        {
            if (isReadOnly)
            {
                return "文件带只读属性，写入前需要先去掉只读。";
            }

            if (unreadableReason is not null)
            {
                return unreadableReason;
            }

            if (document is null)
            {
                return "无法读取文件内容。";
            }

            if (document.HasParseErrors)
            {
                var first = document.ParseDiagnostics
                    .FirstOrDefault(static diagnostic => diagnostic.Severity == SfsSeverity.Error);
                return first is null
                    ? $"ConfigNode 解析有 {document.ParseErrorCount} 个错误，重写会破坏原有内容。"
                    : $"ConfigNode 解析有 {document.ParseErrorCount} 个错误（第 {first.Line} 行：{first.Message}），重写会破坏原有内容。";
            }

            if (document.IsTextLossy)
            {
                return "文件含非法 UTF-8 字节，重写会丢掉坏字节。";
            }

            return null;
        }

        private string? ReadHead(string path)
        {
            try
            {
                using var stream = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);

                var length = (int)Math.Min(stream.Length, HeadByteLength);
                var buffer = new byte[length];
                var read = 0;
                while (read < length)
                {
                    var current = stream.Read(buffer, read, length - read);
                    if (current <= 0)
                    {
                        break;
                    }

                    read += current;
                }

                var span = buffer.AsSpan(0, read);
                if (span.StartsWith(Utf8Bom))
                {
                    span = span[Utf8Bom.Length..];
                }

                return Encoding.UTF8.GetString(span);
            }
            catch (Exception ex) when (IsExpectedIo(ex))
            {
                AddDiagnostic($"跳过无法读取的 cfg：{ToRelative(path)}（{ex.Message}）");
                return null;
            }
        }

        private string ToRelative(string path)
            => Path.GetRelativePath(gameDataDirectory, path).Replace('\\', '/');

        private static string ModNameOf(string relativePath)
        {
            var slash = relativePath.IndexOf('/');
            return slash < 0 ? relativePath : relativePath[..slash];
        }

        private static string DisplayNameOf(string relativePath)
            => ModNameOf(relativePath) + " · " + relativePath;

        /// <summary>
        /// ModuleManager 允许给节点写 <c>:NEEDS[...]</c> 条件；根节点判定只看冒号前的名字。
        /// </summary>
        private static string? NormalizeRootNode(string? root)
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                return null;
            }

            var value = root.Trim();
            var colon = value.IndexOf(':');
            return colon > 0 ? value[..colon] : value;
        }

        /// <summary>
        /// 首 4 KB 里探测根节点：跳过空行与 <c>//</c> 注释，第一行有效内容就是节点名；
        /// 遇到带 <c>=</c> 的行说明是扁平键值文件，返回 null。
        /// </summary>
        private static string? DetectRootNode(string head)
        {
            foreach (var rawLine in head.Split('\n'))
            {
                var line = rawLine.TrimEnd('\r').Trim();
                if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal))
                {
                    continue;
                }

                if (line[0] is '{' or '}')
                {
                    continue;
                }

                if (line.Contains('='))
                {
                    return null;
                }

                // ConfigNode 允许把左花括号写在同一行（现实里 `PART {` / `SETTINGS{` 很常见）。
                // 只取开头的标识符，否则根节点会变成 "PART {"，后面的原版定义/零件判定全部失效。
                var tokenEnd = line.IndexOfAny([' ', '\t', '{']);
                return NormalizeRootNode(tokenEnd < 0 ? line : line[..tokenEnd]);
            }

            return null;
        }

        /// <summary>
        /// ModuleManager 补丁语法：行首（去空白后）出现 <c>@</c>、<c>+</c>、<c>!</c>、<c>$</c>、<c>%</c>。
        /// 命中即判为 patch 文件——它们描述的是"怎么改别人的配置"，不是可编辑的配置本身。
        /// </summary>
        private static bool LooksLikeModuleManagerPatch(string head)
        {
            foreach (var rawLine in head.Split('\n'))
            {
                var line = rawLine.AsSpan().TrimStart();
                if (line.IsEmpty)
                {
                    continue;
                }

                if (line[0] is '@' or '+' or '!' or '$' or '%')
                {
                    return true;
                }
            }

            return false;
        }
    }
}
