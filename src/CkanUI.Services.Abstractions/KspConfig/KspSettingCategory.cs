namespace CkanUI.Services.Abstractions.KspConfig;

/// <summary>
/// KSP 游戏配置分组类别。
/// </summary>
public enum KspSettingCategory
{
    /// <summary>图像与画质（分辨率、全屏、抗锯齿、阴影、贴图等）。</summary>
    Graphics = 0,

    /// <summary>声音与音效（主音量、航天器音效、背景音乐、环境音等）。</summary>
    Audio = 1,

    /// <summary>常规与游戏性（界面缩放、双击视角、摄像机晃动、自动加固等）。</summary>
    Gameplay = 2,

    /// <summary>高级设置与调试（高级可微调部件、详细日志、调试界面等）。</summary>
    Advanced = 3,

    /// <summary>其他未归类或原始配置参数。</summary>
    Other = 4
}
