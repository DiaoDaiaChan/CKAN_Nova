namespace CkanUI.ViewModels.Dialogs.InstanceDialogs;

/// <summary>实例对话框的工作模式（03 篇 §8 D-06 新增/编辑、04 篇 §9 克隆与重命名）。</summary>
public enum InstanceFormMode
{
    /// <summary>新增实例：需要游戏目录 + 实例名称。</summary>
    Add,

    /// <summary>克隆实例：只需新名称，目标目录由服务层按「源目录同级 + 新名称」推导。</summary>
    Clone,

    /// <summary>重命名实例：只改记录里的名称，不动磁盘目录。</summary>
    Rename,
}
