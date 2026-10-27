namespace CkanUI.ViewModels.Pages.Cache;

/// <summary>
/// 宿主注入的「选一个 ZIP 文件」能力（App 侧用 Avalonia 的存储提供者实现，
/// ViewModels 不碰平台 API，04 篇 §1 架构铁律）。
/// </summary>
public interface ICacheZipPicker
{
    /// <summary>弹出文件对话框选一个 ZIP；用户取消时返回 <c>null</c>。</summary>
    Task<string?> PickZipFileAsync(CancellationToken ct = default);
}
