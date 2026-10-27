namespace CkanUI.Sfs.Cli;

/// <summary>format 子命令的解析结果。</summary>
internal sealed record SfsFormatCommand(
    string FilePath,
    bool Write,
    bool Check,
    bool Diff,
    string? OutPath,
    SfsFormatOptions Options);

/// <summary>validate 子命令的解析结果。</summary>
internal sealed record SfsValidateCommand(string FilePath);

/// <summary>
/// 手写参数解析（CPM 冻结、不引入 System.CommandLine）。
/// 支持 <c>--name value</c> 与 <c>--name=value</c> 两种写法；未知选项、冲突组合、缺值都是用法错误（退出码 2）。
/// </summary>
internal static class SfsCliArguments
{
    public static SfsFormatCommand? ParseFormat(IReadOnlyList<string> args, int start, out string? error)
    {
        error = null;
        string? filePath = null;
        string? outPath = null;
        var write = false;
        var check = false;
        var diff = false;
        var stripBom = false;
        var verify = true;
        var newLine = SfsNewLineMode.Auto;
        var indent = SfsIndentMode.Tab;
        var indentWidth = 1;
        var blank = SfsBlankLineMode.Keep;

        for (var index = start; index < args.Count; index++)
        {
            var argument = args[index];
            if (argument.Length == 0)
            {
                continue;
            }

            if (!argument.StartsWith("--", StringComparison.Ordinal))
            {
                if (filePath is not null)
                {
                    error = $"多余的位置参数：{argument}";
                    return null;
                }

                filePath = argument;
                continue;
            }

            var separator = argument.IndexOf('=', StringComparison.Ordinal);
            var name = separator < 0 ? argument[2..] : argument[2..separator];
            var inlineValue = separator < 0 ? null : argument[(separator + 1)..];

            switch (name)
            {
                case "write":
                    write = true;
                    break;
                case "check":
                    check = true;
                    break;
                case "diff":
                    diff = true;
                    break;
                case "strip-bom":
                    stripBom = true;
                    break;
                case "no-verify":
                    verify = false;
                    break;
                case "out":
                    if (!TryTakeValue(args, ref index, name, inlineValue, out outPath, out error))
                    {
                        return null;
                    }

                    break;
                case "eol":
                    if (!TryTakeValue(args, ref index, name, inlineValue, out var eolValue, out error))
                    {
                        return null;
                    }

                    switch (eolValue)
                    {
                        case "auto":
                            newLine = SfsNewLineMode.Auto;
                            break;
                        case "lf":
                            newLine = SfsNewLineMode.Lf;
                            break;
                        case "crlf":
                            newLine = SfsNewLineMode.CrLf;
                            break;
                        default:
                            error = $"--eol 只接受 auto|lf|crlf，收到 {eolValue}";
                            return null;
                    }

                    break;
                case "indent":
                    if (!TryTakeValue(args, ref index, name, inlineValue, out var indentValue, out error))
                    {
                        return null;
                    }

                    switch (indentValue)
                    {
                        case "tab":
                            indent = SfsIndentMode.Tab;
                            indentWidth = 1;
                            break;
                        case "2":
                            indent = SfsIndentMode.Spaces;
                            indentWidth = 2;
                            break;
                        case "4":
                            indent = SfsIndentMode.Spaces;
                            indentWidth = 4;
                            break;
                        default:
                            error = $"--indent 只接受 tab|2|4，收到 {indentValue}";
                            return null;
                    }

                    break;
                case "blank":
                    if (!TryTakeValue(args, ref index, name, inlineValue, out var blankValue, out error))
                    {
                        return null;
                    }

                    switch (blankValue)
                    {
                        case "keep":
                            blank = SfsBlankLineMode.Keep;
                            break;
                        case "collapse":
                            blank = SfsBlankLineMode.Collapse;
                            break;
                        case "strip":
                            blank = SfsBlankLineMode.Strip;
                            break;
                        default:
                            error = $"--blank 只接受 keep|collapse|strip，收到 {blankValue}";
                            return null;
                    }

                    break;
                default:
                    error = $"未知选项：--{name}";
                    return null;
            }
        }

        if (filePath is null)
        {
            error = "format 需要一个文件路径";
            return null;
        }

        if (write && check)
        {
            error = "--write 与 --check 互斥（--check 只判断不写盘）";
            return null;
        }

        if (write && outPath is not null)
        {
            error = "--write 与 --out 互斥（--out 写到另一个文件）";
            return null;
        }

        if (check && outPath is not null)
        {
            error = "--check 与 --out 互斥";
            return null;
        }

        var options = new SfsFormatOptions
        {
            NewLine = newLine,
            Indent = indent,
            IndentWidth = indentWidth,
            BlankLines = blank,
            StripBom = stripBom,
            VerifySemantics = verify,
        };

        return new SfsFormatCommand(filePath, write, check, diff, outPath, options);
    }

    public static SfsValidateCommand? ParseValidate(IReadOnlyList<string> args, int start, out string? error)
    {
        error = null;
        string? filePath = null;

        for (var index = start; index < args.Count; index++)
        {
            var argument = args[index];
            if (argument.Length == 0)
            {
                continue;
            }

            if (argument.StartsWith("--", StringComparison.Ordinal))
            {
                error = $"未知选项：{argument}";
                return null;
            }

            if (filePath is not null)
            {
                error = $"多余的位置参数：{argument}";
                return null;
            }

            filePath = argument;
        }

        if (filePath is null)
        {
            error = "validate 需要一个文件路径";
            return null;
        }

        return new SfsValidateCommand(filePath);
    }

    private static bool TryTakeValue(
        IReadOnlyList<string> args,
        ref int index,
        string name,
        string? inlineValue,
        out string value,
        out string? error)
    {
        error = null;
        if (!string.IsNullOrEmpty(inlineValue))
        {
            value = inlineValue;
            return true;
        }

        if (index + 1 < args.Count)
        {
            index++;
            value = args[index];
            return true;
        }

        value = string.Empty;
        error = $"--{name} 需要一个值";
        return false;
    }
}
