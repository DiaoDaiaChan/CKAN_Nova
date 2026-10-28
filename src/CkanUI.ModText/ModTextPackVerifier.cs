using System;
using System.Collections.Generic;
using System.IO;
using UI18n.Core.Update;

namespace CkanUI.ModText;

/// <summary>单个包文件的校验结果。</summary>
/// <param name="ModRelativePath">清单里的相对路径。</param>
/// <param name="Ok">是否通过。</param>
/// <param name="Reason">失败原因码（见 <see cref="UpdateVerify"/>）；通过时为空串。</param>
public sealed record ModTextPackFileCheck(string ModRelativePath, bool Ok, string Reason);

/// <summary>一次包校验的汇总结果。</summary>
/// <param name="Ok">全部通过为 true。</param>
/// <param name="Reason">失败原因码；通过时为空串。</param>
/// <param name="Files">逐文件结果（清单没有 files 时为空）。</param>
public sealed record ModTextPackVerification(bool Ok, string Reason, IReadOnlyList<ModTextPackFileCheck> Files)
{
    /// <summary>构造一个"无清单"的失败结果。</summary>
    /// <param name="reason">原因码。</param>
    public static ModTextPackVerification Failed(string reason) => new(false, reason, []);
}

/// <summary>
/// 元数据包更新的完整性校验（17 篇 MTI-06）：复用 UI18n.Core 的清单读取与
/// <see cref="UpdateVerify"/>，只在"本地 packs 目录布局"这一层做适配。
/// </summary>
/// <remarks>
/// 为什么直接复用而不重写：清单形状与 zip-slip 校验是跨语言契约（出包脚本用 PowerShell 写、
/// 游戏与工具用 C# 读），任何一边各写一套就必然出现"脚本觉得对、读取器觉得错"的事故
/// （见 <see cref="UpdateManifest.ReaderContract"/> 注释里的 1.6.0 事故）。
/// 本类只做两件事：把清单相对路径映射到本地 packs 目录，汇总逐文件结论。
/// 所有方法返回结果对象，**永不抛异常**。
/// </remarks>
public static class ModTextPackVerifier
{
    /// <summary>清单里的包相对路径前缀（<c>Packs/&lt;lang&gt;/&lt;file&gt;.jsonl</c>）。</summary>
    public const string PackSegment = "Packs/";

    /// <summary>读取并解析更新清单。</summary>
    /// <param name="manifestJson">manifest.json 文本。</param>
    /// <param name="manifest">解析结果。</param>
    /// <param name="error">失败原因。</param>
    public static bool TryReadManifest(string? manifestJson, out UpdateManifest? manifest, out string error)
    {
        manifest = null;
        if (string.IsNullOrWhiteSpace(manifestJson))
        {
            error = UpdateVerify.ReasonNoManifest;
            return false;
        }

        return UpdateManifest.TryParse(manifestJson, out manifest, out error);
    }

    /// <summary>校验下载到的 zip（字节数 + sha256 + 逐条目）。</summary>
    /// <param name="zip">zip 字节。</param>
    /// <param name="manifest">已解析的清单。</param>
    public static ModTextPackVerification VerifyZip(byte[]? zip, UpdateManifest? manifest)
    {
        if (manifest is null)
        {
            return ModTextPackVerification.Failed(UpdateVerify.ReasonNoManifest);
        }

        return UpdateVerify.CheckZip(zip!, manifest, out var reason)
            ? new ModTextPackVerification(true, UpdateVerify.ReasonNone, [])
            : ModTextPackVerification.Failed(reason);
    }

    /// <summary>
    /// 按清单校验本地 packs 目录（离线导入场景）：逐个比对字节数与 sha256。
    /// </summary>
    /// <param name="packsRoot">本地 packs 目录（其下为 &lt;lang&gt;/*.jsonl）。</param>
    /// <param name="manifest">已解析的清单；null 直接判"无清单"。</param>
    public static ModTextPackVerification VerifyPacksDirectory(string? packsRoot, UpdateManifest? manifest)
    {
        if (manifest is null)
        {
            return ModTextPackVerification.Failed(UpdateVerify.ReasonNoManifest);
        }

        if (string.IsNullOrWhiteSpace(packsRoot))
        {
            return ModTextPackVerification.Failed(UpdateVerify.ReasonUnreadable);
        }

        var checks = new List<ModTextPackFileCheck>(manifest.FileCount);
        foreach (var file in manifest.Files)
        {
            if (!TryMapToLocal(packsRoot, file.ModRelativePath, out var localPath))
            {
                // 清单只登记包文件，出现别的路径说明清单坏了，整份判失败而不是跳过。
                checks.Add(new ModTextPackFileCheck(file.ModRelativePath, false, UpdateVerify.ReasonUnreadable));
                return new ModTextPackVerification(false, UpdateVerify.ReasonUnreadable, checks);
            }

            var ok = UpdateVerify.CheckFile(localPath, file.Bytes, file.Sha256, out var reason);
            checks.Add(new ModTextPackFileCheck(file.ModRelativePath, ok, reason));
            if (!ok)
            {
                return new ModTextPackVerification(false, reason, checks);
            }
        }

        return new ModTextPackVerification(true, UpdateVerify.ReasonNone, checks);
    }

    /// <summary>校验单个包文件。</summary>
    /// <param name="path">文件绝对路径。</param>
    /// <param name="expectedBytes">期望字节数。</param>
    /// <param name="expectedSha256">期望 sha256（64 位 hex）。</param>
    /// <param name="reason">失败原因码。</param>
    public static bool VerifyFile(string? path, long expectedBytes, string? expectedSha256, out string reason) =>
        UpdateVerify.CheckFile(path!, expectedBytes, expectedSha256!, out reason);

    /// <summary>
    /// 把清单里的 <c>Packs/&lt;lang&gt;/&lt;file&gt;.jsonl</c> 映射成本地 packs 目录下的绝对路径。
    /// </summary>
    /// <param name="packsRoot">本地 packs 目录。</param>
    /// <param name="modRelativePath">清单相对路径。</param>
    /// <param name="localPath">本地绝对路径。</param>
    public static bool TryMapToLocal(string? packsRoot, string? modRelativePath, out string localPath)
    {
        localPath = string.Empty;

        if (string.IsNullOrWhiteSpace(packsRoot) || string.IsNullOrWhiteSpace(modRelativePath))
        {
            return false;
        }

        var relative = modRelativePath.Replace('\\', '/');
        if (relative.StartsWith(PackSegment, StringComparison.OrdinalIgnoreCase))
        {
            relative = relative[PackSegment.Length..];
        }

        if (relative.Length == 0 || !ZipSafety.IsSafeRelativePath(relative))
        {
            return false;
        }

        localPath = Path.Combine(packsRoot, relative.Replace('/', Path.DirectorySeparatorChar));
        return true;
    }
}
