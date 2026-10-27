namespace CkanUI.Services.Abstractions;

/// <summary>页面/VM 生命周期（04 篇 §1）：订阅在 <see cref="Activate"/> 建立、<see cref="Deactivate"/> 解除。</summary>
public interface IActivatable
{
    void Activate();

    void Deactivate();
}
