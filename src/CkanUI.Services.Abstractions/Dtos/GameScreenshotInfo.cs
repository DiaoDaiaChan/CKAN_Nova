namespace CkanUI.Services.Abstractions.Dtos;

/// <summary>
/// 一张游戏截图（主菜单「游戏截图」页签的数据行）：只描述盘上的文件，不解码图片。
/// </summary>
/// <remarks>
/// 位图解码属于 Avalonia，只能待在 App 层（04 篇 §1 分层铁律），所以 DTO 与 ViewModel 都只带路径，
/// 真正把文件读成 <c>Bitmap</c> 的是 App 层的转换器。
/// </remarks>
/// <param name="FilePath">截图的绝对路径。</param>
/// <param name="FileName">文件名（含扩展名）。</param>
/// <param name="Bytes">文件字节数，用于展示体积。</param>
/// <param name="CapturedAt">文件最后写入时间（UTC），KSP 写完即落盘，等价于拍摄时间。</param>
public sealed record GameScreenshotInfo(string FilePath, string FileName, long Bytes, DateTimeOffset CapturedAt);
