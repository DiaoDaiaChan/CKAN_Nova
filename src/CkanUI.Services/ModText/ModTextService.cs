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
    private string _currentLanguage = "zh-cn";
    private int _totalEntries;

    public ModTextService(string? customBaseDirectory = null)
    {
        _baseDirectory = customBaseDirectory ?? GetDefaultStorageDirectory();
        _currentPack = new DictPack();
        _provider = new ModTextProvider(_currentPack);
    }

    public IModTextProvider Provider => _provider;
    public string CurrentLanguage => _currentLanguage;
    public int TotalEntries => _totalEntries;
    public string BaseDirectory => _baseDirectory;

    public event Action? PackChanged;

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

            var pack = new DictPack();

            try
            {
                // 1. 扫描 packs/<lang>/ 目录下的 *.jsonl
                string langDir = Path.Combine(_baseDirectory, "packs", lang);
                if (Directory.Exists(langDir))
                {
                    var files = Directory.GetFiles(langDir, "*.jsonl", SearchOption.TopDirectoryOnly);
                    Array.Sort(files, StringComparer.Ordinal);
                    foreach (var file in files)
                    {
                        LoadJsonlFile(pack, file);
                    }
                }

                // 2. 扫描 overrides.jsonl（用户/全局覆盖层）
                string overrideFile = Path.Combine(_baseDirectory, "packs", "overrides.jsonl");
                if (File.Exists(overrideFile))
                {
                    LoadJsonlFile(pack, overrideFile);
                }

                lock (_lock)
                {
                    _currentPack = pack;
                    _totalEntries = pack.Entries;
                    _provider = new ModTextProvider(pack);
                }
                Log.Info($"[ModTextService] 载入语言包 '{lang}' 完成，共 {_totalEntries} 条条目。");
            }
            catch (Exception ex)
            {
                Log.Warn($"[ModTextService] 载入语言包异常，降级为空词典: {ex.Message}");
                lock (_lock)
                {
                    _currentPack = new DictPack();
                    _provider = new ModTextProvider(_currentPack);
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

        string langDir = Path.Combine(_baseDirectory, "packs", lang);
        if (Directory.Exists(langDir))
        {
            foreach (var file in Directory.GetFiles(langDir, "*.jsonl", SearchOption.TopDirectoryOnly))
            {
                var fi = new FileInfo(file);
                int count = CountLines(file);
                result.Add(new ModPackFileInfo(Path.Combine("packs", lang, fi.Name), fi.FullName, fi.Length, count, fi.LastWriteTimeUtc));
            }
        }

        string overrideFile = Path.Combine(_baseDirectory, "packs", "overrides.jsonl");
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
        DictPack pack;
        lock (_lock)
        {
            pack = _currentPack;
        }

        var items = new List<ModTextEntryItem>(pack.AllEntries.Count);
        string? q = string.IsNullOrWhiteSpace(filter) ? null : filter.Trim();

        foreach (var entry in pack.AllEntries)
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

    public async Task<string> ExportJsonlAsync(string relativePath, CancellationToken ct = default)
    {
        string fullPath = Path.Combine(_baseDirectory, relativePath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"文件不存在: {relativePath}");
        }
        return await File.ReadAllTextAsync(fullPath, Encoding.UTF8, ct);
    }

    public async Task<int> SyncFromUniversalI18nAsync(string? sourceDir = null, CancellationToken ct = default)
    {
        string? resolvedRoot = ResolveUniversalI18nRoot(sourceDir);
        if (resolvedRoot == null || !Directory.Exists(resolvedRoot))
        {
            throw new DirectoryNotFoundException("未找到 UniversalI18n 仓库或 packs 目录。请确认路径是否存在。");
        }

        string sourcePacks = Path.Combine(resolvedRoot, "packs");
        if (!Directory.Exists(sourcePacks))
        {
            sourcePacks = resolvedRoot;
        }

        string destPacks = Path.Combine(_baseDirectory, "packs");
        Directory.CreateDirectory(destPacks);

        int copiedCount = 0;

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

        Log.Info($"[ModTextService] 从 UniversalI18n ({resolvedRoot}) 同步完成，共传输 {copiedCount} 个文件。");
        await ReloadPacksAsync(_currentLanguage, ct);
        return copiedCount;
    }

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
