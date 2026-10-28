using System.Reflection;
using NUnit.Framework;

namespace CkanUI.Localization.Tests;

/// <summary>
/// 键集漂移守卫（16 篇 §5.1）：常量名即键值、常量为唯一真源、语言包不得留死键。
/// 与 ShippedPacksTests 互补——那边断言"常量都出现在包里"，这边断言"包里没有常量之外的键"。
/// </summary>
[TestFixture]
public sealed class LocalizationKeyDriftTests
{
    private static IReadOnlyList<FieldInfo> KeyFields() =>
        [.. typeof(LocalizationKeys)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && !f.IsInitOnly && f.FieldType == typeof(string))];

    [Test]
    public void Constant_names_follow_the_key_convention()
    {
        Assert.That(KeyFields(), Is.Not.Empty);

        foreach (var field in KeyFields())
        {
            var key = (string)field.GetRawConstantValue()!;
            Assert.That(key, Does.StartWith("Ui_"), field.Name + " 的键值必须带 Ui_ 前缀。");
            // C# 标识符不能带下划线分段，所以常量名是键值去掉 Ui_ 后按段拼接的结果；
            // 这条换算规则一致，才能从 XAML 里的键名反查常量、也才能在改键时立刻发现漏改。
            Assert.That(field.Name, Is.EqualTo(FieldNameOf(key)), key + " 的常量名与键值不符。");
        }
    }

    private static string FieldNameOf(string key) =>
        string.Concat(key.Replace("Ui_", string.Empty, StringComparison.Ordinal)
            .Split('_', StringSplitOptions.RemoveEmptyEntries));

    [Test]
    public void Keys_are_unique()
    {
        var duplicates = KeyFields()
            .GroupBy(f => (string)f.GetRawConstantValue()!, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToArray();

        Assert.That(duplicates, Is.Empty);
    }

    [Test]
    public void Shipped_packs_declare_no_keys_outside_localization_keys()
    {
        var known = KeyFields().Select(f => (string)f.GetRawConstantValue()!).ToHashSet(StringComparer.Ordinal);
        var packs = LocalizerFactory.LoadEmbedded(NullLocalizationLog.Instance);

        Assert.That(packs, Is.Not.Empty);
        foreach (var pack in packs)
        {
            var dead = pack.Strings.Keys.Where(k => !known.Contains(k)).ToArray();
            Assert.That(dead, Is.Empty, pack.Locale + " 里有常量之外的键（死键）:" + string.Join(", ", dead));
        }
    }

    [Test]
    public void Shipped_packs_are_parseable_and_complete()
    {
        var packs = LocalizerFactory.LoadEmbedded(NullLocalizationLog.Instance);
        var zh = packs.Single(p => p.Locale == "zh-CN");
        var en = packs.Single(p => p.Locale == "en-US");

        Assert.That(zh.Strings.Keys.OrderBy(k => k, StringComparer.Ordinal),
            Is.EqualTo(en.Strings.Keys.OrderBy(k => k, StringComparer.Ordinal)),
            "两份语言包键集必须完全相同，否则切换语言会凭空冒出缺键。");
    }
}
