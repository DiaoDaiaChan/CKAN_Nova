using System.Globalization;
using System.Text;

namespace CkanUI.Services.Saves;

/// <summary>
/// 存档落盘原语（13 篇铁律 L8 原子写 / L9 写前快照 / L10 时戳策略）。
/// 与命令行参考实现 <c>src/CkanUI.Sfs.Cli/SfsCli.cs</c> 保持一致。
/// </summary>
internal static class SaveFileWriter
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// 生成写前快照 <c>&lt;name&gt;.pre-edit-&lt;yyyyMMdd-HHmmss&gt;.sfs</c>。
    /// 同名快照已存在时换 <c>-2</c>…<c>-20</c> 后缀，**绝不覆盖已有快照**（L9）。
    /// </summary>
    /// <returns>快照路径；无法创建时返回 null（调用方必须据此放弃覆盖写）。</returns>
    public static string? CreatePreEditSnapshot(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var directory = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(directory))
        {
            return null;
        }

        var stem = Path.GetFileNameWithoutExtension(path);
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        for (var attempt = 1; attempt <= 20; attempt++)
        {
            var suffix = attempt == 1 ? string.Empty : "-" + attempt.ToString(CultureInfo.InvariantCulture);
            var candidate = Path.Combine(
                directory,
                stem + SaveFileService.PreEditMarker + stamp + suffix + SaveFileService.SaveExtension);
            if (File.Exists(candidate))
            {
                continue;
            }

            try
            {
                File.Copy(path, candidate, overwrite: false);
                return candidate;
            }
            catch (IOException)
            {
                // 被别的进程抢先建了同名快照：换下一个后缀重试。
            }
        }

        return null;
    }

    /// <summary>
    /// 用已经写好的临时文件顶替目标（大文件补丁路径）：同样是一次 <c>File.Move(overwrite: true)</c>，
    /// 失败时原文件不变；可选恢复原修改时间（L10）。
    /// </summary>
    public static void CommitTemp(string tempPath, string targetPath, DateTime? preserveLastWriteUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tempPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);

        File.Move(tempPath, targetPath, overwrite: true);
        if (preserveLastWriteUtc is { } stamp)
        {
            File.SetLastWriteTimeUtc(targetPath, stamp);
        }
    }

    /// <summary>
    /// 原子写：先写 <c>&lt;name&gt;.tmp-&lt;8 位 guid&gt;</c>，再 <c>File.Move(overwrite: true)</c> 顶替目标，
    /// 失败时原文件不变；可选恢复原修改时间（L10）。
    /// </summary>
    public static void WriteAtomic(string path, string text, DateTime? preserveLastWriteUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(text);

        var directory = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(directory))
        {
            throw new InvalidOperationException($"目标路径没有目录部分：{path}");
        }

        var temp = Path.Combine(
            directory,
            Path.GetFileName(path) + ".tmp-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            File.WriteAllBytes(temp, Utf8NoBom.GetBytes(text));
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
            {
                try
                {
                    File.Delete(temp);
                }
                catch (IOException)
                {
                    // 临时文件删不掉不影响写入结果，下次运行会被当作垃圾文件忽略。
                }
            }
        }

        if (preserveLastWriteUtc is { } stamp)
        {
            File.SetLastWriteTimeUtc(path, stamp);
        }
    }
}