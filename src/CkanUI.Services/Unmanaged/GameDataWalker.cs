namespace CkanUI.Services.Unmanaged;

/// <summary>
/// GameData 下逐层枚举文件，**跳过重解析点**（目录联接 / 符号链接）。
/// </summary>
/// <remarks>
/// <para>为什么不用 <c>SearchOption.AllDirectories</c>：那个重载会跟进联接。玩家目录里经常有
/// 「GameData/xxx → D:\\OtherGameData\\xxx」这类联接，跟进它有两个后果——指回上层的联接会无限递归，
/// 指向外部的联接会把 GameData 之外的文件当成「未管理内容」列出来，接着被用户一键删掉（越界删除）。
/// 去重、残留清理两处（<c>DedupScanner</c> / <c>ResidualCleanupService</c>）早已按「跳过重解析点」实现，
/// 这里保持一致。</para>
/// <para>子目录读不了（权限/被占用）时跳过它继续扫：扫描结果宁可不完整也不要整页失败，
/// 但根目录本身读不了仍然抛出，由调用方转成可读的失败提示。</para>
/// </remarks>
internal static class GameDataWalker
{
    /// <summary>深度优先枚举 <paramref name="root"/> 下的文件（含 <paramref name="pattern"/> 过滤），不跟进联接。</summary>
    public static IEnumerable<string> EnumerateFiles(string root, string pattern)
    {
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            string[] files;
            string[] children;
            try
            {
                files = Directory.GetFiles(directory, pattern);
                children = Directory.GetDirectories(directory);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (string.Equals(directory, root, StringComparison.Ordinal))
                {
                    throw;
                }

                continue;
            }

            foreach (var file in files)
            {
                yield return file;
            }

            foreach (var child in children)
            {
                if (!IsLink(child))
                {
                    pending.Push(child);
                }
            }
        }
    }

    /// <summary>
    /// 判断路径是不是重解析点；拿不到属性时按「是联接」处理，宁可少扫也不要跟进。
    /// 删除与接管也要用它把关：扫描不跟进联接，但用户或调用方可能直接点名一个联接路径。
    /// </summary>
    public static bool IsLink(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }
}
