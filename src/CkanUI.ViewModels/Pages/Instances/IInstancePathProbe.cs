namespace CkanUI.ViewModels.Pages.Instances;

/// <summary>
/// 路径探测器：VM 不能调用 <c>Directory.Exists</c>（04 篇 §1 禁止 VM 碰 IO），
/// 但界面需要「路径不存在 → 灰显 + 重新定位」与「克隆目标目录已存在 → 二次确认」（03 篇 §5）。
/// </summary>
public interface IInstancePathProbe
{
    /// <summary>目录是否存在。</summary>
    bool DirectoryExists(string path);
}
