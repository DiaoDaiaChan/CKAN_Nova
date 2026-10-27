using System.Text;

namespace CkanUI.Sfs.Cli;

/// <summary>
/// CLI 命令实现（可测试入口：<see cref="Run"/> 只碰传入的 <see cref="TextWriter"/>，不碰 Console）。
/// 退出码约定：0 成功 / 无问题，1 发现问题，2 用法或 IO 错误。
/// </summary>
public static class SfsCli
{
    private const int MaxDiffLines = 20;

    private const string Usage = """
用法:
  ckanui-sfs format <file> [--write | --check] [--out <path>] [--eol=auto|lf|crlf]
                        [--indent=tab|2|4] [--blank=keep|collapse|strip]
                        [--diff] [--strip-bom] [--no-verify]
  ckanui-sfs validate <file>

说明:
  format 默认只把规范形式打到 stdout，不改盘；--write 原子写回（保留原修改时间，先留 .sfsbak 快照）；
         --check 只判断，有差异退出码 1；--out 写到另一个文件。
  validate 输出带行号/列号/严重级别的诊断，无错误且无警告退出 0，否则 1。

退出码: 0 成功/无问题  1 发现问题  2 用法错误
""";

    /// <summary>执行一次 CLI 调用。</summary>
    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        if (args.Length == 0)
        {
            error.WriteLine(Usage);
            return 2;
        }

        return args[0] switch
        {
            "format" => RunFormat(args, output, error),
            "validate" => RunValidate(args, output, error),
            "help" or "--help" or "-h" => PrintUsage(output),
            "--version" => PrintVersion(output),
            _ => Unknown(args[0], error),
        };
    }

    private static int PrintUsage(TextWriter output)
    {
        output.WriteLine(Usage);
        return 0;
    }

    private static int PrintVersion(TextWriter output)
    {
        output.WriteLine("ckanui-sfs 0.1.0 (SFS-01/02/03)");
        return 0;
    }

    private static int Unknown(string command, TextWriter error)
    {
        error.WriteLine($"未知子命令：{command}");
        error.WriteLine(Usage);
        return 2;
    }

    private static int RunFormat(string[] args, TextWriter output, TextWriter error)
    {
        var command = SfsCliArguments.ParseFormat(args, 1, out var argumentError);
        if (command is null)
        {
            error.WriteLine(argumentError);
            error.WriteLine(Usage);
            return 2;
        }

        if (!File.Exists(command.FilePath))
        {
            error.WriteLine($"找不到文件：{command.FilePath}");
            return 2;
        }

        SfsParseResult parsed;
        DateTime lastWriteUtc;
        try
        {
            lastWriteUtc = File.GetLastWriteTimeUtc(command.FilePath);
            parsed = SfsParser.ParseFile(command.FilePath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            error.WriteLine($"读取失败：{exception.Message}");
            return 2;
        }

        if (parsed.Document is null)
        {
            error.WriteLine("解析失败：");
            foreach (var diagnostic in parsed.Diagnostics)
            {
                error.WriteLine(diagnostic.ToString());
            }

            return 1;
        }

        var document = parsed.Document;
        var hasParseErrors = false;
        foreach (var diagnostic in document.ParseDiagnostics)
        {
            if (diagnostic.Severity != SfsSeverity.Error)
            {
                continue;
            }

            hasParseErrors = true;
            error.WriteLine(diagnostic.ToString());
        }

        var result = SfsFormatter.Format(document, command.Options);
        if (result.Refused || hasParseErrors)
        {
            error.WriteLine($"拒绝格式化：{result.RefusalReason ?? "源文件存在解析错误"}");
            foreach (var diagnostic in result.Diagnostics)
            {
                error.WriteLine(diagnostic.ToString());
            }

            return 1;
        }

        if (command.Check)
        {
            if (!result.Changed)
            {
                output.WriteLine("已经是规范形式（无差异）");
                return 0;
            }

            error.WriteLine($"存在差异：{command.FilePath}（--check 不写盘）");
            WriteStats(result, error);
            if (command.Diff)
            {
                WriteDiff(document.SourceText, result.Text, output);
            }

            return 1;
        }

        if (command.OutPath is not null)
        {
            try
            {
                WriteTextAtomic(command.OutPath, result.Text, preserveTimesOf: null);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                error.WriteLine($"写入失败：{exception.Message}");
                return 2;
            }

            WriteStats(result, error);
            output.WriteLine($"已写入 {command.OutPath}");
            return 0;
        }

        if (!command.Write)
        {
            // 默认：不碰磁盘，把规范形式交给 stdout，人读信息走 stderr。
            output.Write(result.Text);
            WriteStats(result, error);
            return 0;
        }

        if (!result.Changed)
        {
            output.WriteLine("已经是规范形式，未改动文件");
            return 0;
        }

        try
        {
            var snapshot = CreateSnapshot(command.FilePath);
            if (snapshot is not null)
            {
                error.WriteLine($"已保存快照：{snapshot}");
            }

            WriteTextAtomic(command.FilePath, result.Text, lastWriteUtc);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            error.WriteLine($"写入失败：{exception.Message}");
            return 2;
        }

        WriteStats(result, error);
        output.WriteLine($"已写回 {command.FilePath}");
        return 0;
    }

    private static int RunValidate(string[] args, TextWriter output, TextWriter error)
    {
        var command = SfsCliArguments.ParseValidate(args, 1, out var argumentError);
        if (command is null)
        {
            error.WriteLine(argumentError);
            error.WriteLine(Usage);
            return 2;
        }

        if (!File.Exists(command.FilePath))
        {
            error.WriteLine($"找不到文件：{command.FilePath}");
            return 2;
        }

        SfsValidationResult result;
        try
        {
            result = SfsValidator.ValidateFile(command.FilePath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            error.WriteLine($"读取失败：{exception.Message}");
            return 2;
        }

        output.WriteLine(result.ToReport());
        return result.HasErrors || result.HasWarnings ? 1 : 0;
    }

    private static void WriteStats(SfsFormatResult result, TextWriter error)
    {
        var stats = result.Stats;
        error.WriteLine(
            $"行 {stats.Lines}；重写缩进 {stats.ReindentedLines}；规范分隔符 {stats.NormalizedSeparators}；"
            + $"统一行尾 {stats.NormalizedLineEndings}；删空行 {stats.BlankLinesRemoved}；"
            + $"拆分同行花括号 {stats.NodeHeadersSplit}；自校验 {(result.SelfCheckPassed ? "通过" : "未启用")}");
    }

    /// <summary>最近的差异片段（首个变动的 20 行），够人读，不追求完整 unified diff（口径见 §6.1）。</summary>
    private static void WriteDiff(string before, string after, TextWriter output)
    {
        var oldLines = SplitLines(before);
        var newLines = SplitLines(after);

        var prefix = 0;
        while (prefix < oldLines.Length && prefix < newLines.Length
            && string.Equals(oldLines[prefix], newLines[prefix], StringComparison.Ordinal))
        {
            prefix++;
        }

        var suffix = 0;
        while (suffix < oldLines.Length - prefix && suffix < newLines.Length - prefix
            && string.Equals(oldLines[oldLines.Length - 1 - suffix], newLines[newLines.Length - 1 - suffix], StringComparison.Ordinal))
        {
            suffix++;
        }

        output.WriteLine($"@@ 第 {prefix + 1} 行起 @@");
        var printed = 0;
        for (var index = prefix; index < oldLines.Length - suffix && printed < MaxDiffLines; index++, printed++)
        {
            output.WriteLine($"- {oldLines[index]}");
        }

        printed = 0;
        for (var index = prefix; index < newLines.Length - suffix && printed < MaxDiffLines; index++, printed++)
        {
            output.WriteLine($"+ {newLines[index]}");
        }
    }

    private static string[] SplitLines(string text)
    {
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        return normalized.Split('\n');
    }

    /// <summary>L9 写前快照：首次写盘前留一份 <c>&lt;file&gt;.sfsbak</c>，已存在则不动（保住最初的原始字节）。</summary>
    private static string? CreateSnapshot(string path)
    {
        var snapshot = path + ".sfsbak";
        if (File.Exists(snapshot))
        {
            return null;
        }

        File.Copy(path, snapshot, overwrite: false);
        return snapshot;
    }

    /// <summary>L8 原子写：同目录临时文件 + 覆盖移动，失败时原文件不动；<paramref name="preserveTimesOf"/> 非 null 时还原修改时间（L10）。</summary>
    private static void WriteTextAtomic(string path, string text, DateTime? preserveTimesOf)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrEmpty(directory))
        {
            directory = ".";
        }

        var tempPath = Path.Combine(directory, Path.GetFileName(fullPath) + ".tmp-" + Guid.NewGuid().ToString("N")[..8]);
        var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(text);
        try
        {
            File.WriteAllBytes(tempPath, bytes);
            File.Move(tempPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }

        if (preserveTimesOf is { } lastWriteUtc)
        {
            File.SetLastWriteTimeUtc(fullPath, lastWriteUtc);
        }
    }
}
