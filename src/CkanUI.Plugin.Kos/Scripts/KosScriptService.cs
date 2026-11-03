using System.Globalization;
using System.Security;
using System.Text;
using CkanUI.Services.Abstractions;

namespace CkanUI.Plugin.Kos.Scripts;

/// <summary>
/// <see cref="IKosScriptService"/> 的默认实现：只读写 &lt;gameDir&gt;/Ships/Script 下的 .ks 文件。
/// </summary>
/// <remarks>
/// <para>
/// kOS 的 Archive 卷不是打包格式：官方文档 general/volumes 写明 Archive 里的文件就是
/// Ships/Script 下扩展名为 .ks 的真实文件。因此这里只认这个目录、只认 .ks，别处的同名文件一律不碰。
/// </para>
/// <para>
/// 两条纪律贯穿全文件：
/// 一、公开方法一律不抛异常——游戏开着、文件被占用、实例被拔盘、文件名里塞路径分隔符都是常态，
/// 全部转成 <see cref="OperationResult{T}"/> 的中文失败文案或 Catalog 的 Diagnostics；
/// 二、覆盖前先备份、写入走同目录临时文件 + <c>File.Move(overwrite: true)</c>——
/// 脚本是玩家手写的代码，不能因为一次写盘失败留下半截 .ks。
/// </para>
/// </remarks>
public sealed class KosScriptService : IKosScriptService
{
    /// <summary>KSP 实例下的飞船目录名。</summary>
    public const string ShipsDirectoryName = "Ships";

    /// <summary>Archive 卷对应的子目录名（相对 Ships）。</summary>
    public const string ScriptDirectoryName = "Script";

    /// <summary>KSP 实例下的模组目录名。</summary>
    public const string GameDataDirectoryName = "GameData";

    /// <summary>kOS 模组目录名。</summary>
    public const string KosModDirectoryName = "kOS";

    /// <summary>kOS 脚本扩展名（保存时统一成小写）。</summary>
    public const string ScriptExtension = ".ks";

    /// <summary>kOS 的启动脚本名。</summary>
    public const string BootScriptName = "boot.ks";

    /// <summary>单个脚本的大小上限（2 MB）：超过就不进编辑器。</summary>
    public const long MaxScriptBytes = 2L * 1024 * 1024;

    /// <summary>同一秒内备份重名时最多追加到 -20。</summary>
    public const int MaxBackupSuffix = 20;

    /// <summary>新建脚本时的默认换行。</summary>
    public const string DefaultNewLine = "\r\n";

    private const string BackupMarker = ".bak-";
    private const string TemporaryMarker = ".tmp-";

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private static readonly TimeSpan ExternalChangeTolerance = TimeSpan.FromSeconds(1);

    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static readonly char[] DirectorySeparators =
    {
        Path.DirectorySeparatorChar,
        Path.AltDirectorySeparatorChar,
        '/',
        '\\',
    };

    /// <inheritdoc />
    public KosScriptCatalog LoadCatalog(string? gameDir)
    {
        var diagnostics = new List<string>();
        var scripts = new List<KosScriptFile>();

        if (string.IsNullOrWhiteSpace(gameDir))
        {
            diagnostics.Add("没有选择实例，无法定位 kOS 的 Archive 脚本目录。");
            return new KosScriptCatalog(string.Empty, string.Empty, false, false, scripts, diagnostics);
        }

        string scriptDirectory;
        bool directoryExists;
        bool modInstalled;
        try
        {
            scriptDirectory = Path.Combine(gameDir, ShipsDirectoryName, ScriptDirectoryName);
            directoryExists = Directory.Exists(scriptDirectory);
            modInstalled = Directory.Exists(Path.Combine(gameDir, GameDataDirectoryName, KosModDirectoryName));
        }
        catch (Exception ex) when (IsRecoverable(ex))
        {
            // 路径本身非法（盘符消失、网络路径断开）不该让整个页面空白：照样返回期望路径，让空态能提示。
            diagnostics.Add("定位 Archive 脚本目录失败：" + ex.Message);
            return new KosScriptCatalog(gameDir, string.Empty, false, false, scripts, diagnostics);
        }

        if (directoryExists)
        {
            ScanScripts(scriptDirectory, scripts, diagnostics);
        }

        // 目录枚举顺序依赖文件系统，界面要稳定顺序：按文件名忽略大小写排序。
        scripts.Sort(static (left, right) => StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name));
        return new KosScriptCatalog(gameDir, scriptDirectory, directoryExists, modInstalled, scripts, diagnostics);
    }

    /// <inheritdoc />
    public OperationResult<KosScriptDocument> Load(string gameDir, string name)
    {
        try
        {
            if (!TryResolveDirectory(gameDir, out var directory, out var directoryError))
            {
                return OperationResult<KosScriptDocument>.Fail(directoryError);
            }

            if (!TryValidateName(name, out var normalized, out var nameError))
            {
                return OperationResult<KosScriptDocument>.Fail(nameError);
            }

            var fullPath = Path.Combine(directory, normalized);
            if (!IsUnderDirectory(fullPath, directory))
            {
                return OperationResult<KosScriptDocument>.Fail("脚本路径不在 Archive 目录内，已拒绝读取。", fullPath);
            }

            if (!File.Exists(fullPath))
            {
                return OperationResult<KosScriptDocument>.Fail($"脚本“{normalized}”不存在。", fullPath);
            }

            var length = new FileInfo(fullPath).Length;
            if (length > MaxScriptBytes)
            {
                return OperationResult<KosScriptDocument>.Fail(
                    $"脚本“{normalized}”有 {FormatBytes(length)}，超过 2 MB 上限，不在编辑器中打开。",
                    fullPath);
            }

            var bytes = ReadAllBytesShared(fullPath);
            var text = Decode(bytes, out var hadBom);
            var content = NormalizeToLf(text);
            var lastWriteUtc = File.GetLastWriteTimeUtc(fullPath);

            return OperationResult<KosScriptDocument>.Ok(new KosScriptDocument(
                normalized,
                fullPath,
                content,
                DetectNewLine(text),
                hadBom,
                bytes.Length,
                lastWriteUtc,
                IsBootScript(normalized)));
        }
        catch (Exception ex) when (IsRecoverable(ex))
        {
            return OperationResult<KosScriptDocument>.Fail("读取脚本失败：" + ex.Message, ex.ToString());
        }
    }

    /// <inheritdoc />
    public OperationResult<KosScriptSaveOutcome> Save(
        string gameDir,
        string name,
        string content,
        DateTime? expectedLastWriteUtc = null)
    {
        try
        {
            if (!TryResolveDirectory(gameDir, out var directory, out var directoryError))
            {
                return OperationResult<KosScriptSaveOutcome>.Fail(directoryError);
            }

            if (!TryValidateName(name, out var normalized, out var nameError))
            {
                return OperationResult<KosScriptSaveOutcome>.Fail(nameError);
            }

            var fullPath = Path.Combine(directory, normalized);
            if (!IsUnderDirectory(fullPath, directory))
            {
                return OperationResult<KosScriptSaveOutcome>.Fail("脚本路径不在 Archive 目录内，已拒绝保存。", fullPath);
            }

            if (!File.Exists(fullPath))
            {
                return OperationResult<KosScriptSaveOutcome>.Fail($"脚本“{normalized}”不存在，无法保存。", fullPath);
            }

            var diskLastWriteUtc = File.GetLastWriteTimeUtc(fullPath);
            if (expectedLastWriteUtc is { } expected)
            {
                var expectedUtc = NormalizeUtc(expected);
                if ((diskLastWriteUtc - expectedUtc).Duration() > ExternalChangeTolerance)
                {
                    return OperationResult<KosScriptSaveOutcome>.Fail(
                        $"脚本“{normalized}”已被外部修改，已拒绝覆盖。请重新打开确认最新内容后再保存。",
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "打开时：{0:O}；磁盘上：{1:O}",
                            expectedUtc,
                            diskLastWriteUtc));
                }
            }

            var normalizedContent = NormalizeToLf(content ?? string.Empty);
            var newLine = DetectFileNewLine(fullPath);
            if (!TryCreateBackup(fullPath, out var backupPath, out var backupError))
            {
                return OperationResult<KosScriptSaveOutcome>.Fail(backupError, fullPath);
            }

            var temporaryPath = fullPath + TemporaryMarker + Guid.NewGuid().ToString("N");
            try
            {
                WriteScriptFile(temporaryPath, normalizedContent, newLine);
                // 同目录 Move + overwrite：要么旧文件完整保留，要么新内容完整落盘，不存在“写了一半”。
                File.Move(temporaryPath, fullPath, overwrite: true);
            }
            catch (Exception ex) when (IsRecoverable(ex))
            {
                return OperationResult<KosScriptSaveOutcome>.Fail("保存脚本失败：" + ex.Message, ex.ToString());
            }
            finally
            {
                TryDeleteFile(temporaryPath);
            }

            var sizeBytes = new FileInfo(fullPath).Length;
            return OperationResult<KosScriptSaveOutcome>.Ok(new KosScriptSaveOutcome(
                normalized,
                fullPath,
                backupPath,
                CountLines(normalizedContent),
                sizeBytes));
        }
        catch (Exception ex) when (IsRecoverable(ex))
        {
            return OperationResult<KosScriptSaveOutcome>.Fail("保存脚本失败：" + ex.Message, ex.ToString());
        }
    }

    /// <inheritdoc />
    public OperationResult<KosScriptSaveOutcome> Create(string gameDir, string name, string content)
    {
        try
        {
            if (!TryResolveDirectory(gameDir, out var directory, out var directoryError))
            {
                return OperationResult<KosScriptSaveOutcome>.Fail(directoryError);
            }

            if (!TryValidateName(name, out var normalized, out var nameError))
            {
                return OperationResult<KosScriptSaveOutcome>.Fail(nameError);
            }

            var fullPath = Path.Combine(directory, normalized);
            if (!IsUnderDirectory(fullPath, directory))
            {
                return OperationResult<KosScriptSaveOutcome>.Fail("脚本路径不在 Archive 目录内，已拒绝新建。", fullPath);
            }

            if (File.Exists(fullPath))
            {
                return OperationResult<KosScriptSaveOutcome>.Fail($"脚本“{normalized}”已存在，未覆盖。", fullPath);
            }

            var normalizedContent = NormalizeToLf(content ?? string.Empty);
            Directory.CreateDirectory(directory);

            var temporaryPath = fullPath + TemporaryMarker + Guid.NewGuid().ToString("N");
            try
            {
                // 新建默认 CRLF：kOS 编辑器与 Windows 上的其它脚本工具都按 CRLF 显示更稳。
                WriteScriptFile(temporaryPath, normalizedContent, DefaultNewLine);
                File.Move(temporaryPath, fullPath, overwrite: false);
            }
            catch (Exception ex) when (IsRecoverable(ex))
            {
                var message = File.Exists(fullPath)
                    ? $"脚本“{normalized}”已存在，未覆盖。"
                    : "新建脚本失败：" + ex.Message;
                return OperationResult<KosScriptSaveOutcome>.Fail(message, ex.ToString());
            }
            finally
            {
                TryDeleteFile(temporaryPath);
            }

            var sizeBytes = new FileInfo(fullPath).Length;
            return OperationResult<KosScriptSaveOutcome>.Ok(new KosScriptSaveOutcome(
                normalized,
                fullPath,
                null,
                CountLines(normalizedContent),
                sizeBytes));
        }
        catch (Exception ex) when (IsRecoverable(ex))
        {
            return OperationResult<KosScriptSaveOutcome>.Fail("新建脚本失败：" + ex.Message, ex.ToString());
        }
    }

    /// <inheritdoc />
    public OperationResult<string> Rename(string gameDir, string oldName, string newName)
    {
        try
        {
            if (!TryResolveDirectory(gameDir, out var directory, out var directoryError))
            {
                return OperationResult<string>.Fail(directoryError);
            }

            if (!TryValidateName(oldName, out var oldNormalized, out var oldError))
            {
                return OperationResult<string>.Fail(oldError);
            }

            if (!TryValidateName(newName, out var newNormalized, out var newError))
            {
                return OperationResult<string>.Fail(newError);
            }

            var oldPath = Path.Combine(directory, oldNormalized);
            var newPath = Path.Combine(directory, newNormalized);
            if (!IsUnderDirectory(oldPath, directory) || !IsUnderDirectory(newPath, directory))
            {
                return OperationResult<string>.Fail("脚本路径不在 Archive 目录内，已拒绝重命名。", newPath);
            }

            if (!File.Exists(oldPath))
            {
                return OperationResult<string>.Fail($"脚本“{oldNormalized}”不存在。", oldPath);
            }

            if (string.Equals(oldNormalized, newNormalized, StringComparison.Ordinal))
            {
                return OperationResult<string>.Fail("新文件名与原文件名相同。", newPath);
            }

            if (File.Exists(newPath))
            {
                return OperationResult<string>.Fail($"目标文件“{newNormalized}”已存在，未覆盖。", newPath);
            }

            // 不覆盖：目标存在时 File.Move 自己也会抛，但上面先给出可读文案，不靠异常兜底。
            File.Move(oldPath, newPath);
            return OperationResult<string>.Ok(newNormalized);
        }
        catch (Exception ex) when (IsRecoverable(ex))
        {
            return OperationResult<string>.Fail("重命名脚本失败：" + ex.Message, ex.ToString());
        }
    }

    /// <inheritdoc />
    public OperationResult<string> Delete(string gameDir, string name)
    {
        try
        {
            if (!TryResolveDirectory(gameDir, out var directory, out var directoryError))
            {
                return OperationResult<string>.Fail(directoryError);
            }

            if (!TryValidateName(name, out var normalized, out var nameError))
            {
                return OperationResult<string>.Fail(nameError);
            }

            var fullPath = Path.Combine(directory, normalized);
            if (!IsUnderDirectory(fullPath, directory))
            {
                return OperationResult<string>.Fail("脚本路径不在 Archive 目录内，已拒绝删除。", fullPath);
            }

            if (!File.Exists(fullPath))
            {
                return OperationResult<string>.Fail($"脚本“{normalized}”不存在，无法删除。", fullPath);
            }

            if (!TryCreateBackup(fullPath, out var backupPath, out var backupError))
            {
                return OperationResult<string>.Fail(backupError, fullPath);
            }

            try
            {
                File.Delete(fullPath);
            }
            catch (Exception ex) when (IsRecoverable(ex))
            {
                return OperationResult<string>.Fail(
                    $"删除脚本“{normalized}”失败：{ex.Message}（已保留备份：{backupPath}）",
                    ex.ToString());
            }

            return OperationResult<string>.Ok(backupPath);
        }
        catch (Exception ex) when (IsRecoverable(ex))
        {
            return OperationResult<string>.Fail("删除脚本失败：" + ex.Message, ex.ToString());
        }
    }

    private static void ScanScripts(string scriptDirectory, List<KosScriptFile> scripts, List<string> diagnostics)
    {
        List<string> paths;
        try
        {
            paths = Directory.EnumerateFiles(scriptDirectory).ToList();
        }
        catch (Exception ex) when (IsRecoverable(ex))
        {
            diagnostics.Add("读取 Archive 脚本目录失败：" + ex.Message);
            return;
        }

        foreach (var path in paths)
        {
            var name = Path.GetFileName(path);
            if (!name.EndsWith(ScriptExtension, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                var info = new FileInfo(path);
                var sizeBytes = info.Length;
                var lastWriteUtc = info.LastWriteTimeUtc;
                int? lineCount = null;

                if (sizeBytes > MaxScriptBytes)
                {
                    // 2 MB 以上的脚本不进编辑器；行数统计也跳过，避免为了列表把整个大文件读一遍。
                    diagnostics.Add($"脚本“{name}”超过 2 MB 上限，未统计行数。");
                }
                else
                {
                    try
                    {
                        lineCount = CountLines(NormalizeToLf(ReadTextShared(path)));
                    }
                    catch (Exception ex) when (IsRecoverable(ex))
                    {
                        // 单个坏文件（被占用、权限不足）只让这一行缺行数，不能拖垮整页。
                        diagnostics.Add($"读取脚本“{name}”行数失败：{ex.Message}");
                    }
                }

                scripts.Add(new KosScriptFile(name, path, sizeBytes, lastWriteUtc, lineCount, IsBootScript(name)));
            }
            catch (Exception ex) when (IsRecoverable(ex))
            {
                diagnostics.Add($"读取脚本“{name}”信息失败：{ex.Message}");
            }
        }
    }

    private static bool TryResolveDirectory(string? gameDir, out string directory, out string error)
    {
        directory = string.Empty;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(gameDir))
        {
            error = "没有选择实例，无法定位 kOS 的 Archive 脚本目录。";
            return false;
        }

        try
        {
            directory = Path.GetFullPath(Path.Combine(gameDir, ShipsDirectoryName, ScriptDirectoryName));
            return true;
        }
        catch (Exception ex) when (IsRecoverable(ex))
        {
            error = "实例目录无效：" + ex.Message;
            return false;
        }
    }

    private static bool TryValidateName(string? name, out string normalized, out string error)
    {
        normalized = string.Empty;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(name))
        {
            error = "文件名不能为空。";
            return false;
        }

        if (!string.Equals(name, name.Trim(), StringComparison.Ordinal))
        {
            error = "文件名首尾不能有空白字符。";
            return false;
        }

        if (name.IndexOfAny(DirectorySeparators) >= 0)
        {
            // 遍历攻击（../x.ks、sub/x.ks）在这里就被挡住，根本走不到路径拼接。
            error = "文件名不能包含路径分隔符（/ 或 \\）。";
            return false;
        }

        if (name.IndexOf(':') >= 0)
        {
            error = "文件名不能包含冒号。";
            return false;
        }

        if (name.Contains("..", StringComparison.Ordinal))
        {
            error = "文件名不能包含“..”。";
            return false;
        }

        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            if (name.IndexOf(invalid) >= 0)
            {
                error = $"文件名包含非法字符“{invalid}”。";
                return false;
            }
        }

        if (!name.EndsWith(ScriptExtension, StringComparison.OrdinalIgnoreCase))
        {
            error = "文件名必须以 .ks 结尾。";
            return false;
        }

        var stem = name[..^ScriptExtension.Length];
        if (stem.Length == 0)
        {
            error = "文件名不能只有扩展名。";
            return false;
        }

        // 保存时统一成小写扩展名：Windows 上大小写不敏感，但跨平台拷贝与 diff 都希望看到同一个写法。
        normalized = stem + ScriptExtension;
        return true;
    }

    private static bool IsUnderDirectory(string path, string directory)
    {
        var root = Path.GetFullPath(directory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var target = Path.GetFullPath(path);
        return target.StartsWith(root, PathComparison);
    }

    private static bool TryCreateBackup(string fullPath, out string backupPath, out string error)
    {
        backupPath = string.Empty;
        error = string.Empty;

        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var basePath = fullPath + BackupMarker + stamp;

        for (var suffix = 0; suffix <= MaxBackupSuffix; suffix++)
        {
            var candidate = suffix == 0
                ? basePath
                : basePath + "-" + suffix.ToString(CultureInfo.InvariantCulture);

            if (File.Exists(candidate))
            {
                continue;
            }

            try
            {
                File.Copy(fullPath, candidate, overwrite: false);
                backupPath = candidate;
                return true;
            }
            catch (IOException) when (File.Exists(candidate))
            {
                // 并发下别的进程抢先建了同名备份，换下一个后缀，不覆盖别人的备份。
            }
            catch (Exception ex) when (IsRecoverable(ex))
            {
                error = "创建备份失败：" + ex.Message;
                return false;
            }
        }

        error = string.Format(
            CultureInfo.InvariantCulture,
            "同一秒内的备份文件已有 {0} 份，请先清理 .bak 文件再保存。",
            MaxBackupSuffix);
        return false;
    }

    private static void WriteScriptFile(string path, string normalizedContent, string newLine)
    {
        var text = string.Equals(newLine, "\n", StringComparison.Ordinal)
            ? normalizedContent
            : normalizedContent.Replace("\n", newLine, StringComparison.Ordinal);

        // UTF-8 无 BOM：kOS 的读取器与文本 diff 都不需要 BOM。
        File.WriteAllText(path, text, Utf8NoBom);
    }

    private static string DetectFileNewLine(string path)
    {
        try
        {
            var text = Decode(ReadAllBytesShared(path), out _);
            return DetectNewLine(text);
        }
        catch (Exception ex) when (IsRecoverable(ex))
        {
            // 读不到旧文件时按 LF 写：此时备份多半也失败了，不会走到这里；兜底只是保证方法不抛。
            return "\n";
        }
    }

    private static string DetectNewLine(string text) =>
        text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

    private static string NormalizeToLf(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

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

        // "a\n" 是 1 行（末尾换行只结束最后一行），"a\nb" 是 2 行。
        return normalizedText[^1] == '\n' ? newlines : newlines + 1;
    }

    private static bool IsBootScript(string name) =>
        string.Equals(name, BootScriptName, StringComparison.OrdinalIgnoreCase);

    private static byte[] ReadAllBytesShared(string path)
    {
        // FileShare.ReadWrite：游戏进程可能正开着同一个脚本，读取不能让出共享位就只会显示“被占用”。
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 4096,
            FileOptions.SequentialScan);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static string ReadTextShared(string path) => Decode(ReadAllBytesShared(path), out _);

    private static string Decode(byte[] bytes, out bool hadBom)
    {
        hadBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        // throwOnInvalidBytes: false —— 坏编码的脚本按替换字符读出来，至少还能在编辑器里看到并修。
        return hadBom
            ? Utf8NoBom.GetString(bytes, 3, bytes.Length - 3)
            : Utf8NoBom.GetString(bytes);
    }

    private static DateTime NormalizeUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };

    private static string FormatBytes(long sizeBytes) =>
        sizeBytes.ToString("N0", CultureInfo.InvariantCulture) + " 字节";

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (IsRecoverable(ex))
        {
            // 临时文件清理失败不能掩盖真正的保存错误：真正的问题已经由调用方转成失败文案了。
        }
    }

    private static bool IsRecoverable(Exception ex) =>
        ex is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException
            or SecurityException
            or InvalidOperationException;
}
