using CkanUI.Services.Logging;
using NUnit.Framework;

namespace CkanUI.Services.Tests;

[TestFixture]
public sealed class LoggingSetupTests
{
    [Test]
    public void GetLogger_ReturnsNamedLogger()
    {
        var log = LoggingSetup.GetLogger("CkanUI.Tests");
        Assert.That(log.Logger.Name, Is.EqualTo("CkanUI.Tests"));
    }
}
