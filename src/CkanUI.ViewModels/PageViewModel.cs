using CkanUI.Services.Abstractions;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CkanUI.ViewModels;

/// <summary>所有页面视图模型的公共基类（04 篇 §1 / ARC-03）。提供幂等的 <see cref="IActivatable"/> 生命周期实现与扩展钩子。</summary>
public abstract class PageViewModel : ObservableObject, IActivatable
{
    /// <summary>页面标题。</summary>
    public virtual string Title => string.Empty;

    /// <summary>
    /// 页面文案解析器（本地化键 → 译文）。为 null 时页面用源码里的中文原文。
    /// </summary>
    /// <remarks>
    /// 用委托而不是注入本地化接口：页面 VM 因此不依赖任何具体本地化实现，单测也能"不给解析器"
    /// 直接断言中文原文（既有 1500+ 用例就是这么写的）。宿主在导航时统一挂 <c>ILocalizer.Get</c>，
    /// 于是同一份页面代码在 zh-CN 下由语言包给中文、在 en-US 下给英文，缺键时语言包自己回退到键。
    /// </remarks>
    public Func<string, string>? TextResolver { get; set; }

    /// <summary>带占位符的文案解析器（键 + 参数 → 译文），通常是 <c>ILocalizer.Format</c>；为 null 时退回中文原文格式化。</summary>
    public Func<string, object?[], string>? TextFormatter { get; set; }

    /// <summary>取一段页面文案：没有解析器时返回源码里的中文原文。</summary>
    /// <param name="fallback">源码里的中文原文（同时是测试里的期望值）。</param>
    /// <param name="key">本地化键（<see cref="Localization.LocalizationKeys"/> 里的常量）。</param>
    protected string Text(string fallback, string key)
        => TextResolver is null ? fallback : TextResolver(key);

    /// <summary>取一段带占位符的文案（<c>{0}</c> 风格）：没有格式化器时用中文原文自己格式化。</summary>
    /// <param name="fallback">源码里的中文原文（含占位符）。</param>
    /// <param name="key">本地化键。</param>
    /// <param name="args">占位符实参。</param>
    protected string Text(string fallback, string key, params object?[] args)
    {
        if (TextFormatter is not null)
        {
            return TextFormatter(key, args);
        }

        if (args.Length == 0)
        {
            return fallback;
        }

        try
        {
            return string.Format(System.Globalization.CultureInfo.CurrentCulture, fallback, args);
        }
        catch (FormatException)
        {
            // 模板里占位符写坏了也只是少一句译文，绝不能因此让页面命令炸掉。
            return fallback;
        }
    }

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
