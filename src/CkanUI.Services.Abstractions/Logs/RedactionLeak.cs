namespace CkanUI.Services.Abstractions.Logs;

/// <summary>
/// 一处漏检：种类 + 偏移 + 长度。定位足够排查，且不含原文。
/// </summary>
public sealed record RedactionLeak(RedactionLeakKind Kind, int Index, int Length)
{
    /// <summary>给用户/日志看的描述（不含命中内容）。</summary>
    public string Describe() => string.Create(
        System.Globalization.CultureInfo.InvariantCulture,
        $"{Kind.Text()}@{Index}+{Length}");
}

/// <summary>漏检种类的稳定文案（日志与单测断言共用，避免各处手写字符串）。</summary>
public static class RedactionLeakKinds
{
    /// <summary>种类的小写英文名。</summary>
    public static string Text(this RedactionLeakKind kind) => kind switch
    {
        RedactionLeakKind.UserName => "user-name",
        RedactionLeakKind.Token => "token",
        RedactionLeakKind.Email => "email",
        RedactionLeakKind.IpAddress => "ip",
        RedactionLeakKind.SteamId => "steamid",
        RedactionLeakKind.HostName => "host",
        RedactionLeakKind.SaveFile => "save",
        RedactionLeakKind.AbsolutePath => "path",
        _ => "unknown",
    };
}
