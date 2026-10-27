using System.Globalization;

namespace CkanUI.Sfs;

/// <summary>
/// 值索引（07 篇 §9.1 INV-3 "值不变量"的可执行版本）。
/// 把文档压平成 (path,key) → value 的**多重集**（有序列表，重复保留）：
/// 格式化前后逐项相等，就说明"值的字节一个都没动"。
/// 路径里带同名兄弟序号（实测 31 个 SCENARIO 都叫 SCENARIO），否则跨 SCENARIO 的值会互相顶替。
/// </summary>
public static class SfsValueIndex
{
    private const string Field = "\u001F";

    /// <summary>构建排序后的键值多重集，用 <see cref="StringComparer.Ordinal"/> 排序，可逐项比较。</summary>
    public static IReadOnlyList<string> Build(SfsDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var entries = new List<string>(document.PairCount);
        var work = new Stack<WorkItem>();
        PushRange(work, document.Elements, string.Empty);

        while (work.Count > 0)
        {
            var (element, path) = work.Pop();
            switch (element)
            {
                case SfsPair pair:
                    entries.Add(string.Concat(path, Field, pair.Key, Field, pair.Value));
                    break;

                case SfsNode node:
                    var childPaths = BuildChildPaths(node, path);
                    for (var index = node.Children.Count - 1; index >= 0; index--)
                    {
                        work.Push(new WorkItem(node.Children[index], childPaths[index]));
                    }

                    break;

                default:
                    break;
            }
        }

        entries.Sort(StringComparer.Ordinal);
        return entries;
    }

    /// <summary>兄弟同名节点用出现序号区分；键值对沿用父节点路径。</summary>
    private static string[] BuildChildPaths(SfsNode node, string path)
    {
        var childPaths = new string[node.Children.Count];
        var counters = new Dictionary<string, int>(StringComparer.Ordinal);

        for (var index = 0; index < node.Children.Count; index++)
        {
            if (node.Children[index] is not SfsNode child)
            {
                childPaths[index] = path;
                continue;
            }

            counters.TryGetValue(child.Name, out var ordinal);
            counters[child.Name] = ordinal + 1;
            childPaths[index] = string.Create(CultureInfo.InvariantCulture, $"{path}/{child.Name}#{ordinal}");
        }

        return childPaths;
    }

    private static void PushRange(Stack<WorkItem> work, IReadOnlyList<SfsElement> elements, string path)
    {
        for (var index = elements.Count - 1; index >= 0; index--)
        {
            work.Push(new WorkItem(elements[index], path));
        }
    }

    private readonly record struct WorkItem(SfsElement Element, string Path);
}
