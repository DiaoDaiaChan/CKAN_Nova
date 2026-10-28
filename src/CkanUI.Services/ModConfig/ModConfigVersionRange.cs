using CKAN.Versioning;

namespace CkanUI.Services.ModConfig;

/// <summary>一条版本约束，例如 <c>&gt;=1.30</c> 解析成 (Op: "&gt;=", Version: "1.30")。</summary>
/// <param name="Operator">比较运算符：<c>&gt;=</c> <c>&lt;=</c> <c>&gt;</c> <c>&lt;</c> <c>=</c>（<c>==</c> 归一成 <c>=</c>）。</param>
/// <param name="Version">版本号原文（已通过 ModuleVersion 校验可解析）。</param>
public readonly record struct ModConfigVersionConstraint(string Operator, string Version)
{
    /// <summary>回写成 adapter.json 里的原始字符串。</summary>
    public override string ToString() => Operator + Version;
}

/// <summary>
/// adapter.json 里 <c>modVersionRange</c> 的解析与判定。
/// <para>
/// 语义：数组里所有约束同时成立才算"支持该版本"（AND），区间按 [min, max) 使用（§4.1）。
/// 之所以单独放在这里而不是塞进加载器，是因为状态机（CFG-03）要在**不读 adapter 文件**的纯函数里判定越界，
/// 加载器和状态机都需要同一套语义，语义只能有一份实现。
/// </para>
/// </summary>
public static class ModConfigVersionRange
{
    /// <summary>合法的运算符，长的在前（保证 "&gt;=" 不被误判成 "&gt;"）。</summary>
    private static readonly string[] KnownOperators = [">=", "<=", "==", ">", "<", "="];

    /// <summary>解析单条约束。<paramref name="text"/> 为空、运算符缺失或版本号非法都返回 false。</summary>
    public static bool TryParseConstraint(
        string? text,
        out ModConfigVersionConstraint constraint,
        out string? error)
    {
        constraint = default;
        error = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            error = "版本约束为空";
            return false;
        }

        var trimmed = text.Trim();
        foreach (var candidate in KnownOperators)
        {
            if (!trimmed.StartsWith(candidate, StringComparison.Ordinal))
            {
                continue;
            }

            var versionText = trimmed[candidate.Length..].Trim();
            if (versionText.Length == 0)
            {
                error = $"版本约束「{trimmed}」缺少版本号";
                return false;
            }

            if (!IsModuleVersion(versionText))
            {
                error = $"版本约束「{trimmed}」里的版本号「{versionText}」不是合法版本";
                return false;
            }

            // "==" 归一成 "="，避免后面判定时出现两套写法。
            var op = candidate == "==" ? "=" : candidate;
            constraint = new ModConfigVersionConstraint(op, versionText);
            return true;
        }

        // 不带运算符的裸版本按"等于"处理（等价于 =1.30），比直接报错友好。
        if (IsModuleVersion(trimmed))
        {
            constraint = new ModConfigVersionConstraint("=", trimmed);
            return true;
        }

        error = $"版本约束「{trimmed}」缺少运算符（应为 >=、<=、>、< 或 =）";
        return false;
    }

    /// <summary>解析整条区间。空集合是合法的（表示不限制版本）。</summary>
    public static bool TryParseAll(
        IReadOnlyList<string>? constraints,
        out IReadOnlyList<ModConfigVersionConstraint> parsed,
        out string? error)
    {
        parsed = [];
        error = null;

        if (constraints is null || constraints.Count == 0)
        {
            return true;
        }

        var list = new List<ModConfigVersionConstraint>(constraints.Count);
        foreach (var raw in constraints)
        {
            if (!TryParseConstraint(raw, out var constraint, out var itemError))
            {
                error = itemError;
                return false;
            }

            list.Add(constraint);
        }

        parsed = list;
        return true;
    }

    /// <summary>
    /// 判定已装版本是否落在区间内。
    /// <para>
    /// <paramref name="installedVersion"/> 为 null/空白时无法判定：返回 false 且 <paramref name="satisfied"/> 为 false，
    /// 由调用方决定降级策略（本项目的做法是"按支持处理但记一条警告"，见 ModConfigDetector）。
    /// </para>
    /// </summary>
    public static bool TryIsSatisfied(
        IReadOnlyList<string>? constraints,
        string? installedVersion,
        out bool satisfied,
        out string? error)
    {
        satisfied = false;
        error = null;

        if (!TryParseAll(constraints, out var parsed, out error))
        {
            return false;
        }

        if (parsed.Count == 0)
        {
            satisfied = true;
            return true;
        }

        if (string.IsNullOrWhiteSpace(installedVersion))
        {
            error = "已安装版本未知，无法判定版本区间";
            return false;
        }

        if (!IsModuleVersion(installedVersion))
        {
            error = $"已安装版本「{installedVersion}」不是合法版本，无法判定版本区间";
            return false;
        }

        var installed = new ModuleVersion(installedVersion.Trim());
        foreach (var constraint in parsed)
        {
            var bound = new ModuleVersion(constraint.Version);
            var compare = installed.CompareTo(bound);
            var ok = constraint.Operator switch
            {
                ">=" => compare >= 0,
                "<=" => compare <= 0,
                ">" => compare > 0,
                "<" => compare < 0,
                _ => compare == 0,
            };

            if (!ok)
            {
                satisfied = false;
                return true;
            }
        }

        satisfied = true;
        return true;
    }

    /// <summary>
    /// 把区间渲染成给人看的短文本，用于越界横幅（§4.3 要求横幅里出现"≥1.30, &lt;2.0"这种写法）。
    /// 放在 Services 而不是 VM：VM 不引用 Core，不持有 ModuleVersion 语义，只该拿现成字符串。
    /// </summary>
    public static string FormatForDisplay(IReadOnlyList<string>? constraints)
    {
        if (constraints is null || constraints.Count == 0)
        {
            return "不限";
        }

        var parts = new List<string>(constraints.Count);
        foreach (var raw in constraints)
        {
            if (!TryParseConstraint(raw, out var constraint, out _))
            {
                parts.Add(raw ?? string.Empty);
                continue;
            }

            var symbol = constraint.Operator switch
            {
                ">=" => "≥",
                "<=" => "≤",
                ">" => ">",
                "<" => "<",
                _ => "=",
            };
            parts.Add(symbol + constraint.Version);
        }

        return string.Join(", ", parts);
    }

    /// <summary>
    /// 判断一段文本是否像"版本号"。
    /// </summary>
    /// <remarks>
    /// 为什么不能只靠 <see cref="ModuleVersion"/> 的构造：它对任意字符串都不抛异常
    /// （"abc"、"about" 都能构造出来），拿它当合法性判据等于没有校验，
    /// adapter 里写着 "&gt;=not-a-version" 也会被当成合法区间。
    /// 所以先过一层语法闸门：可选 epoch 前缀 "n:"，然后必须数字开头，且只允许版本号里会出现的字符。
    /// </remarks>
    private static bool IsModuleVersion(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var value = text.Trim();
        var index = 0;
        var colon = value.IndexOf(':', StringComparison.Ordinal);
        if (colon > 0)
        {
            for (var i = 0; i < colon; i++)
            {
                if (!char.IsAsciiDigit(value[i]))
                {
                    return false;
                }
            }

            index = colon + 1;
        }

        if (index >= value.Length || !char.IsAsciiDigit(value[index]))
        {
            return false;
        }

        for (var i = index; i < value.Length; i++)
        {
            var ch = value[i];
            if (!char.IsAsciiDigit(ch) && !char.IsAsciiLetter(ch) && ch is not '.' and not '-' and not '_' and not '+')
            {
                return false;
            }
        }

        try
        {
            _ = new ModuleVersion(value);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
