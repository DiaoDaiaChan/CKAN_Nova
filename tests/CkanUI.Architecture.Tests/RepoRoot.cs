using System;
using System.IO;

namespace CkanUI.Architecture.Tests;

/// <summary>定位仓库根（ckan-ui.sln 所在目录），供架构测试读取 csproj 与源码树。</summary>
internal static class RepoRoot
{
    private static readonly Lazy<string> LazyRoot = new(Find);

    public static string Path => LazyRoot.Value;

    private static string Find()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(System.IO.Path.Combine(dir.FullName, "ckan-ui.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("未找到 ck 仓库根：向上找不到 ckan-ui.sln");
    }
}
