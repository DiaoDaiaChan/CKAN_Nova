using CkanUI.ModText;
using NUnit.Framework;

namespace CkanUI.ModText.Tests;

[TestFixture]
public sealed class ModTextTests
{
    [Test]
    public void ModText_CarriesSource()
    {
        var text = new ModText("你好", ModTextSource.Human, false);
        Assert.That(text.Source, Is.EqualTo(ModTextSource.Human));
        Assert.That(text.Text, Is.EqualTo("你好"));
        Assert.That(text.Stale, Is.False);
    }

    [Test]
    public void ModTextProvider_FindsTranslation_ByExactAndNormalized()
    {
        string text = "An autopilot for Kerbal Space Program";
        ulong h = UI18n.Core.Hashing.Fnv1a.Hash(text);
        string jsonl = "{\"h\":\"" + h.ToString("x16") + "\",\"n\":\"" + text + "\",\"s\":\"" + text + "\",\"t\":\"坎巴拉太空计划的自动驾驶仪\",\"mod\":\"CKANMeta\",\"ctx\":\"MechJeb2\",\"src\":\"human\"}";
        
        var provider = ModTextProvider.FromJsonLines(new[] { jsonl });
        var found = provider.Find("MechJeb2", "abstract", text);

        Assert.That(found, Is.Not.Null);
        Assert.That(found!.Text, Is.EqualTo("坎巴拉太空计划的自动驾驶仪"));
        Assert.That(found.Source, Is.EqualTo(ModTextSource.Human));
        Assert.That(found.Stale, Is.False);
    }

    [Test]
    public void ModTextProvider_ReturnsNull_ForKeepOrMissing()
    {
        ulong h = UI18n.Core.Hashing.Fnv1a.Hash("MechJeb2");
        string keepJsonl = "{\"h\":\"" + h.ToString("x16") + "\",\"n\":\"MechJeb2\",\"s\":\"MechJeb2\",\"t\":\"MechJeb2\",\"mod\":\"CKANMeta\",\"ctx\":\"MechJeb2\"}";

        var provider = ModTextProvider.FromJsonLines(new[] { keepJsonl });
        var resultKeep = provider.Find("MechJeb2", "name", "MechJeb2");
        Assert.That(resultKeep, Is.Null);

        var resultMissing = provider.Find("OtherMod", "abstract", "Unseen text");
        Assert.That(resultMissing, Is.Null);
    }
}
