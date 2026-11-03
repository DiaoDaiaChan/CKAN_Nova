namespace CkanUI.Services.Abstractions.Assets;

/// <summary>
/// 加载图文件的命名约定。
/// </summary>
/// <remarks>
/// 「停用一张加载图」不是删文件，而是在扩展名后面加后缀——两个加载图模组（ROLoadingImages /
/// LoadingScreenManager）只认原扩展名，加了后缀就不会被读走。
/// 这个后缀既被服务层（改名/枚举）用，也被 UI 层的缩略图转换器用（解码前要剥掉后缀判断真实格式），
/// 所以放在契约层，避免两处各写一份字面量后悄悄漂移。
/// </remarks>
public static class LoadingImageNaming
{
    /// <summary>停用标记：加在扩展名后面，两个加载图模组都只认原扩展名，所以改了就不会被读。</summary>
    public const string DisabledSuffix = ".disabled";
}
