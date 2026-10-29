namespace CkanUI.ViewModels.Pages.ModConfig;

/// <summary>
/// 按键组合的文本格式，供 Mod 配置的 keybinding 字段使用。
/// </summary>
/// <remarks>
/// <para>顺序固定为 <c>Ctrl+Alt+Shift+Win+Key</c>：KSP 的键位配置对顺序不敏感，但同一份配置里
/// 写法一致才好搜索、好对比。修饰键名用 Win 而不是 Meta/Command——KSP 面向 Windows 玩家。</para>
/// <para>本类是纯函数，不引用 Avalonia：视图层把 KeyModifiers 拆成四个 bool 再调进来，因此可以在
/// 不启动窗口的单元测试里覆盖。</para>
/// </remarks>
public static class KeyGestureText
{
    /// <summary>拼一个按键组合文本。</summary>
    /// <param name="ctrl">是否按住 Ctrl。</param>
    /// <param name="alt">是否按住 Alt。</param>
    /// <param name="shift">是否按住 Shift。</param>
    /// <param name="meta">是否按住 Win 键。</param>
    /// <param name="key">主键名（键的显示文本）。</param>
    public static string Format(bool ctrl, bool alt, bool shift, bool meta, string? key)
    {
        var name = key?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            return string.Empty;
        }

        var parts = new List<string>(5);
        if (ctrl)
        {
            parts.Add("Ctrl");
        }

        if (alt)
        {
            parts.Add("Alt");
        }

        if (shift)
        {
            parts.Add("Shift");
        }

        if (meta)
        {
            parts.Add("Win");
        }

        parts.Add(name);
        return string.Join("+", parts);
    }
}
