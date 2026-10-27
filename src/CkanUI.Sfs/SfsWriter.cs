using System.Text;

namespace CkanUI.Sfs;

/// <summary>
/// 保字序列化器（13 篇 §4 SfsWriter、WBS SFS-02）。
/// <see cref="Write"/> 从**语法树**重建原文：逐元素写回 <see cref="SfsRawLine.Content"/> 与各自的行尾，
/// 因此未修改的文档必然逐字节等于原文件（L3 往返一致）。
/// 这是"打开就改坏存档"的唯一防线——它不重新排版任何东西。
/// </summary>
public static class SfsWriter
{
    /// <summary>按原样（AsIs）序列化为文本；未修改时与源文本逐字节相同。</summary>
    public static string Write(SfsDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var builder = new StringBuilder(document.SourceText.Length + 64);
        if (document.HasBom)
        {
            builder.Append('\uFEFF');
        }

        var work = new Stack<WorkItem>();
        PushRange(work, document.Elements, 0);

        while (work.Count > 0)
        {
            var (element, depth, role) = work.Pop();

            if (element is SfsNode node)
            {
                if (role == SfsLineRole.Close)
                {
                    if (node.CloseLine is { } closeLine)
                    {
                        builder.Append(closeLine.Content).Append(closeLine.Terminator);
                    }

                    continue;
                }

                builder.Append(node.Raw.Content).Append(node.Raw.Terminator);
                if (node.BraceLine is { } braceLine)
                {
                    builder.Append(braceLine.Content).Append(braceLine.Terminator);
                }

                work.Push(new WorkItem(node, depth, SfsLineRole.Close));
                PushRange(work, node.Children, depth + 1);
                continue;
            }

            builder.Append(element.Raw.Content).Append(element.Raw.Terminator);
        }

        return builder.ToString();
    }

    /// <summary>按原样序列化为 UTF-8 字节。</summary>
    public static byte[] WriteBytes(SfsDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return Encoding.UTF8.GetBytes(Write(document));
    }

    /// <summary>逆序压栈，保证弹出顺序与原文件一致。</summary>
    private static void PushRange(Stack<WorkItem> work, IReadOnlyList<SfsElement> elements, int depth)
    {
        for (var index = elements.Count - 1; index >= 0; index--)
        {
            work.Push(new WorkItem(elements[index], depth, SfsLineRole.Element));
        }
    }

    /// <summary>写盘时机（AsIs / 格式化）——用值类型避免逐行装箱。</summary>
    private readonly record struct WorkItem(SfsElement Element, int Depth, SfsLineRole Role);
}

/// <summary>一个元素在遍历中要产出的"行角色"。</summary>
internal enum SfsLineRole
{
    /// <summary>元素首行（节点 = 名字行）。</summary>
    Element = 0,

    /// <summary>节点的左花括号行。</summary>
    Brace = 1,

    /// <summary>节点的右花括号行。</summary>
    Close = 2,
}
