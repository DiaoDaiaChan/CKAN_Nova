using System;

namespace CkanUI.ViewModels.Pages.Home;

/// <summary>
/// 主菜单「游戏截图」页签里的一行：视图只拿路径去解码缩略图，文案在 VM 侧就已经成形。
/// </summary>
/// <param name="FilePath">截图文件的绝对路径（视图据此解码缩略图）。</param>
/// <param name="FileName">文件名，用于标题与提示。</param>
/// <param name="CapturedText">「拍摄于 …」的成品文案。</param>
/// <param name="SizeText">文件大小文案，如 <c>1.4 MB</c>。</param>
public sealed record HomeScreenshotRowViewModel(string FilePath, string FileName, string CapturedText, string SizeText);
