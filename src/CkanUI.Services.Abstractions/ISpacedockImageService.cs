using System.Threading;
using System.Threading.Tasks;

namespace CkanUI.Services.Abstractions;

/// <summary>
/// SpaceDock 模组截图/图片抓取服务。
/// 根据 SpaceDock 页面链接解析 mod id，请求 SpaceDock API 获取 mod 截图/背景图或缩略图字节数据。
/// </summary>
public interface ISpacedockImageService
{
    /// <summary>
    /// 获取指定 SpaceDock 模组页面的截图/背景图片原始字节数据。
    /// </summary>
    /// <param name="spacedockUrl">SpaceDock 模组页面 URL（例如 https://spacedock.info/mod/123/xxx）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>操作结果，成功时包含图片二进制 byte[] 数据。</returns>
    Task<OperationResult<byte[]>> FetchScreenshotAsync(string spacedockUrl, CancellationToken cancellationToken = default);
}
