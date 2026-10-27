using System.Text;

namespace CkanUI.Sfs.Tests;

/// <summary>测试用的 SFS 片段（KSP 实测形状：CRLF、每层一个制表符、花括号独占一行）。</summary>
internal static class SfsSamples
{
    /// <summary>已经符合规范形式的存档片段：format 它必须逐字节不变。</summary>
    public const string CanonicalCrlf =
        "GAME\r\n"
        + "{\r\n"
        + "\tversion = 1.0\r\n"
        + "\tUT = 5005129.6190400701\r\n"
        + "\tSCENARIO\r\n"
        + "\t{\r\n"
        + "\t\tname = Default\r\n"
        + "\t}\r\n"
        + "}\r\n";

    /// <summary>同内容的 LF 版本。</summary>
    public static readonly string CanonicalLf = CanonicalCrlf.Replace("\r\n", "\n", StringComparison.Ordinal);

    /// <summary>一个"脏"存档：4 空格缩进、紧贴等号、同行花括号、空值、混合大小写。
    public const string Messy =
        "GAME\r\n"
        + "{\r\n"
        + "    version=1.0\r\n"
        + "  UT   =   5005129.6190400701\r\n"
        + "    linkURL = \r\n"
        + "    modded = True\r\n"
        + "    SCENARIO {\r\n"
        + "        name = Default\r\n"
        + "    }\r\n"
        + "}\r\n";

    /// <summary>Messy 的规范形式（4 空格缩进归零、分隔符统一成 " = "、同行花括号拆开、空值保留尾空格）。</summary>
    public const string CanonicalMessy =
        "GAME\r\n"
        + "{\r\n"
        + "\tversion = 1.0\r\n"
        + "\tUT = 5005129.6190400701\r\n"
        + "\tlinkURL = \r\n"
        + "\tmodded = True\r\n"
        + "\tSCENARIO\r\n"
        + "\t{\r\n"
        + "\t\tname = Default\r\n"
        + "\t}\r\n"
        + "}\r\n";

    /// <summary>用 UTF-8（不带 BOM）写出文件。</summary>
    public static void WriteFile(string path, string text)
        => File.WriteAllBytes(path, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(text));

    /// <summary>读回文件原始字节。</summary>
    public static byte[] ReadBytes(string path) => File.ReadAllBytes(path);

    /// <summary>构造 N 层嵌套（每层一个节点）。</summary>
    public static string Nested(int depth, string newLine)
    {
        var builder = new StringBuilder();
        for (var level = 0; level < depth; level++)
        {
            builder.Append('\t', level).Append("N").Append(level).Append(newLine);
            builder.Append('\t', level).Append("{").Append(newLine);
        }

        for (var level = depth - 1; level >= 0; level--)
        {
            builder.Append('\t', level).Append("}").Append(newLine);
        }

        return builder.ToString();
    }
}
