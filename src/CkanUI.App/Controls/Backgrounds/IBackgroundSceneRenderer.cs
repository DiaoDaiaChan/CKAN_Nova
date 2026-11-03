using Avalonia.Media;

namespace CkanUI.App.Controls.Backgrounds;

/// <summary>一种程序化背景场景。实现必须无状态、只读输入、不碰控件树。</summary>
public interface IBackgroundSceneRenderer
{
    void Render(DrawingContext context, BackgroundSceneContext scene);
}
