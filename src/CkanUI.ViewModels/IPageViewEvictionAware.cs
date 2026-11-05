namespace CkanUI.ViewModels;

/// <summary>页面视图被驻留预算摘下时的清理钩子。</summary>
/// <remarks>
/// 视图没了，VM 里为视图缓存的重资源（解码后的位图等）仍会被单例 VM 钉住——那部分内存
/// 既不随视图回收，也不随 GC 释放。实现方在这里放掉它们，下次进入页面时按原有加载路径重新取。
/// 调用方保证：只有页面不可见、且用户已静置超过驻留阈值时才会触发。
/// </remarks>
public interface IPageViewEvictionAware
{
    /// <summary>视图已被摘出可视树，放掉只为它缓存的重资源。</summary>
    void OnViewEvicted();
}
