using CkanUI.Services.Abstractions.Security;

namespace CkanUI.Services.Security;

/// <summary>用 Windows DPAPI（当前用户）保护短文本。</summary>
public sealed class DpapiSecretProtector : ISecretProtector
{
    /// <inheritdoc />
    public string? Protect(string? plainText) => DpapiSecret.Protect(plainText);

    /// <inheritdoc />
    public string? Unprotect(string? protectedText) => DpapiSecret.Unprotect(protectedText);
}
