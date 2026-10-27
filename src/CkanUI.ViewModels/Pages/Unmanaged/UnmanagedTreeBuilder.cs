using CkanUI.Services.Abstractions;

namespace CkanUI.ViewModels.Pages.Unmanaged;

/// <summary>把扫描得到的扁平列表拼成树（03 篇 §2 V-07：目录在前、同级按名称排序）。</summary>
public static class UnmanagedTreeBuilder
{
    /// <summary>构建树；<paramref name="filter"/> 非空时只保留匹配项及其祖先。</summary>
    public static IReadOnlyList<UnmanagedNodeViewModel> Build(
        IEnumerable<UnmanagedFileInfo> entries, string? filter = null)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var directories = new Dictionary<string, UnmanagedNodeViewModel>(StringComparer.OrdinalIgnoreCase);
        var files = new List<UnmanagedNodeViewModel>();
        foreach (var entry in entries)
        {
            var node = new UnmanagedNodeViewModel(entry);
            if (entry.IsDirectory) directories[entry.RelativePath] = node;
            else files.Add(node);
        }

        var roots = new List<UnmanagedNodeViewModel>();
        foreach (var (relative, node) in directories)
        {
            var parent = ParentOf(relative);
            if (parent.Length > 0 && directories.TryGetValue(parent, out var parentNode)) parentNode.AddChild(node);
            else roots.Add(node);
        }

        foreach (var file in files)
        {
            var parent = ParentOf(file.RelativePath);
            if (parent.Length > 0 && directories.TryGetValue(parent, out var parentNode)) parentNode.AddChild(file);
            else roots.Add(file);
        }

        Sort(roots);
        if (string.IsNullOrWhiteSpace(filter)) return roots;
        return Prune(roots, filter.Trim());
    }

    private static void Sort(List<UnmanagedNodeViewModel> nodes)
    {
        nodes.Sort(static (left, right) =>
        {
            if (left.IsDirectory != right.IsDirectory) return left.IsDirectory ? -1 : 1;
            return string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
        });

        foreach (var node in nodes) Sort(node.ChildrenList);
    }

    private static List<UnmanagedNodeViewModel> Prune(List<UnmanagedNodeViewModel> nodes, string filter)
    {
        var kept = new List<UnmanagedNodeViewModel>(nodes.Count);
        foreach (var node in nodes)
        {
            var selfMatches = node.RelativePath.Contains(filter, StringComparison.OrdinalIgnoreCase);
            var children = selfMatches ? node.ChildrenList.ToList() : Prune(node.ChildrenList, filter);
            if (!selfMatches && children.Count == 0) continue;
            node.ReplaceChildren(children);
            kept.Add(node);
        }

        return kept;
    }

    private static string ParentOf(string relative)
    {
        var index = relative.LastIndexOf('/');
        return index < 0 ? string.Empty : relative[..index];
    }
}
