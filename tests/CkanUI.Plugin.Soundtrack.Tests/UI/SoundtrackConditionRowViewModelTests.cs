using CkanUI.Plugin.Soundtrack.UI;
using NUnit.Framework;

namespace CkanUI.Plugin.Soundtrack.Tests.UI;

/// <summary>
/// 条件行选择器：枚举/布尔/数字/天体名在界面上怎么变成能写回 cfg 的文本。
/// </summary>
[TestFixture]
public sealed class SoundtrackConditionRowViewModelTests
{
    [Test]
    public void 多选条件勾选后按目录顺序拼成竖线分隔()
    {
        var row = new SoundtrackConditionRowViewModel(null, "scene", "VAB");
        var sph = row.OptionItems.Single(option => option.Value == "SPH");

        sph.IsSelected = true;

        Assert.Multiple(() =>
        {
            Assert.That(row.IsFlags, Is.True);
            Assert.That(row.Draft, Is.EqualTo("VAB | SPH"));
            Assert.That(row.FlagsSummary, Does.Contain("载具组装厂"));
            Assert.That(row.HasPendingEdit, Is.True);
        });

        sph.IsSelected = false;
        Assert.That(row.Draft, Is.EqualTo("VAB"));
    }

    [Test]
    public void 布尔条件用下拉值写True或False()
    {
        var row = new SoundtrackConditionRowViewModel(null, "inAtmosphere", "False");
        var yes = row.OptionItems.Single(option => option.Value == "True");

        row.SelectedOption = yes;

        Assert.Multiple(() =>
        {
            Assert.That(row.IsBoolean, Is.True);
            Assert.That(row.Draft, Is.EqualTo("True"));
            Assert.That(row.SelectedOption!.Value, Is.EqualTo("True"));
        });
    }

    [Test]
    public void 数值条件接受小数并拒绝空值()
    {
        var row = new SoundtrackConditionRowViewModel(null, "minAltitude", "1000");

        row.NumberDraft = 2500.5m;
        Assert.That(row.Draft, Is.EqualTo("2500.5"));

        row.NumberDraft = null;
        Assert.That(row.Draft, Is.EqualTo("2500.5"), "清空数字框不应把已有草稿变成空串。");
    }

    [Test]
    public void 天体名使用可搜索建议并去重排序()
    {
        var row = new SoundtrackConditionRowViewModel(null, "bodyName", "Kerbin");

        row.SetBodyOptions(["Mun", "kerbin", "Duna", " ", "Mun"]);

        Assert.Multiple(() =>
        {
            Assert.That(row.IsBodyName, Is.True);
            Assert.That(row.IsOtherText, Is.False);
            Assert.That(row.BodyOptions, Is.EqualTo(new[] { "Duna", "kerbin", "Mun" }), "去重按忽略大小写，但保留第一次出现的写法。");
        });
    }

    [Test]
    public void 勾选已知场景时不丢掉目录不认识的模组场景()
    {
        var row = new SoundtrackConditionRowViewModel(null, "scene", "VAB | ModScene");

        Assert.That(row.FlagsSummary, Does.Contain("ModScene（未识别）"));

        row.OptionItems.Single(option => option.Value == "SPH").IsSelected = true;

        Assert.That(row.Draft, Is.EqualTo("VAB | SPH | ModScene"), "模组场景要原样留在草稿里，保存校验再决定收不收。");

        row.OptionItems.Single(option => option.Value == "VAB").IsSelected = false;

        Assert.That(row.Draft, Is.EqualTo("SPH | ModScene"));
    }

    [Test]
    public void 未知键保持只读识别并按文本编辑()
    {
        var row = new SoundtrackConditionRowViewModel(null, "futureKey", "anything");

        Assert.Multiple(() =>
        {
            Assert.That(row.IsKnown, Is.False);
            Assert.That(row.ValueHint, Does.Contain("playWhen"));
            Assert.That(row.Draft, Is.EqualTo("anything"));
        });
    }
}
