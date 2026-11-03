using NUnit.Framework;

namespace CkanUI.Plugin.Performance.Tests;

/// <summary>
/// 夹具行尾的回归：样例文件是 JSON，签出时的行尾由 .gitattributes 的 `* text=auto` 决定。
/// 用例里会按 "\n" 拼出要替换的片段，所以读进来必须是 LF——否则换台机器重新签出就假红。
/// </summary>
[TestFixture]
public sealed class TelemetryFixtureLineEndingTests
{
    [Test]
    public void 样例读取后统一成LF()
    {
        Assert.Multiple(() =>
        {
            Assert.That(TelemetryFixtures.LatestJson, Does.Not.Contain("\r"));
            Assert.That(TelemetryFixtures.StatusJson, Does.Not.Contain("\r"));
        });
    }
}
