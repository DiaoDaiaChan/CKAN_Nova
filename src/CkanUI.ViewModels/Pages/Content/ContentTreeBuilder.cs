using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.ViewModels.Pages.Content;

/// <summary>把扁平的 ContentEntry 列表构建为层次树结构（目录在前，同级按名称排序）。</summary>
public static class ContentTreeBuilder
{
    public static IReadOnlyList<ContentNodeViewModel> Build(IEnumerable<ContentEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var directories = new Dictionary<string, ContentNodeViewModel>(StringComparer.OrdinalIgnoreCase);
        var files = new List<ContentNodeViewModel>();

        foreach (var entry in entries)
        {
            var node = new ContentNodeViewModel(entry);
            if (entry.IsDirectory)
            {
                directories[entry.RelativePath] = node;
            }
            else
            {
                files.Add(node);
            }
        }

        var roots = new List<ContentNodeViewModel>();

        foreach (var (relative, node) in directories)
        {
            var parent = ParentOf(relative);
            if (parent.Length > 0 && directories.TryGetValue(parent, out var parentNode))
            {
                parentNode.AddChild(node);
            }
            else
            {
                roots.Add(node);
            }
        }

        foreach (var file in files)
        {
            var parent = ParentOf(file.RelativePath);
            if (parent.Length > 0 && directories.TryGetValue(parent, out var parentNode))
            {
                parentNode.AddChild(file);
            }
            else
            {
                roots.Add(file);
            }
        }

        Sort(roots);
        return roots;
    }

    private static void Sort(List<ContentNodeViewModel> nodes)
    {
        nodes.Sort(static (left, right) =>
        {
            if (left.IsDirectory != right.IsDirectory)
            {
                return left.IsDirectory ? -1 : 1;
            }
            return string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
        });

        foreach (var node in nodes)
        {
            Sort(node.ChildrenList);
        }
    }

    private static string ParentOf(string relative)
    {
        var index = relative.LastIndexOf('/');
        return index < 0 ? string.Empty : relative[..index];
    }
}
