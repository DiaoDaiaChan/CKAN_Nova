namespace CkanUI.ViewModels.Pages.Settings;

/// <summary>
/// 认证令牌脱敏（12 篇 F-11：脱敏显示；16 篇 H-10：令牌明文不进日志）。
/// 只露前 4 后 4，中间固定 4 个星号（星号数量不反映真实长度，避免泄漏长度信息）；
/// 长度不足 9 位时整串打星，短令牌不会因为「前 4 后 4」而基本全露。
/// </summary>
public static class AuthTokenMasking
{
    /// <summary>两端各露出的字符数。</summary>
    public const int VisibleEdgeLength = 4;

    /// <summary>未配置令牌时的占位文案。</summary>
    public const string EmptyPlaceholder = "（未设置）";

    /// <summary>脱敏后的展示文本。</summary>
    public static string Mask(string? token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return EmptyPlaceholder;
        }

        if (token.Length <= VisibleEdgeLength * 2)
        {
            return new string('*', token.Length);
        }

        return string.Concat(token.AsSpan(0, VisibleEdgeLength), "****", token.AsSpan(token.Length - VisibleEdgeLength));
    }

    /// <summary>是否已配置令牌。</summary>
    public static bool IsConfigured(string? token) => !string.IsNullOrEmpty(token);
}
