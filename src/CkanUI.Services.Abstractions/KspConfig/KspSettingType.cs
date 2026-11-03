namespace CkanUI.Services.Abstractions.KspConfig;

/// <summary>
/// KSP 配置项的数据渲染与编辑呈现类型。
/// </summary>
public enum KspSettingType
{
    /// <summary>布尔开关（True / False）。</summary>
    Boolean = 0,

    /// <summary>连续浮点数值（滑块调节，如音量 0.0 - 1.0、界面缩放等）。</summary>
    Float = 1,

    /// <summary>整数（帧率限制、阴影层级、缓冲区等）。</summary>
    Integer = 2,

    /// <summary>预设枚举选项（如抗锯齿 0/2/4/8，画质档位 1-5，下拉框呈现）。</summary>
    Preset = 3,

    /// <summary>屏幕分辨率（宽 x 高组合）。</summary>
    Resolution = 4,

    /// <summary>任意纯文本输入。</summary>
    Text = 5
}
