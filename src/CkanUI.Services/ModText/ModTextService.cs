using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Encodings.Web;
using System.Threading;
using System.Threading.Tasks;
using CkanUI.ModText;
using CkanUI.Services.Abstractions;
using log4net;
using UI18n.Core.Dict;
using UI18n.Core.Hashing;
using UI18n.Core.Text;

namespace CkanUI.Services.ModText;

/// <summary>
/// Mod 元数据翻译管理服务实现（17 篇 §3.4、§5）。
/// 扫描 %LocalAppData%/CKAN/ckan-ui/i18n/packs/<lang>/*.jsonl 与 overrides.jsonl，
/// 组装 DictPack，管理文件落盘、人工覆盖增删改与热重载。
/// </summary>
public sealed class ModTextService : IModTextService, IDisposable
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(ModTextService));

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly string _baseDirectory;
    private readonly object _lock = new();
    private IModTextProvider _provider;
    private DictPack _currentPack;
    private DictPack _currentOverrides;
    private ModTextPolicy _policy;
    private ModTextBlocklist _blocklist = ModTextBlocklist.Empty;
    private string _currentLanguage = "zh-cn";
    private int _totalEntries;

    public ModTextService(string? customBaseDirectory = null, ModTextPolicy policy = ModTextPolicy.ExistingOnly)
    {
        _baseDirectory = customBaseDirectory ?? GetDefaultStorageDirectory();
        _policy = policy;

        // 构造期只建空包，不碰磁盘：VM 构造不能有 IO，首帧要能立刻画出来（17 篇 §7）。
        _currentPack = new DictPack();
        _currentOverrides = new DictPack();
        _provider = new ModTextProvider(_currentOverrides, _currentPack, _policy, _blocklist);
    }

    public IModTextProvider Provider => _provider;
    public string CurrentLanguage => _currentLanguage;
    public int TotalEntries => _totalEntries;
    public string BaseDirectory => _baseDirectory;

    /// <summary>本地 packs 目录（内含 &lt;lang&gt;/*.jsonl 与 overrides.jsonl）。</summary>
    public string PacksRoot => Path.Combine(_baseDirectory, "packs");

    /// <summary>黑名单文件路径（17 篇 MTI-07）。</summary>
    public string BlocklistPath => Path.Combine(_baseDirectory, "blocklist.json");

    /// <summary>
    /// 译文使用策略（17 篇 MTI-08 三档）。改完立即生效，无需重载包。
    /// </summary>
    /// <remarks>
    /// 没放进 <see cref="IModTextService"/>：加接口成员会波及 ViewModel 里的空服务替身与他人测试假服务，
    /// 而设置页目前还没有这三档的入口，先留在实现类型上，等接线的任务再抬到接口。
    /// </remarks>
    public ModTextPolicy Policy
    {
        get => _policy;
        set
        {
            _policy = value;
            if (_provider is ModTextProvider provider)
            {
                provider.Policy = value;
            }
        }
    }

    /// <summary>当前黑名单（改完立即生效）。</summary>
    public ModTextBlocklist Blocklist
    {
        get => _blocklist;
        set
        {
            _blocklist = value ?? ModTextBlocklist.Empty;
            if (_provider is ModTextProvider provider)
            {
                provider.Blocklist = _blocklist;
            }
        }
    }

    public event Action? PackChanged;

    /// <summary>
    /// 按更新清单校验已安装的 packs（离线导入后自检 / 更新后复核，17 篇 MTI-06）。
    /// 清单缺失或校验失败都返回原因码，不抛异常。
    /// </summary>
    /// <param name="manifestJson">manifest.json 文本。</param>
    public ModTextPackVerification VerifyInstalledPacks(string? manifestJson)
    {
        if (!ModTextPackVerifier.TryReadManifest(manifestJson, out var manifest, out var error))
        {
            return ModTextPackVerification.Failed(error);
        }

        return ModTextPackVerifier.VerifyPacksDirectory(PacksRoot, manifest);
    }

    public static string GetDefaultStorageDirectory()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "CKAN", "ckan-ui", "i18n");
    }

    public Task ReloadPacksAsync(string? language = null, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            string lang = string.IsNullOrWhiteSpace(language) ? _currentLanguage : language.Trim().ToLowerInvariant();
            _currentLanguage = lang;

            var packs = new DictPack();
            var overrides = new DictPack();
            var blocklist = ModTextBlocklist.Empty;

            try
            {
                // 1. 定位语言目录：PackLocator 会按 zh-cn → zh 逐级回退，并容忍目录名大小写不一致。
                //    旧实现直接 Path.Combine(packs, lang)，遇到 zh-CN 这种目录名会整包漏载、界面直接全英文。
                var resolution = PackLocator.Resolve(PacksRoot, lang);
                if (resolution.Status == PackDirStatus.Found && !string.IsNullOrEmpty(resolution.Directory))
                {
                    var files = resolution.Files ?? [];
                    Array.Sort(files, StringComparer.Ordinal);
                    foreach (var file in files)
                    {
                        LoadJsonlFile(packs, file);
                    }
                }
                else
                {
                    // 缺包不是错误：降级为英文原文，首帧照常绘制（17 篇 §7「不阻塞首帧」）。
                    Log.Info($"[ModTextService] 语言 '{lang}' 没有本地语言包（{resolution.Status}），元数据译文降级为英文。");
                }

                // 2. 人工覆盖层单独成包：优先级由结构保证，不与几千条语言包混在一起。
                string overrideFile = Path.Combine(PacksRoot, "overrides.jsonl");
                if (File.Exists(overrideFile))
                {
                    LoadJsonlFile(overrides, overrideFile);
                }

                // 3. 黑名单（MTI-07）：文件缺失或解析失败一律按空名单处理，绝不因名单问题丢译文。
                blocklist = ModTextBlocklist.LoadFile(BlocklistPath);
                if (blocklist.Count == 0)
                {
                    blocklist = ModTextBlocklist.LoadFile(Path.Combine(PacksRoot, "blocklist.json"));
                }

                lock (_lock)
                {
                    _currentPack = packs;
                    _currentOverrides = overrides;
                    _blocklist = blocklist;
                    _totalEntries = packs.Entries + overrides.Entries;
                    _provider = new ModTextProvider(overrides, packs, _policy, blocklist);
                }
                Log.Info($"[ModTextService] 载入语言包 '{lang}' 完成，共 {_totalEntries} 条条目（语言包 {packs.Entries} + 覆盖 {overrides.Entries}），黑名单 {blocklist.Count} 条。");
            }
            catch (Exception ex)
            {
                Log.Warn($"[ModTextService] 载入语言包异常，降级为空词典: {ex.Message}");
                lock (_lock)
                {
                    _currentPack = new DictPack();
                    _currentOverrides = new DictPack();
                    _provider = new ModTextProvider(_currentOverrides, _currentPack, _policy, _blocklist);
                    _totalEntries = 0;
                }
            }

            PackChanged?.Invoke();
        }, ct);
    }

    public IReadOnlyList<string> GetAvailableLanguages()
    {
        var list = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "zh-cn" };
        string packsDir = Path.Combine(_baseDirectory, "packs");
        if (Directory.Exists(packsDir))
        {
            foreach (var dir in Directory.GetDirectories(packsDir))
            {
                string name = Path.GetFileName(dir);
                if (!string.Equals(name, "journal", StringComparison.OrdinalIgnoreCase))
                {
                    list.Add(name);
                }
            }
        }
        return list.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public IReadOnlyList<ModPackFileInfo> GetPackFiles(string? language = null)
    {
        string lang = string.IsNullOrWhiteSpace(language) ? _currentLanguage : language.Trim().ToLowerInvariant();
        var result = new List<ModPackFileInfo>();

        // 目录名不一定是 lang 原样小写（zh-CN / zh-cn / zh），统一交给 PackLocator 决定。
        var resolution = PackLocator.Resolve(PacksRoot, lang);
        string langDir = resolution.Directory ?? string.Empty;
        string dirName = string.IsNullOrEmpty(langDir) ? lang : Path.GetFileName(langDir);
        if (!string.IsNullOrEmpty(langDir) && Directory.Exists(langDir))
        {
            foreach (var file in Directory.GetFiles(langDir, "*.jsonl", SearchOption.TopDirectoryOnly))
            {
                var fi = new FileInfo(file);
                int count = CountLines(file);
                result.Add(new ModPackFileInfo(Path.Combine("packs", dirName, fi.Name), fi.FullName, fi.Length, count, fi.LastWriteTimeUtc));
            }
        }

        string overrideFile = Path.Combine(PacksRoot, "overrides.jsonl");
        if (File.Exists(overrideFile))
        {
            var fi = new FileInfo(overrideFile);
            int count = CountLines(overrideFile);
            result.Add(new ModPackFileInfo(Path.Combine("packs", "overrides.jsonl"), fi.FullName, fi.Length, count, fi.LastWriteTimeUtc));
        }

        return result;
    }

    public IReadOnlyList<ModTextEntryItem> GetAllEntries(string? filter = null)
    {
        DictPack packs;
        DictPack overrides;
        lock (_lock)
        {
            packs = _currentPack;
            overrides = _currentOverrides;
        }

        // 覆盖层排在前面：列表里先看到"我改过的条目"，与查找优先级一致。
        var all = new List<DictEntry>(overrides.AllEntries.Count + packs.AllEntries.Count);
        all.AddRange(overrides.AllEntries);
        all.AddRange(packs.AllEntries);

        var items = new List<ModTextEntryItem>(all.Count);
        string? q = string.IsNullOrWhiteSpace(filter) ? null : filter.Trim();

        foreach (var entry in all)
        {
            if (q != null)
            {
                bool matches = (entry.Source != null && entry.Source.Contains(q, StringComparison.OrdinalIgnoreCase)) ||
                                (entry.Translation != null && entry.Translation.Contains(q, StringComparison.OrdinalIgnoreCase)) ||
                                (entry.Context != null && entry.Context.Contains(q, StringComparison.OrdinalIgnoreCase)) ||
                                (entry.Normalized != null && entry.Normalized.Contains(q, StringComparison.OrdinalIgnoreCase));
                if (!matches) continue;
            }

            items.Add(new ModTextEntryItem(
                entry.Hash,
                entry.Normalized,
                entry.Source ?? string.Empty,
                entry.Translation ?? string.Empty,
                entry.Context,
                entry.Mod,
                entry.Src,
                entry.IsKeep));
        }

        return items;
    }

    public async Task PutOverrideAsync(string identifier, string field, string sourceText, string translation, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceText);

        string packsDir = Path.Combine(_baseDirectory, "packs");
        Directory.CreateDirectory(packsDir);
        string overrideFile = Path.Combine(packsDir, "overrides.jsonl");

        string normalized = TextNormalizer.Normalize(sourceText);
        ulong hash = Fnv1a.Hash(normalized);
        string n = $"ckan:{identifier}:{field}";

        var entryObj = new Dictionary<string, object?>
        {
            ["h"] = hash.ToString("x16"),
            ["n"] = n,
            ["s"] = sourceText,
            ["t"] = translation,
            ["mod"] = "CKANMeta",
            ["ctx"] = identifier,
            ["src"] = "human"
        };

        string jsonLine = JsonSerializer.Serialize(entryObj, JsonOptions);

        // 读出已有行，替换相同 (h, ctx) 或同 n 行，若无则追加
        var lines = new List<string>();
        if (File.Exists(overrideFile))
        {
            lines = (await File.ReadAllLinesAsync(overrideFile, Encoding.UTF8, ct)).ToList();
        }

        int replaceIdx = -1;
        string ctxPattern = $"\"ctx\":\"{identifier}\"";
        string hashPattern = $"\"h\":\"{hash:x16}\"";
        for (int i = 0; i < lines.Count; i++)
        {
            string line = lines[i];
            if (line.Contains(ctxPattern, StringComparison.Ordinal) && line.Contains(hashPattern, StringComparison.Ordinal))
            {
                replaceIdx = i;
                break;
            }
        }

        if (replaceIdx >= 0)
        {
            lines[replaceIdx] = jsonLine;
        }
        else
        {
            lines.Add(jsonLine);
        }

        string tmpFile = overrideFile + ".tmp";
        await File.WriteAllLinesAsync(tmpFile, lines, Encoding.UTF8, ct);
        File.Move(tmpFile, overrideFile, overwrite: true);

        // 热重载
        await ReloadPacksAsync(_currentLanguage, ct);
    }

    public async Task DeleteOverrideAsync(string identifier, string field, string sourceText, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceText);

        string overrideFile = Path.Combine(_baseDirectory, "packs", "overrides.jsonl");
        if (!File.Exists(overrideFile))
        {
            return;
        }

        string normalized = TextNormalizer.Normalize(sourceText);
        ulong hash = Fnv1a.Hash(normalized);

        var lines = (await File.ReadAllLinesAsync(overrideFile, Encoding.UTF8, ct)).ToList();
        string ctxPattern = $"\"ctx\":\"{identifier}\"";
        string hashPattern = $"\"h\":\"{hash:x16}\"";
        string nPattern = $"\"n\":\"ckan:{identifier}:{field}\"";

        int removeIdx = -1;
        for (int i = 0; i < lines.Count; i++)
        {
            string line = lines[i];
            if ((line.Contains(ctxPattern, StringComparison.Ordinal) && line.Contains(hashPattern, StringComparison.Ordinal)) ||
                line.Contains(nPattern, StringComparison.Ordinal))
            {
                removeIdx = i;
                break;
            }
        }

        if (removeIdx >= 0)
        {
            lines.RemoveAt(removeIdx);
            string tmpFile = overrideFile + ".tmp";
            await File.WriteAllLinesAsync(tmpFile, lines, Encoding.UTF8, ct);
            File.Move(tmpFile, overrideFile, overwrite: true);

            await ReloadPacksAsync(_currentLanguage, ct);
        }
    }

    public async Task ImportJsonlAsync(string targetFileName, string content, string? language = null, CancellationToken ct = default)
    {
        string lang = string.IsNullOrWhiteSpace(language) ? _currentLanguage : language.Trim().ToLowerInvariant();
        string dir;
        string destFile;

        if (targetFileName.Equals("overrides.jsonl", StringComparison.OrdinalIgnoreCase))
        {
            dir = Path.Combine(_baseDirectory, "packs");
            destFile = Path.Combine(dir, "overrides.jsonl");
        }
        else
        {
            dir = Path.Combine(_baseDirectory, "packs", lang);
            destFile = Path.Combine(dir, targetFileName);
        }

        Directory.CreateDirectory(dir);
        string tmp = destFile + ".tmp";
        await File.WriteAllTextAsync(tmp, content, Encoding.UTF8, ct);
        File.Move(tmp, destFile, overwrite: true);

        await ReloadPacksAsync(lang, ct);
    }

    public async Task<OperationResult<string>> ExportJsonlAsync(string relativePath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return OperationResult<string>.Fail("导出路径不能为空。");
        }

        string fullPath = Path.Combine(_baseDirectory, relativePath);
        if (!File.Exists(fullPath))
        {
            // 失败用返回值表达，不抛异常：调用方是 ViewModel，抛出去只能变成一条无人处理的崩溃（01 篇 §8）。
            return OperationResult<string>.Fail($"文件不存在: {relativePath}", fullPath);
        }

        try
        {
            return OperationResult<string>.Ok(await File.ReadAllTextAsync(fullPath, Encoding.UTF8, ct));
        }
        catch (IOException ex)
        {
            // 被其他进程占用/读到一半断了：用户重试可能成功，标可重试。
            Log.Warn($"[ModTextService] 导出 {relativePath} 失败: {ex.Message}");
            return OperationResult<string>.Fail($"读取失败: {relativePath}", ex.Message, retryable: true);
        }
        catch (UnauthorizedAccessException ex)
        {
            Log.Warn($"[ModTextService] 导出 {relativePath} 没有权限: {ex.Message}");
            return OperationResult<string>.Fail($"没有读取权限: {relativePath}", ex.Message);
        }
    }

    public async Task<OperationResult<int>> SyncFromUniversalI18nAsync(string? sourceDir = null, CancellationToken ct = default)
    {
        // 显式传了路径就只认这个路径：以前参数不存在时会静默回落到用户主目录里的真实仓库，
        // 用户点了"同步这个目录"却同步了另一份数据，既难解释也难测。
        if (!string.IsNullOrWhiteSpace(sourceDir) && !Directory.Exists(sourceDir))
        {
            return OperationResult<int>.Fail(
                "未找到 UniversalI18n 仓库或 packs 目录。请确认路径是否存在。",
                sourceDir);
        }

        string? resolvedRoot = ResolveUniversalI18nRoot(sourceDir);
        if (resolvedRoot == null || !Directory.Exists(resolvedRoot))
        {
            return OperationResult<int>.Fail("未找到 UniversalI18n 仓库或 packs 目录。请确认路径是否存在。");
        }

        string sourcePacks = Path.Combine(resolvedRoot, "packs");
        if (!Directory.Exists(sourcePacks))
        {
            sourcePacks = resolvedRoot;
        }

        string destPacks = Path.Combine(_baseDirectory, "packs");

        int copiedCount = 0;

        try
        {
            // 建目录也可能失败（只读盘、无权限），放在 try 内一起转成失败结果。
            Directory.CreateDirectory(destPacks);

            // 1. 同步根层词库与约定文件 (conventions.jsonl, glossary.jsonl, overrides.jsonl 等)
            foreach (var file in Directory.GetFiles(sourcePacks, "*.jsonl", SearchOption.TopDirectoryOnly))
            {
                ct.ThrowIfCancellationRequested();
                string fileName = Path.GetFileName(file);
                string dest = Path.Combine(destPacks, fileName);

                if (fileName.Equals("overrides.jsonl", StringComparison.OrdinalIgnoreCase) && File.Exists(dest))
                {
                    MergeJsonlLines(file, dest);
                }
                else
                {
                    File.Copy(file, dest, overwrite: true);
                }
                copiedCount++;
            }

            // 2. 同步子语言目录 (如 zh-cn/)
            foreach (var subDir in Directory.GetDirectories(sourcePacks))
            {
                ct.ThrowIfCancellationRequested();
                string dirName = Path.GetFileName(subDir);
                if (dirName.Equals("journal", StringComparison.OrdinalIgnoreCase)) continue;

                string destSubDir = Path.Combine(destPacks, dirName);
                Directory.CreateDirectory(destSubDir);

                foreach (var file in Directory.GetFiles(subDir, "*.jsonl", SearchOption.TopDirectoryOnly))
                {
                    ct.ThrowIfCancellationRequested();
                    string dest = Path.Combine(destSubDir, Path.GetFileName(file));
                    File.Copy(file, dest, overwrite: true);
                    copiedCount++;
                }
            }
        }
        catch (IOException ex)
        {
            // 半途失败时已复制的文件留在原处（覆盖写不产生半文件），重试会补齐剩余文件。
            Log.Warn($"[ModTextService] 从 UniversalI18n ({resolvedRoot}) 同步中断，已传输 {copiedCount} 个文件: {ex.Message}");
            return OperationResult<int>.Fail($"同步中断，已传输 {copiedCount} 个文件。", ex.Message, retryable: true);
        }
        catch (UnauthorizedAccessException ex)
        {
            Log.Warn($"[ModTextService] 从 UniversalI18n ({resolvedRoot}) 同步被拒绝: {ex.Message}");
            return OperationResult<int>.Fail("没有权限读写词库目录。", ex.Message);
        }

        Log.Info($"[ModTextService] 从 UniversalI18n ({resolvedRoot}) 同步完成，共传输 {copiedCount} 个文件。");
        await ReloadPacksAsync(_currentLanguage, ct);
        return OperationResult<int>.Ok(copiedCount);
    }

    /// <summary>
    /// 解析 UniversalI18n 仓库根：显式路径 → 环境变量 UNIVERSAL_I18N_ROOT → 用户主目录下的默认克隆。
    /// 注意：显式路径不存在时 <see cref="SyncFromUniversalI18nAsync"/> 直接返回失败，不会回落到后两者。
    /// </summary>
    /// <param name="customDir">显式指定的仓库根。</param>
    /// <returns>存在的仓库根；都找不到时返回 null。</returns>
    public static string? ResolveUniversalI18nRoot(string? customDir = null)
    {
        if (!string.IsNullOrWhiteSpace(customDir) && Directory.Exists(customDir))
        {
            return customDir;
        }

        string? env = Environment.GetEnvironmentVariable("UNIVERSAL_I18N_ROOT");
        if (!string.IsNullOrWhiteSpace(env) && Directory.Exists(env))
        {
            return env;
        }

        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string defaultGithub = Path.Combine(userProfile, "Documents", "github", "UniversalI18n");
        if (Directory.Exists(defaultGithub))
        {
            return defaultGithub;
        }

        return null;
    }

    private static void MergeJsonlLines(string sourceFile, string destFile)
    {
        try
        {
            var destLines = File.Exists(destFile) ? File.ReadAllLines(destFile, Encoding.UTF8).ToList() : new List<string>();
            var seenKeys = new HashSet<string>(StringComparer.Ordinal);

            foreach (var line in destLines)
            {
                string key = ExtractLineKey(line);
                if (!string.IsNullOrEmpty(key)) seenKeys.Add(key);
            }

            bool changed = false;
            foreach (var line in File.ReadLines(sourceFile, Encoding.UTF8))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                string key = ExtractLineKey(line);
                if (string.IsNullOrEmpty(key) || seenKeys.Add(key))
                {
                    destLines.Add(line);
                    changed = true;
                }
            }

            if (changed)
            {
                string tmp = destFile + ".tmp";
                File.WriteAllLines(tmp, destLines, Encoding.UTF8);
                File.Move(tmp, destFile, overwrite: true);
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"[ModTextService] 合并 overrides.jsonl 发生异常: {ex.Message}");
        }
    }

    private static string ExtractLineKey(string line)
    {
        // 提取 h 与 ctx 或 n 作为唯一键
        int hIdx = line.IndexOf("\"h\":", StringComparison.Ordinal);
        if (hIdx < 0) return line.Trim();
        int hEnd = line.IndexOf('"', hIdx + 5);
        if (hEnd < 0) return line.Trim();
        string h = line.Substring(hIdx + 5, hEnd - (hIdx + 5));

        int ctxIdx = line.IndexOf("\"ctx\":", StringComparison.Ordinal);
        if (ctxIdx >= 0)
        {
            int ctxEnd = line.IndexOf('"', ctxIdx + 7);
            if (ctxEnd >= 0)
            {
                string ctx = line.Substring(ctxIdx + 7, ctxEnd - (ctxIdx + 7));
                return $"{h}:{ctx}";
            }
        }
        return h;
    }

    private static int CountLines(string filePath)
    {
        try
        {
            int count = 0;
            using var reader = new StreamReader(filePath, Encoding.UTF8);
            while (reader.ReadLine() != null) count++;
            return count;
        }
        catch
        {
            return 0;
        }
    }

    private static void LoadJsonlFile(DictPack pack, string filePath)
    {
        try
        {
            using var reader = new StreamReader(filePath, Encoding.UTF8);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                pack.AddJsonLine(line);
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"[ModTextService] 读取文件失败 {filePath}: {ex.Message}");
        }
    }

    public void Dispose()
    {
    }
}
