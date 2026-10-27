using CkanUI.Services.Abstractions;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CkanUI.ViewModels;

/// <summary>所有页面视图模型的公共基类（04 篇 §1 / ARC-03）。提供幂等的 <see cref="IActivatable"/> 生命周期实现与扩展钩子。</summary>
public abstract class PageViewModel : ObservableObject, IActivatable
{
    /// <summary>页面标题。</summary>
    public virtual string Title => string.Empty;

    /// <summary>当前页面是否处于激活状态。</summary>
    public bool IsActive { get; private set; }

    /// <summary>激活页面（幂等）。多次调用不会重复触发 <see cref="OnActivated"/>。</summary>
    public void Activate()
    {
        if (IsActive)
        {
            return;
        }

        IsActive = true;
        OnActivated();
    }

    /// <summary>取消激活页面（幂等）。在失活后 <see cref="IsActive"/> 为 false，多次调用不会重复触发 <see cref="OnDeactivated"/>。</summary>
    public void Deactivate()
    {
        if (!IsActive)
        {
            return;
        }

        IsActive = false;
        OnDeactivated();
    }

    /// <summary>子类覆写的激活处理钩子，在此建立事件订阅。</summary>
    protected virtual void OnActivated()
    {
    }

    /// <summary>子类覆写的停用处理钩子，在此清理事件订阅。</summary>
    protected virtual void OnDeactivated()
    {
    }
}
