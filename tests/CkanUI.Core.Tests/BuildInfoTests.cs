using CkanUI.Core;
using NUnit.Framework;

namespace CkanUI.Core.Tests;

[TestFixture]
public sealed class BuildInfoTests
{
    [Test]
    public void PackId_IsCkanUI() => Assert.That(BuildInfo.PackId, Is.EqualTo("CkanUI"));
}
