namespace CkanUI.Services.Abstractions.Security;

/// <summary>
/// 把短文本（API Key 之类）加密成可落盘的字符串。
/// 为什么放在抽象层：视图模型不能依赖 Windows 实现，但「保存前必须加密」这条纪律要由界面层来执行。
/// </summary>
public interface ISecretProtector
{
    /// <summary>加密；返回 null 表示无法安全保存（界面应提示且不落盘）。</summary>
    string? Protect(string? plainText);

    /// <summary>解密；返回 null 表示解不开（换了机器/用户，或内容损坏）。</summary>
    string? Unprotect(string? protectedText);
}

/// <summary>明文直通实现，只给测试和「没有平台加密」的场景用。</summary>
public sealed class PassthroughSecretProtector : ISecretProtector
{
    public string? Protect(string? plainText) => plainText;

    public string? Unprotect(string? protectedText) => protectedText;
}
