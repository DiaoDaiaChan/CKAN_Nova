using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

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
  ckanui-sfs outline <file> [--json] [--limit <n>]
  ckanui-sfs search <file> <查询词> [--json] [--limit <n>]
  ckanui-sfs set <file> <行号> <新值> [--write | --out <path>] [--print]
  ckanui-sfs delete <file> <行号> [--write | --out <path>]
  ckanui-sfs duplicate <file> <行号> [--write | --out <path>]
  ckanui-sfs insert <file> <块所在行号> <键> <值> [--write | --out <path>]

说明:
  format 默认只把规范形式打到 stdout，不改盘；--write 原子写回（保留原修改时间，先留 .sfsbak 快照）；
         --check 只判断，有差异退出码 1；--out 写到另一个文件。
  validate 输出带行号/列号/严重级别的诊断，无错误且无警告退出 0，否则 1。
  outline / search 只读：outline 给分区大纲（常用内容在前，未知键原样保留），search 按路径/键/标签/值匹配。
  set/delete/duplicate/insert 是行级结构化编辑：只改目标行，写盘前自校验块数/键值数/键值多重集增量；
         默认不写盘，--write 原子写回（先留 .sfsbak），--out 另存；自校验不通过拒绝写盘（退出码 1）。

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
            "outline" => RunOutline(args, output, error),
            "search" => RunSearch(args, output, error),
            "set" or "delete" or "duplicate" or "insert" => RunEdit(args[0], args, output, error),
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
        output.WriteLine("ckanui-sfs 0.2.0 (SFS-01/02/03 + outline/search/edit)");
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

    private static int RunOutline(string[] args, TextWriter output, TextWriter error)
    {
        var command = SfsCliArguments.ParseOutline(args, 1, out var argumentError);
        if (command is null)
        {
            error.WriteLine(argumentError);
            error.WriteLine(Usage);
            return 2;
        }

        var document = Load(command.FilePath, error, out var exitCode);
        if (document is null)
        {
            return exitCode;
        }

        var hasErrors = ReportParseErrors(document, error);
        var outline = SfsOutline.Build(document);
        if (command.Json)
        {
            WriteJson(output, new
            {
                file = Path.GetFullPath(command.FilePath),
                snapshot = outline.Snapshot,
                sections = outline.Sections.Select(section => new
                {
                    section.Id,
                    section.Title,
                    section.Subtitle,
                    kind = section.Kind.ToString(),
                    section.Rank,
                    section.Truncated,
                    section.TotalPairCount,
                    section.TotalNodeCount,
                    fields = section.Fields.Take(command.Limit).Select(field => new
                    {
                        field.Line,
                        field.Key,
                        field.Label,
                        field.Value,
                        kind = field.Kind.ToString(),
                        field.Priority,
                        field.IsKnown,
                        field.Path,
                    }),
                    nodes = section.Nodes.Take(command.Limit).Select(node => new
                    {
                        node.Line,
                        node.Name,
                        node.Label,
                        node.Path,
                        node.PairCount,
                        node.NodeCount,
                        node.Summary,
                    }),
                }),
            });
        }
        else
        {
            WriteOutline(outline, command.Limit, output);
        }

        return hasErrors ? 1 : 0;
    }

    private static int RunSearch(string[] args, TextWriter output, TextWriter error)
    {
        var command = SfsCliArguments.ParseSearch(args, 1, out var argumentError);
        if (command is null)
        {
            error.WriteLine(argumentError);
            error.WriteLine(Usage);
            return 2;
        }

        var document = Load(command.FilePath, error, out var exitCode);
        if (document is null)
        {
            return exitCode;
        }

        var hasErrors = ReportParseErrors(document, error);
        var outline = SfsOutline.Build(document);
        var result = outline.Search(command.Query, command.Limit);
        if (command.Json)
        {
            WriteJson(output, new
            {
                query = command.Query,
                total = result.Total,
                fields = result.Fields.Select(field => new
                {
                    field.Line,
                    field.Key,
                    field.Label,
                    field.Value,
                    field.Path,
                    sectionId = field.SectionId,
                }),
                nodes = result.Nodes.Select(node => new
                {
                    node.Line,
                    node.Name,
                    node.Label,
                    node.Path,
                    node.Summary,
                }),
            });
        }
        else if (result.Total == 0)
        {
            output.WriteLine($"没有匹配 “{command.Query}” 的键或块");
        }
        else
        {
            foreach (var field in result.Fields)
            {
                output.WriteLine($"第 {field.Line} 行  {KeyText(field.Label, field.Key)} = {field.Value}   [{field.Path}]");
            }

            foreach (var node in result.Nodes)
            {
                output.WriteLine($"第 {node.Line} 行  块 {node.Name}（{node.Label}）   [{node.Path}]");
            }

            output.WriteLine($"共 {result.Total} 条");
        }

        return hasErrors ? 1 : 0;
    }

    private static int RunEdit(string commandName, string[] args, TextWriter output, TextWriter error)
    {
        var command = SfsCliArguments.ParseEdit(commandName, args, 1, out var argumentError);
        if (command is null)
        {
            error.WriteLine(argumentError);
            error.WriteLine(Usage);
            return 2;
        }

        DateTime lastWriteUtc;
        SfsDocument? document;
        try
        {
            lastWriteUtc = File.GetLastWriteTimeUtc(command.FilePath);
            var parsed = SfsParser.ParseFile(command.FilePath);
            document = parsed.Document;
            if (document is null)
            {
                error.WriteLine("解析失败：");
                foreach (var diagnostic in parsed.Diagnostics)
                {
                    error.WriteLine(diagnostic.ToString());
                }

                return 1;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            error.WriteLine($"读取失败：{exception.Message}");
            return 2;
        }

        if (ReportParseErrors(document, error))
        {
            error.WriteLine("源文件有解析错误，拒绝编辑");
            return 1;
        }

        var result = command.Kind switch
        {
            SfsEditKind.Set => SfsEditor.SetValue(document, command.Line, command.Value!),
            SfsEditKind.Delete => SfsEditor.DeleteElement(document, command.Line),
            SfsEditKind.Duplicate => SfsEditor.DuplicateElement(document, command.Line),
            SfsEditKind.Insert => SfsEditor.InsertPair(document, command.Line, command.Key!, command.Value!),
            _ => throw new InvalidOperationException($"未知编辑类型：{command.Kind}"),
        };

        error.WriteLine(result.Summary);
        foreach (var issue in result.Issues)
        {
            error.WriteLine($"  ! {issue}");
        }

        if (!result.Changed)
        {
            return 1;
        }

        if (!result.Verified)
        {
            error.WriteLine("自校验未通过，拒绝写盘（原文件未改动）");
            return 1;
        }

        if (command.Print)
        {
            output.Write(result.Text);
            return 0;
        }

        if (command.OutPath is { } outPath)
        {
            try
            {
                WriteTextAtomic(outPath, result.Text, preserveTimesOf: null);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                error.WriteLine($"写入失败：{exception.Message}");
                return 2;
            }

            output.WriteLine($"已写入 {outPath}");
            return 0;
        }

        if (!command.Write)
        {
            error.WriteLine("未写盘（加 --write 或 --out <路径>，或 --print 输出全文）");
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

        output.WriteLine($"已写回 {command.FilePath}");
        return 0;
    }

    private static SfsDocument? Load(string path, TextWriter error, out int exitCode)
    {
        exitCode = 0;
        if (!File.Exists(path))
        {
            error.WriteLine($"找不到文件：{path}");
            exitCode = 2;
            return null;
        }

        try
        {
            var parsed = SfsParser.ParseFile(path);
            if (parsed.Document is null)
            {
                error.WriteLine("解析失败：");
                foreach (var diagnostic in parsed.Diagnostics)
                {
                    error.WriteLine(diagnostic.ToString());
                }

                exitCode = 1;
                return null;
            }

            return parsed.Document;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            error.WriteLine($"读取失败：{exception.Message}");
            exitCode = 2;
            return null;
        }
    }

    private static bool ReportParseErrors(SfsDocument document, TextWriter error)
    {
        var hasErrors = false;
        foreach (var diagnostic in document.ParseDiagnostics)
        {
            if (diagnostic.Severity != SfsSeverity.Error)
            {
                continue;
            }

            hasErrors = true;
            error.WriteLine(diagnostic.ToString());
        }

        return hasErrors;
    }

    private static void WriteOutline(SfsOutline outline, int limit, TextWriter output)
    {
        var snapshot = outline.Snapshot;
        output.WriteLine(
            $"存档 {snapshot.SaveName ?? "（未命名）"} · 版本 {snapshot.Version ?? "?"} · 模式 {snapshot.Mode ?? "?"}");
        output.WriteLine(
            $"资金 {FieldText(snapshot.Funds)} · 科技 {FieldText(snapshot.Science)} · 声望 {FieldText(snapshot.Reputation)}"
            + $" · 游戏时间 {FieldText(snapshot.GameTime)}");
        output.WriteLine(
            $"规模：飞行器 {snapshot.VesselCount} · 部件 {snapshot.PartCount} · 场景 {snapshot.ScenarioCount}"
            + $" · 乘员 {snapshot.CrewCount} · 键值 {snapshot.PairCount} · 块 {snapshot.NodeCount} · 行 {snapshot.LineCount}"
            + (snapshot.Truncated ? "（已截断）" : string.Empty));
        output.WriteLine();

        foreach (var section in outline.Sections)
        {
            var subtitle = string.IsNullOrEmpty(section.Subtitle) || section.Subtitle == section.Title
                ? string.Empty
                : $"（{section.Subtitle}）";
            output.WriteLine($"[{section.Title}]{subtitle}  键值 {section.TotalPairCount} · 块 {section.TotalNodeCount}");

            var printed = 0;
            foreach (var field in section.Fields)
            {
                if (printed >= limit)
                {
                    output.WriteLine($"  … 还有 {section.Fields.Count - printed} 项（--limit 调整）");
                    break;
                }

                output.WriteLine($"  {field.Line,6}  {KeyText(field.Label, field.Key)} = {field.Value}");
                printed++;
            }

            printed = 0;
            foreach (var node in section.Nodes)
            {
                if (printed >= limit)
                {
                    output.WriteLine($"  … 还有 {section.Nodes.Count - printed} 个块（--limit 调整）");
                    break;
                }

                var summary = string.IsNullOrEmpty(node.Summary) ? string.Empty : $" · {node.Summary}";
                output.WriteLine($"  {node.Line,6}  ▸ {node.Name}（{node.Label}）{summary}");
                printed++;
            }

            if (section.Truncated)
            {
                output.WriteLine("  （该分区超出预算，已截断）");
            }

            output.WriteLine();
        }
    }

    private static string FieldText(SfsOutlineField? field)
        => field is null
            ? "–"
            : field.Unit == "√"
                ? $"√{field.Value}"
                : field.Value + (string.IsNullOrEmpty(field.Unit) ? string.Empty : field.Unit);

    private static string KeyText(string label, string key)
        => string.Equals(label, key, StringComparison.Ordinal) ? key : $"{label}（{key}）";

    private static void WriteJson(TextWriter output, object payload)
        => output.WriteLine(JsonSerializer.Serialize(payload, JsonOptions));

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

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
