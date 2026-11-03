using CkanUI.Plugin.Rp1.Career;
using NUnit.Framework;

namespace CkanUI.Plugin.Rp1.Tests;

[TestFixture]
public sealed class Rp1GameDateTests
{
    [Test]
    public void FromUt_StartsAt1951()
        => Assert.That(Rp1GameDate.Format(0), Is.EqualTo("1951-01-01 00:00"));

    [Test]
    public void Format_UsesEarthCalendar()
    {
        Assert.That(Rp1GameDate.Format(86400), Is.EqualTo("1951-01-02 00:00"));
        Assert.That(Rp1GameDate.Format(31536000), Is.EqualTo("1952-01-01 00:00"));
    }

    [Test]
    public void Format_UnknownForNullNaNOrInfinity()
    {
        Assert.That(Rp1GameDate.Format(null), Is.EqualTo("时间未知"));
        Assert.That(Rp1GameDate.Format(double.NaN), Is.EqualTo("时间未知"));
        Assert.That(Rp1GameDate.Format(double.PositiveInfinity), Is.EqualTo("时间未知"));
    }
}
