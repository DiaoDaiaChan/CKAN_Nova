namespace CkanUI.ViewModels.Dialogs.InstanceDialogs;

/// <summary>
/// 实例表单的纯校验函数（03 篇 §8 D-06「路径校验 / 重名检查」、04 篇 §9）。
/// </summary>
/// <remarks>
/// 纯函数、无 IO、无状态：同样的输入永远同样的错误文案，因此可以在 headless 单测里逐条覆盖，
/// 视图模型只负责把结果显示到 <see cref="InstanceFormViewModel.ValidationError"/>。
/// </remarks>
public static class InstanceFormValidator
{
    /// <summary>实例名称长度上限（与 Core 实例记录的可读性折中，超长名称在列表里无法辨认）。</summary>
    public const int MaxNameLength = 64;

    /// <summary>校验实例名称；通过返回 null，否则返回中文错误文案。</summary>
    /// <param name="name">用户输入的名称（允许首尾空白，内部会 Trim）。</param>
    /// <param name="existingNames">已注册实例名（用于重名检查，忽略大小写）。</param>
    /// <param name="currentName">重命名场景的原名称：新名与原名相同视为无变化。</param>
    public static string? ValidateName(string? name, IReadOnlyCollection<string>? existingNames = null, string? currentName = null)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return "实例名称不能为空。";
        }

        if (trimmed.Length > MaxNameLength)
        {
            return $"实例名称不能超过 {MaxNameLength} 个字符。";
        }

        foreach (var invalid in InvalidNameChars())
        {
            if (trimmed.Contains(invalid, StringComparison.Ordinal))
            {
                return "实例名称不能包含 \\ / : * ? \" < > | 等字符。";
            }
        }

        if (!string.IsNullOrWhiteSpace(currentName) && string.Equals(trimmed, currentName.Trim(), StringComparison.Ordinal))
        {
            return "新名称与原名相同，无需重命名。";
        }

        if (existingNames is not null)
        {
            foreach (var existing in existingNames)
            {
                if (string.IsNullOrWhiteSpace(existing))
                {
                    continue;
                }

                if (string.Equals(trimmed, existing.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return $"已存在同名实例「{existing.Trim()}」，请换一个名称。";
                }
            }
        }

        return null;
    }

    /// <summary>校验游戏目录；通过返回 null，否则返回中文错误文案。</summary>
    public static string? ValidatePath(string? path)
    {
        var trimmed = path?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return "请选择 KSP 安装目录。";
        }

        foreach (var invalid in Path.GetInvalidPathChars())
        {
            if (trimmed.Contains(invalid, StringComparison.Ordinal))
            {
                return "目录路径包含非法字符。";
            }
        }

        if (!Path.IsPathRooted(trimmed))
        {
            return "请填写绝对路径（例如 D:\\Games\\Kerbal Space Program）。";
        }

        return null;
    }

    /// <summary>名称里不允许出现的字符：文件名非法字符 + 目录分隔符（名称会被用作克隆目录名）。</summary>
    private static char[] InvalidNameChars()
    {
        var invalid = Path.GetInvalidFileNameChars();
        var result = new char[invalid.Length + 2];
        invalid.CopyTo(result, 0);
        result[invalid.Length] = '/';
        result[invalid.Length + 1] = '\\';
        return result;
    }
}
