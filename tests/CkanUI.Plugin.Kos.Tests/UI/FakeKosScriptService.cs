using System.Text;
using CkanUI.Plugin.Kos.Scripts;
using CkanUI.Services.Abstractions;

namespace CkanUI.Plugin.Kos.Tests.UI;

/// <summary>
/// 内存版 Archive 卷：测试用，不碰真实磁盘。
/// 目录/模组状态、脚本内容、最后写入时间都可控，调用参数全部留痕。
/// </summary>
internal sealed class FakeKosScriptService : IKosScriptService
{
    private readonly Dictionary<string, FakeScript> _files = new(StringComparer.OrdinalIgnoreCase);

    public string GameDir { get; set; } = Path.Combine(Path.GetTempPath(), "ckan-ui-kos-ui-tests", "game");

    public bool ModInstalled { get; set; } = true;

    public bool DirectoryExists { get; set; } = true;

    public List<string> Diagnostics { get; } = [];

    public int LoadCatalogCount { get; private set; }

    public List<string> CatalogGameDirs { get; } = [];

    public List<LoadCall> LoadCalls { get; } = [];

    public List<SaveCall> SaveCalls { get; } = [];

    public List<CreateCall> CreateCalls { get; } = [];

    public List<RenameCall> RenameCalls { get; } = [];

    public List<DeleteCall> DeleteCalls { get; } = [];

    /// <summary>设置后 Save 返回失败（用于“外部改动/磁盘错误时正文不丢”）。</summary>
    public string? SaveFailureMessage { get; set; }

    /// <summary>这些名字的 Load 返回失败。</summary>
    public HashSet<string> LoadFailures { get; } = new(StringComparer.OrdinalIgnoreCase);

    public string ScriptDirectory => Path.Combine(GameDir, "Ships", "Script");

    public void AddScript(string name, string content = "", bool isBoot = false, DateTime? lastWriteUtc = null)
    {
        _files[name] = new FakeScript(name, content, isBoot, lastWriteUtc ?? new DateTime(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc));
    }

    public KosScriptCatalog LoadCatalog(string? gameDir)
    {
        LoadCatalogCount++;
        CatalogGameDirs.Add(gameDir ?? string.Empty);
        var files = _files.Values
            .OrderBy(file => file.Name, StringComparer.OrdinalIgnoreCase)
            .Select(file => file.ToFile(ScriptDirectory))
            .ToList();
        return new KosScriptCatalog(
            gameDir ?? string.Empty,
            ScriptDirectory,
            DirectoryExists,
            ModInstalled,
            files,
            Diagnostics.ToList());
    }

    public OperationResult<KosScriptDocument> Load(string gameDir, string name)
    {
        LoadCalls.Add(new LoadCall(gameDir, name));
        if (!_files.TryGetValue(name, out var file))
        {
            return OperationResult<KosScriptDocument>.Fail($"脚本“{name}”不存在。", Path.Combine(gameDir, "Ships", "Script", name));
        }

        if (LoadFailures.Contains(name))
        {
            return OperationResult<KosScriptDocument>.Fail($"脚本“{name}”被占用，无法读取。");
        }

        return OperationResult<KosScriptDocument>.Ok(file.ToDocument(ScriptDirectory));
    }

    public OperationResult<KosScriptSaveOutcome> Save(
        string gameDir,
        string name,
        string content,
        DateTime? expectedLastWriteUtc = null)
    {
        SaveCalls.Add(new SaveCall(gameDir, name, content, expectedLastWriteUtc));
        if (SaveFailureMessage is { Length: > 0 } failure)
        {
            return OperationResult<KosScriptSaveOutcome>.Fail(failure);
        }

        if (!_files.TryGetValue(name, out var file))
        {
            return OperationResult<KosScriptSaveOutcome>.Fail($"脚本“{name}”不存在。");
        }

        file.Content = Normalize(content ?? string.Empty);
        file.LastWriteUtc = DateTime.UtcNow;
        return OperationResult<KosScriptSaveOutcome>.Ok(new KosScriptSaveOutcome(
            file.Name,
            Path.Combine(ScriptDirectory, file.Name),
            Path.Combine(ScriptDirectory, file.Name) + ".bak-test",
            CountLines(file.Content),
            file.SizeBytes));
    }

    public OperationResult<KosScriptSaveOutcome> Create(string gameDir, string name, string content)
    {
        CreateCalls.Add(new CreateCall(gameDir, name, content));
        if (_files.ContainsKey(name))
        {
            return OperationResult<KosScriptSaveOutcome>.Fail($"脚本“{name}”已存在，未覆盖。");
        }

        var file = new FakeScript(name, Normalize(content ?? string.Empty), isBoot: false, DateTime.UtcNow);
        _files[name] = file;
        return OperationResult<KosScriptSaveOutcome>.Ok(new KosScriptSaveOutcome(
            name,
            Path.Combine(ScriptDirectory, name),
            null,
            CountLines(file.Content),
            file.SizeBytes));
    }

    public OperationResult<string> Rename(string gameDir, string oldName, string newName)
    {
        RenameCalls.Add(new RenameCall(gameDir, oldName, newName));
        if (!_files.TryGetValue(oldName, out var file))
        {
            return OperationResult<string>.Fail($"脚本“{oldName}”不存在。");
        }

        if (_files.ContainsKey(newName))
        {
            return OperationResult<string>.Fail($"目标文件“{newName}”已存在，未覆盖。");
        }

        _files.Remove(oldName);
        file.Name = newName;
        _files[newName] = file;
        return OperationResult<string>.Ok(newName);
    }

    public OperationResult<string> Delete(string gameDir, string name)
    {
        DeleteCalls.Add(new DeleteCall(gameDir, name));
        if (!_files.Remove(name))
        {
            return OperationResult<string>.Fail($"脚本“{name}”不存在，无法删除。");
        }

        return OperationResult<string>.Ok(Path.Combine(ScriptDirectory, name) + ".bak-test");
    }

    private static string Normalize(string content)
        => content.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    private static int CountLines(string normalizedText)
    {
        if (normalizedText.Length == 0)
        {
            return 0;
        }

        var newlines = 0;
        foreach (var ch in normalizedText)
        {
            if (ch == '\n')
            {
                newlines++;
            }
        }

        return normalizedText[^1] == '\n' ? newlines : newlines + 1;
    }

    internal sealed record LoadCall(string GameDir, string Name);

    internal sealed record SaveCall(string GameDir, string Name, string Content, DateTime? ExpectedLastWriteUtc);

    internal sealed record CreateCall(string GameDir, string Name, string Content);

    internal sealed record RenameCall(string GameDir, string OldName, string NewName);

    internal sealed record DeleteCall(string GameDir, string Name);

    private sealed class FakeScript(string name, string content, bool isBoot, DateTime lastWriteUtc)
    {
        public string Name { get; set; } = name;

        public string Content { get; set; } = Normalize(content);

        public bool IsBoot { get; } = isBoot;

        public DateTime LastWriteUtc { get; set; } = lastWriteUtc;

        public long SizeBytes => Encoding.UTF8.GetByteCount(Content);

        public KosScriptFile ToFile(string directory)
            => new(Name, Path.Combine(directory, Name), SizeBytes, LastWriteUtc, CountLines(Content), IsBoot);

        public KosScriptDocument ToDocument(string directory)
            => new(
                Name,
                Path.Combine(directory, Name),
                Content,
                "\r\n",
                false,
                SizeBytes,
                LastWriteUtc,
                IsBoot);
    }
}
