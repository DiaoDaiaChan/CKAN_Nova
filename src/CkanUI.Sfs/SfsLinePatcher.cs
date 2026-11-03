using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CkanUI.Sfs;

/// <summary>单行补丁的结果（大文件模式下不建语法树，改用“行替换 + 流式复核”）。</summary>
public sealed record SfsLinePatchResult
{
    public required bool Succeeded { get; init; }

    public required string Summary { get; init; }

    public IReadOnlyList<string> Issues { get; init; } = [];

    /// <summary>目标行替换前的原文（用于界面对比）。</summary>
    public string? OldLine { get; init; }

    /// <summary>目标行替换后的原文。</summary>
    public string? NewLine { get; init; }

    /// <summary>补丁落到的临时文件（同目录，供调用方复核后原子顶替）。</summary>
    public string? TempPath { get; init; }
}

/// <summary>
/// 大文件模式下的行级补丁：**逐字节原样复制，只替换目标行**。
/// 完整解析需要建几百万个对象，几百 MB 的整合包存档会直接把内存打爆；
/// 这里改成流式扫描定位 + 单行替换，写回再流式复核计数，内存恒定。
/// </summary>
public static class SfsLinePatcher
{
    /// <summary>可编辑行的字节上限：再大就说明这行是 mod 的数据块，不当键值处理。</summary>
    public const long MaxEditableLineBytes = 8L * 1024 * 1024;

    /// <summary>读取指定行的原文（不加载整份文件）。</summary>
    public static bool TryReadLine(string path, int lineNumber, out string content, out string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        content = string.Empty;
        error = string.Empty;
        if (lineNumber <= 0)
        {
            error = "行号必须从 1 开始";
            return false;
        }

        var found = false;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);
        using var reader = new StreamReader(stream, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true, bufferSize: 1 << 16);
        var index = 0;
        string? raw;
        while ((raw = reader.ReadLine()) is not null)
        {
            index++;
            if (index != lineNumber)
            {
                continue;
            }

            content = raw;
            found = true;
            break;
        }

        if (!found)
        {
            error = $"第 {lineNumber} 行超出文件范围";
        }

        return found;
    }

    /// <summary>
    /// 把第 <paramref name="lineNumber"/> 行改写成 <c>缩进 + 键 + 原分隔符 + 新值</c>，
    /// 其余字节（包括行尾风格、BOM、注释、未知块）原样复制到 <paramref name="tempPath"/>。
    /// </summary>
    public static SfsLinePatchResult ReplaceValue(string sourcePath, int lineNumber, string? newKey, string newValue, string tempPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(tempPath);
        ArgumentNullException.ThrowIfNull(newValue);

        if (!SfsEditor.TryValidateValue(newValue, out var valueError))
        {
            return Fail("值不合法：" + valueError);
        }

        if (!string.IsNullOrEmpty(newKey) && !SfsEditor.TryValidateKey(newKey, out var keyError))
        {
            return Fail("键名不合法：" + keyError);
        }

        var bounds = Locate(sourcePath, lineNumber);
        if (bounds is null)
        {
            return Fail($"第 {lineNumber} 行超出文件范围");
        }

        var targetLineBytes = bounds.Value.ContentEnd - bounds.Value.ContentStart;
        if (targetLineBytes > MaxEditableLineBytes)
        {
            return Fail($"第 {lineNumber} 行有 {targetLineBytes / 1024 / 1024} MB，是 mod 的数据块而不是键值对，拒绝按行改写");
        }

        var targetBytes = new byte[targetLineBytes];
        using (var read = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16))
        {
            read.Position = bounds.Value.ContentStart;
            read.ReadExactly(targetBytes);
        }

        var targetText = new UTF8Encoding(false, false).GetString(targetBytes);
        var token = SfsLexer.Lex(targetText);
        if (token.Kind != SfsLineKind.KeyValue)
        {
            return Fail($"第 {lineNumber} 行不是键值对，不能改值（是 {Describe(token.Kind)}）");
        }

        var key = string.IsNullOrEmpty(newKey) ? token.Key : newKey;
        if (string.Equals(key, token.Key, StringComparison.Ordinal) && string.Equals(newValue, token.Value, StringComparison.Ordinal))
        {
            return new SfsLinePatchResult { Succeeded = true, Summary = "值与原来相同，未修改", OldLine = targetText, NewLine = targetText };
        }

        var replacement = token.Indent + key + token.Separator + newValue;
        var replacementBytes = new UTF8Encoding(false).GetBytes(replacement);

        CopyWithSplice(sourcePath, tempPath, bounds.Value.ContentStart, bounds.Value.ContentEnd, replacementBytes);
        return new SfsLinePatchResult
        {
            Succeeded = true,
            Summary = $"第 {lineNumber} 行 {token.Key}: \"{token.Value}\" → \"{newValue}\"",
            OldLine = targetText,
            NewLine = replacement,
            TempPath = tempPath,
        };
    }

    /// <summary>按行号定位内容区间（不含行尾，行尾可能是 \r\n 或 \n）。找不到返回 null。</summary>
    private static Bounds? Locate(string path, int lineNumber)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);
        var buffer = new byte[1 << 16];

        var bom = 0;
        if (stream.Length >= 3)
        {
            var head = new byte[3];
            stream.ReadExactly(head);
            if (head[0] == 0xEF && head[1] == 0xBB && head[2] == 0xBF)
            {
                bom = 3;
            }

            stream.Position = 0;
        }

        var line = 1;
        long lineStart = bom;
        long position = bom;
        byte previous = 0;

        while (true)
        {
            stream.Position = position;
            var read = stream.Read(buffer, 0, buffer.Length);
            if (read <= 0)
            {
                break;
            }

            for (var i = 0; i < read; i++)
            {
                var value = buffer[i];
                if (value == (byte)'\n')
                {
                    var contentEnd = position + i - (previous == (byte)'\r' ? 1 : 0);
                    if (line == lineNumber)
                    {
                        return new Bounds(lineStart, contentEnd);
                    }

                    line++;
                    lineStart = position + i + 1;
                }

                previous = value;
            }

            position += read;
            if (line > lineNumber)
            {
                break;
            }
        }

        // 最后一行没有行尾：lineStart 还在文件内才算存在。
        if (line == lineNumber && lineStart < position)
        {
            return new Bounds(lineStart, position);
        }

        return null;
    }

    private static void CopyWithSplice(string sourcePath, string tempPath, long contentStart, long contentEnd, byte[] replacement)
    {
        using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);
        using var target = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16);
        var buffer = new byte[1 << 16];

        CopyRange(source, target, 0, contentStart, buffer);
        target.Write(replacement, 0, replacement.Length);
        CopyRange(source, target, contentEnd, source.Length, buffer);
        target.Flush();
    }

    private static void CopyRange(FileStream source, FileStream target, long start, long end, byte[] buffer)
    {
        source.Position = start;
        var remaining = end - start;
        while (remaining > 0)
        {
            var read = source.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
            if (read <= 0)
            {
                break;
            }

            target.Write(buffer, 0, read);
            remaining -= read;
        }
    }

    private static SfsLinePatchResult Fail(string summary) => new()
    {
        Succeeded = false,
        Summary = summary,
        Issues = new[] { summary },
    };

    private static string Describe(SfsLineKind kind) => kind switch
    {
        SfsLineKind.Blank => "空行",
        SfsLineKind.Comment => "注释",
        SfsLineKind.Name => "块名",
        SfsLineKind.OpenBrace => "左花括号",
        SfsLineKind.CloseBrace => "右花括号",
        _ => "其他",
    };

    private readonly record struct Bounds(long ContentStart, long ContentEnd);
}