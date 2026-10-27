using System.Globalization;
using System.Text;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.Sfs;

namespace CkanUI.Services.Saves;

/// <summary>
/// 存档文件服务实现（13 篇 §7 / 06 篇 E1）：索引、校验、格式化落盘。
/// </summary>
/// <remarks>
/// <para>本类**不抛异常**给调用方：文件不存在、被占用、结构损坏都转成
/// <see cref="OperationResult"/> 或诊断；真正的 bug 才会上报 <see cref="IErrorReporter"/>。</para>
/// <para>格式化落盘三条路径：<see cref="SaveFormatMode.Preview"/> 只算不写、
/// <see cref="SaveFormatMode.NewFile"/> 写 <c>&lt;name&gt;.formatted.sfs</c>（默认，原文件不动）、
/// <see cref="SaveFormatMode.Overwrite"/> 先快照再原子覆盖（13 篇 L8/L9/L10）。</para>
/// </remarks>
public sealed class SaveFileService : ISaveFileService
{
    /// <summary>KSP 的存档目录名。</summary>
    public const string SavesDirectoryName = "saves";

    /// <summary>存档主扩展名。</summary>
    public const string SaveExtension = ".sfs";

    /// <summary>备份扩展名：<c>persistent.sfs.bak</c> 这类文件同样要列出来。</summary>
    public const string BackupExtension = ".sfs.bak";

    /// <summary>本工具产出的格式化文件后缀（不当作存档列出）。</summary>
    public const string FormattedSuffix = ".formatted.sfs";

    /// <summary>写前快照的文件名片段（13 篇 §7.4）。</summary>
    public const string PreEditMarker = ".pre-edit-";

    /// <summary>预览片段上限（字符）：足够看结构，不会把 5MB 存档塞进 UI。</summary>
    private const int PreviewCharLimit = 20000;

    /// <summary>不当作存档槽遍历的子目录（KSP 自带的备份与飞船目录）。</summary>
    private static readonly string[] ExcludedDirectoryNames = { "Backup", "Ships" };

    private readonly IErrorReporter _errors;

    /// <summary>构造服务。</summary>
    /// <param name="errors">错误上报（所有异常都要留痕，04 篇 §3）。</param>
    public SaveFileService(IErrorReporter errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        _errors = errors;
    }

    /// <inheritdoc />
    public string GetSavesDirectory(string gameDir)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameDir);
        return Path.Combine(gameDir, SavesDirectoryName);
    }

    /// <inheritdoc />
    public async Task<OperationResult<IReadOnlyList<SaveGameInfo>>> ListSavesAsync(
        string gameDir,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(gameDir))
        {
            return OperationResult<IReadOnlyList<SaveGameInfo>>.Fail("实例目录为空，无法枚举存档。");
        }

        var root = GetSavesDirectory(gameDir);
        try
        {
            var rows = await Task.Run(() => Scan(root, cancellationToken), cancellationToken).ConfigureAwait(true);
            return OperationResult<IReadOnlyList<SaveGameInfo>>.Ok(rows);
        }
        catch (OperationCanceledException)
        {
            return OperationResult<IReadOnlyList<SaveGameInfo>>.Fail("枚举存档已取消。");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _errors.Report(ex, "枚举存档");
            return OperationResult<IReadOnlyList<SaveGameInfo>>.Fail(
                "读取存档目录失败：目录可能被其他程序占用或没有权限。",
                root + "：" + ex.Message);
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<SaveValidationReport>> ValidateAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return OperationResult<SaveValidationReport>.Fail("存档路径为空，无法校验。");
        }

        try
        {
            var report = await Task.Run(() => BuildReport(filePath), cancellationToken).ConfigureAwait(true);
            if (report is null)
            {
                return OperationResult<SaveValidationReport>.Fail($"存档文件不存在：{filePath}");
            }

            return OperationResult<SaveValidationReport>.Ok(report);
        }
        catch (OperationCanceledException)
        {
            return OperationResult<SaveValidationReport>.Fail("校验存档已取消。");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 13 篇 §7.2：文件被占用只提示、不重试。
            _errors.Report(ex, "校验存档");
            return OperationResult<SaveValidationReport>.Fail(
                "读取存档失败：文件可能被其他程序占用。",
                filePath + "：" + ex.Message);
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<SaveFormatOutcome>> FormatAsync(
        SaveFormatRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.FilePath))
        {
            return OperationResult<SaveFormatOutcome>.Fail("存档路径为空，无法格式化。");
        }

        try
        {
            var outcome = await Task.Run(() => FormatCore(request), cancellationToken).ConfigureAwait(true);
            if (outcome is null)
            {
                return OperationResult<SaveFormatOutcome>.Fail($"存档文件不存在：{request.FilePath}");
            }

            return OperationResult<SaveFormatOutcome>.Ok(outcome);
        }
        catch (OperationCanceledException)
        {
            return OperationResult<SaveFormatOutcome>.Fail("格式化存档已取消。");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _errors.Report(ex, "格式化存档");
            return OperationResult<SaveFormatOutcome>.Fail(
                "写入存档失败：文件可能被其他程序占用，或目录不可写。",
                request.FilePath + "：" + ex.Message);
        }
    }

    // ---- 索引 -------------------------------------------------------------

    private static IReadOnlyList<SaveGameInfo> Scan(string root, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(root))
        {
            // 没装过存档不是错误：页面显示空态（03 篇 §14.2）。
            return Array.Empty<SaveGameInfo>();
        }

        var rows = new List<SaveGameInfo>();
        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsSaveCandidate(root, path))
            {
                continue;
            }

            rows.Add(Describe(root, path));
        }

        return rows
            .OrderByDescending(row => row.LastWriteTime)
            .ThenBy(row => row.SlotName, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool IsSaveCandidate(string root, string path)
    {
        var name = Path.GetFileName(path);
        if (name.EndsWith(FormattedSuffix, StringComparison.OrdinalIgnoreCase) ||
            name.Contains(PreEditMarker, StringComparison.OrdinalIgnoreCase) ||
            name.Contains(".tmp-", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!name.EndsWith(SaveExtension, StringComparison.OrdinalIgnoreCase) &&
            !name.EndsWith(BackupExtension, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var relative = Path.GetRelativePath(root, path);
        var parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Length <= 1 || !ExcludedDirectoryNames.Contains(parts[0], StringComparer.OrdinalIgnoreCase);
    }

    private static SaveGameInfo Describe(string root, string path)
    {
        var info = new FileInfo(path);
        var meta = SaveMetaReader.Read(path);
        return new SaveGameInfo(
            SlotName: BuildSlotName(Path.GetRelativePath(root, path)),
            FilePath: info.FullName,
            SizeBytes: info.Exists ? info.Length : 0,
            LastWriteTime: new DateTimeOffset(info.Exists ? info.LastWriteTimeUtc : DateTime.UtcNow, TimeSpan.Zero),
            Title: meta.Title,
            Mode: meta.Mode,
            GameVersion: meta.GameVersion,
            Scene: meta.Scene,
            UniversalTime: meta.UniversalTime,
            VesselCount: meta.VesselCount,
            Funds: meta.Funds,
            SnapshotCount: CountSnapshots(info.DirectoryName),
            IsBackup: info.Name.EndsWith(BackupExtension, StringComparison.OrdinalIgnoreCase),
            HasLoadMeta: meta.HasLoadMeta);
    }

    private static string BuildSlotName(string relativePath)
    {
        var directory = Path.GetDirectoryName(relativePath);
        var fileName = Path.GetFileName(relativePath);
        var stem = fileName.EndsWith(BackupExtension, StringComparison.OrdinalIgnoreCase)
            ? fileName[..^BackupExtension.Length]
            : Path.GetFileNameWithoutExtension(fileName);

        return string.IsNullOrEmpty(directory)
            ? stem
            : directory.Replace('\\', '/') + " / " + stem;
    }

    private static int CountSnapshots(string? directory)
    {
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
        {
            return 0;
        }

        var count = 0;
        try
        {
            foreach (var file in Directory.EnumerateFiles(directory))
            {
                if (Path.GetFileName(file).Contains(PreEditMarker, StringComparison.OrdinalIgnoreCase))
                {
                    count++;
                }
            }

            var backupDirectory = Path.Combine(directory, "Backup");
            if (Directory.Exists(backupDirectory))
            {
                count += Directory.EnumerateFiles(backupDirectory, "*" + SaveExtension).Count();
            }
        }
        catch (IOException)
        {
            // 快照数只是展示字段，读不到就当 0。
        }
        catch (UnauthorizedAccessException)
        {
        }

        return count;
    }

    // ---- 校验 -------------------------------------------------------------

    private static SaveValidationReport? BuildReport(string filePath)
    {
        var info = new FileInfo(filePath);
        if (!info.Exists)
        {
            return null;
        }

        var bytes = File.ReadAllBytes(info.FullName);
        var parsed = SfsParser.ParseBytes(bytes, info.FullName);

        IReadOnlyList<SfsDiagnostic> diagnostics;
        var lineCount = 0;
        var nodeCount = 0;
        var pairCount = 0;
        var maxDepth = 0;
        var hasBom = false;
        var newLine = string.Empty;
        var isTextLossy = false;

        if (parsed.Document is { } document)
        {
            diagnostics = SfsValidator.Validate(document).Diagnostics;
            lineCount = document.LineCount;
            nodeCount = document.NodeCount;
            pairCount = document.PairCount;
            maxDepth = document.MaxDepth;
            hasBom = document.HasBom;
            newLine = document.DominantNewLine;
            isTextLossy = document.IsTextLossy;
        }
        else
        {
            // 结构完全解析不出来：直接把解析诊断当校验结果，仍然是"有诊断、不抛异常"。
            diagnostics = parsed.Diagnostics;
        }

        return new SaveValidationReport(
            FilePath: info.FullName,
            SizeBytes: info.Length,
            LastWriteTime: new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero),
            HasBom: hasBom,
            DominantNewLine: newLine,
            LineCount: lineCount,
            NodeCount: nodeCount,
            PairCount: pairCount,
            MaxDepth: maxDepth,
            IsTextLossy: isTextLossy,
            Diagnostics: SaveDiagnosticMapper.MapAll(diagnostics));
    }

    // ---- 格式化 -----------------------------------------------------------

    private static SaveFormatOutcome? FormatCore(SaveFormatRequest request)
    {
        var info = new FileInfo(request.FilePath);
        if (!info.Exists)
        {
            return null;
        }

        var originalBytes = File.ReadAllBytes(info.FullName);
        var parsed = SfsParser.ParseBytes(originalBytes, info.FullName);
        if (parsed.Document is not { } document)
        {
            return Refused(
                request,
                originalBytes.LongLength,
                "存档有解析错误（结构不完整），已拒绝格式化，原文件未改动。",
                parsed.Diagnostics);
        }

        var result = SfsFormatter.Format(document, SfsFormatOptions.Default);
        if (result.Refused)
        {
            return Refused(
                request,
                originalBytes.LongLength,
                result.RefusalReason ?? "格式化被拒绝（自校验未通过），原文件未改动。",
                result.Diagnostics);
        }

        var formattedBytes = Encoding.UTF8.GetByteCount(result.Text);
        var stats = new SaveFormatStats(
            ReindentedLines: result.Stats.ReindentedLines,
            NormalizedSeparators: result.Stats.NormalizedSeparators,
            NormalizedLineEndings: result.Stats.NormalizedLineEndings,
            BlankLinesRemoved: result.Stats.BlankLinesRemoved);

        if (request.Mode == SaveFormatMode.Preview)
        {
            return new SaveFormatOutcome(
                SourcePath: info.FullName,
                Mode: request.Mode,
                Refused: false,
                RefusalReason: null,
                WroteFile: false,
                WrittenPath: null,
                BackupPath: null,
                Changed: result.Changed,
                SemanticsPreserved: result.SelfCheckPassed,
                BytesBefore: originalBytes.LongLength,
                BytesAfter: formattedBytes,
                Stats: stats,
                Preview: Condense(result.Text));
        }

        var overwrite = request.Mode == SaveFormatMode.Overwrite;
        var target = overwrite
            ? info.FullName
            : Path.Combine(
                info.DirectoryName!,
                Path.GetFileNameWithoutExtension(info.Name) + FormattedSuffix);

        string? backupPath = null;
        if (overwrite)
        {
            // L9：任何覆盖写之前必须先落一份快照，快照建不出来就不写。
            backupPath = SaveFileWriter.CreatePreEditSnapshot(info.FullName);
            if (backupPath is null)
            {
                return Refused(
                    request,
                    originalBytes.LongLength,
                    "无法创建写前快照（目录不可写或同名快照过多），已拒绝覆盖写，原文件未改动。",
                    Array.Empty<SfsDiagnostic>());
            }
        }

        // L10：覆盖写保留原修改时间，避免存档在主菜单里"跳到最新"。
        var preserveStamp = overwrite ? info.LastWriteTimeUtc : (DateTime?)null;
        SaveFileWriter.WriteAtomic(target, result.Text, preserveStamp);

        return new SaveFormatOutcome(
            SourcePath: info.FullName,
            Mode: request.Mode,
            Refused: false,
            RefusalReason: null,
            WroteFile: true,
            WrittenPath: target,
            BackupPath: backupPath,
            Changed: result.Changed,
            SemanticsPreserved: result.SelfCheckPassed,
            BytesBefore: originalBytes.LongLength,
            BytesAfter: formattedBytes,
            Stats: stats,
            Preview: null);
    }

    private static SaveFormatOutcome Refused(
        SaveFormatRequest request,
        long bytesBefore,
        string reason,
        IReadOnlyList<SfsDiagnostic> diagnostics)
    {
        return new SaveFormatOutcome(
            SourcePath: request.FilePath,
            Mode: request.Mode,
            Refused: true,
            RefusalReason: reason,
            WroteFile: false,
            WrittenPath: null,
            BackupPath: null,
            Changed: false,
            SemanticsPreserved: false,
            BytesBefore: bytesBefore,
            BytesAfter: bytesBefore,
            Stats: new SaveFormatStats(0, 0, 0, 0),
            Preview: diagnostics.Count == 0
                ? null
                : $"共 {diagnostics.Count.ToString(CultureInfo.InvariantCulture)} 条诊断，首条：{diagnostics[0]}");
    }

    private static string Condense(string text)
    {
        if (text.Length <= PreviewCharLimit)
        {
            return text;
        }

        return string.Concat(
            text.AsSpan(0, PreviewCharLimit),
            "\n…（预览已截断，共 ",
            text.Length.ToString(CultureInfo.InvariantCulture),
            " 字符）");
    }
}
