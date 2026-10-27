using System.Diagnostics;

namespace CkanUI.TestSupport;

/// <summary>
/// 仓库内临时工作目录。
/// 为什么不用 <see cref="Path.GetTempPath"/>：本仓测试要在受限沙箱（以及 CI 的受限 TEMP）里跑，
/// 系统临时目录可能拒绝创建子目录（实测 Windows 沙箱下 Directory.CreateDirectory 抛
/// UnauthorizedAccessException / DirectoryNotFoundException）。仓库根的 `artifacts/tmp` 一定可写，
/// 且 `artifacts/` 已在 .gitignore 与 verify-layout H-01 白名单里，因此统一落在这里。
/// </summary>
public sealed class TempWorkspace : IDisposable
{
    private bool _disposed;

    public TempWorkspace(string? label = null)
    {
        var root = System.IO.Path.Combine(RepositoryRoot.Value, "artifacts", "tmp");
        Directory.CreateDirectory(root);
        var name = string.IsNullOrWhiteSpace(label) ? Guid.NewGuid().ToString("N") : $"{label}-{Guid.NewGuid():N}";
        Path = System.IO.Path.Combine(root, name);
        Directory.CreateDirectory(Path);
    }

    /// <summary>本次测试独占的目录（已创建）。</summary>
    public string Path { get; }

    /// <summary>在 <see cref="Path"/> 下拼一个子路径（不创建）。</summary>
    public string Combine(params string[] parts) => System.IO.Path.Combine(new[] { Path }.Concat(parts).ToArray());

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        TryDelete(Path);
    }

    /// <summary>仓库根：从测试程序集所在目录向上找 ckan-ui.sln。</summary>
    private static readonly Lazy<string> RepositoryRoot = new(() =>
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(System.IO.Path.Combine(directory.FullName, "ckan-ui.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        // 退化：找不到仓库根时用当前工作目录，至少不炸在构造器里。
        return Directory.GetCurrentDirectory();
    });

    private static void TryDelete(string path)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }

                return;
            }
            catch (IOException)
            {
                // 文件句柄尚未释放（log4net / 文件流），退避重试。
                Thread.Sleep(50);
            }
            catch (UnauthorizedAccessException)
            {
                return;
            }
        }

        Debug.WriteLine($"TempWorkspace 未能删除: {path}");
    }
}
