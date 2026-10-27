using System.Text;

namespace CkanUI.ViewModels.Pages.Mods;

/// <summary>
/// 搜索文本规范化（镜像上游 <c>CkanModule.nonAlphaNumsAnyLanguage</c>：只保留字母与数字）。
/// 视图模型不能引用 CKAN 命名空间，所以这里按同样的字符集重写一份，
/// 保证"解析结果里的关键字"与"行投影出来的可搜文本"落在同一个规范形上。
/// </summary>
public static class ModsSearchText
{
    /// <summary>去掉标点与空白，只保留字母和数字；幂等（再规范化一次结果不变）。</summary>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;

        StringBuilder? builder = null;
        for (var i = 0; i < value.Length; i++)
        {
            var ch = value[i];
            if (char.IsLetterOrDigit(ch))
            {
                builder?.Append(ch);
                continue;
            }

            builder ??= new StringBuilder(value.Length).Append(value, 0, i);
        }

        return builder?.ToString() ?? value;
    }

    /// <summary>
    /// 名称缩写（镜像上游 <c>GUIMod.Abbrevation</c>：按空格切分后取每段首字符），
    /// 让 <c>rt</c> 这类缩写搜索与上游一致。
    /// </summary>
    public static string Abbreviation(string? name)
    {
        if (string.IsNullOrEmpty(name)) return string.Empty;

        var parts = name.Split(' ');
        if (parts.Length == 1) return name.Length > 0 ? name[..1] : string.Empty;

        var builder = new StringBuilder(parts.Length);
        foreach (var part in parts)
        {
            if (part.Length > 0) builder.Append(part[0]);
        }
        return builder.ToString();
    }
}
