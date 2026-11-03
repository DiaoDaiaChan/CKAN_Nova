using System.Linq;
using System.Text;

using CkanUI.Services.Abstractions.Security;

namespace CkanUI.Plugin.Ai.Tests.Support;

/// <summary>
/// 记号保护器：把明文倒序加前缀。既不依赖 Windows DPAPI（沙箱/CI 上跑得动），
/// 又能证明「落盘的是保护器的输出」——磁盘上如果出现明文，说明有代码绕过了保护器。
/// </summary>
internal sealed class MarkerSecretProtector : ISecretProtector
{
    /// <summary>保护器前缀。</summary>
    public const string Prefix = "enc:";

    /// <inheritdoc />
    public string? Protect(string? plainText)
        => string.IsNullOrEmpty(plainText) ? plainText : Prefix + new string(plainText.Reverse().ToArray());

    /// <inheritdoc />
    public string? Unprotect(string? protectedText)
    {
        if (string.IsNullOrEmpty(protectedText) || !protectedText.StartsWith(Prefix, System.StringComparison.Ordinal))
        {
            return null;
        }

        return new string(protectedText[Prefix.Length..].Reverse().ToArray());
    }

    /// <summary>取出保护器写入磁盘的记号（测试里断言存在）。</summary>
    /// <param name="plainText">明文。</param>
    /// <returns>期望落在磁盘上的字符串。</returns>
    public static string Expect(string plainText) => Prefix + new string(plainText.Reverse().ToArray());
}
