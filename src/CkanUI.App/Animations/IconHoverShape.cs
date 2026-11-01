namespace CkanUI.App.Animations;

/// <summary>图标悬浮后的循环形态。</summary>
public enum IconHoverShape
{
    /// <summary>跟随宿主或祖先的设置，谁都没说话时用 <see cref="Waggle"/>。</summary>
    Auto = 0,

    /// <summary>轻微摇摆：通用图标的默认形态。</summary>
    Waggle = 1,

    /// <summary>持续旋转：齿轮、时钟、同步这类"转起来才像在工作"的图标。</summary>
    Spin = 2,
}
