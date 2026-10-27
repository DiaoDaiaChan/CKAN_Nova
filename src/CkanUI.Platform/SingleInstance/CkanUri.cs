using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace CkanUI.Platform.SingleInstance;

/// <summary>一条从 <c>ckan://</c> URI 解析出的命令（ARC-05）。</summary>
/// <param name="Kind">命令类别，统一小写：<c>mod</c> / <c>instance</c> / <c>repo</c>。</param>
/// <param name="Value">命令参数（已做 URL 解码），例如模块 Identifier 或实例名。</param>
public sealed record CkanUriCommand(string Kind, string Value);

/// <summary>
/// <c>ckan://</c> 协议 URI 的解析与生成（ARC-05）。
/// 纯函数、零 Avalonia 依赖，可被单实例转发与命令行两条入口共用。
/// </summary>
public static class CkanUri
{
    /// <summary>协议 scheme。</summary>
    public const string Scheme = "ckan";

    private static readonly HashSet<string> Kinds =
        new(StringComparer.OrdinalIgnoreCase) { "mod", "instance", "repo" };

    /// <summary>
    /// 解析 <c>ckan://&lt;kind&gt;/&lt;value&gt;</c>。
    /// 带 query、fragment、多余路径段、未知 kind、空 value 一律视为非法并返回 false。
    /// </summary>
    public static bool TryParse(string? raw, [NotNullWhen(true)] out CkanUriCommand? command)
    {
        command = null;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        if (!Uri.TryCreate(raw.Trim(), UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, Scheme, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // query / fragment 没有语义，出现即说明调用方拼错了，宁可拒绝也不要静默丢弃
        if (uri.Query.Length > 0 || uri.Fragment.Length > 0)
        {
            return false;
        }

        var kind = uri.Host;
        if (!Kinds.Contains(kind))
        {
            return false;
        }

        var path = uri.AbsolutePath.Trim('/');
        if (path.Length == 0 || path.Contains('/', StringComparison.Ordinal))
        {
            return false;
        }

        string value;
        try
        {
            value = Uri.UnescapeDataString(path);
        }
        catch (UriFormatException)
        {
            return false;
        }

        // 解码后为空或全是空白（"%20"）都没有语义，按非法处理。
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        command = new CkanUriCommand(kind.ToLowerInvariant(), value);
        return true;
    }

    /// <summary>把命令还原成 <c>ckan://</c> URI（与 <see cref="TryParse"/> 互为逆运算）。</summary>
    public static string Build(CkanUriCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return $"{Scheme}://{command.Kind.ToLowerInvariant()}/{Uri.EscapeDataString(command.Value)}";
    }
}
