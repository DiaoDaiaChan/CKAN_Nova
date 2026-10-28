using CkanUI.Services.Abstractions.Logs;
using NUnit.Framework;

namespace CkanUI.Services.Tests;

/// <summary>LOG-04 搜索语法（14 篇 §4.2 ②）：过滤条与搜索框共用同一份解析结果。</summary>
[TestFixture]
sealed class LogSearchSyntaxTests
{
    [Test]
    public void Parse_EmptyInputIsUnfiltered()
    {
        var query = LogSearchSyntax.Parse("   ");

        Assert.That(query.IsUnfiltered, Is.True);
        Assert.That(query.Text, Is.Null);
        Assert.That(query.SafeTake, Is.EqualTo(500));
    }

    [Test]
    public void Parse_NullInputIsUnfiltered()
    {
        Assert.That(LogSearchSyntax.Parse(null).IsUnfiltered, Is.True);
    }

    [Test]
    public void Parse_FreeTextStaysText()
    {
        var query = LogSearchSyntax.Parse("RealChute failed");

        Assert.That(query.Text, Is.EqualTo("RealChute failed"));
        Assert.That(query.Levels, Is.Null);
        Assert.That(query.IsUnfiltered, Is.False);
    }

    [Test]
    public void Parse_LevelField()
    {
        var query = LogSearchSyntax.Parse("级别:error");

        Assert.That(query.Levels, Is.EqualTo(new[] { LogLevel.Error }));
        Assert.That(query.Text, Is.Null);
    }

    [TestCase("level:warn", LogLevel.Warn)]
    [TestCase("级别:warning", LogLevel.Warn)]
    [TestCase("level:err", LogLevel.Error)]
    [TestCase("级别:FATAL", LogLevel.Fatal)]
    [TestCase("level:user", LogLevel.User)]
    [TestCase("level:debug", LogLevel.Debug)]
    [TestCase("level:trace", LogLevel.Trace)]
    public void Parse_LevelAliases(string input, LogLevel expected)
    {
        var query = LogSearchSyntax.Parse(input);

        Assert.That(query.Levels, Is.EqualTo(new[] { expected }));
    }

    [Test]
    public void Parse_FamilyField()
    {
        var query = LogSearchSyntax.Parse("族:net");

        Assert.That(query.Families, Is.EqualTo(new[] { LogFamily.Net }));
    }

    [Test]
    public void Parse_FamilyFieldEnglishKey()
    {
        var query = LogSearchSyntax.Parse("family:Download");

        Assert.That(query.Families, Is.EqualTo(new[] { LogFamily.Download }));
    }

    [Test]
    public void Parse_UnknownFamilyFallsBackToText()
    {
        // 不能变成「过滤掉全部」——用户手误时最烦的就是空列表。
        var query = LogSearchSyntax.Parse("族:网络");

        Assert.That(query.Families, Is.Null);
        Assert.That(query.Text, Is.EqualTo("族:网络"));
    }

    [TestCase("源:user", LogSourceKind.User)]
    [TestCase("source:child", LogSourceKind.Child)]
    [TestCase("源:log4net", LogSourceKind.Log4Net)]
    [TestCase("source:ex", LogSourceKind.Exception)]
    [TestCase("source:ui", LogSourceKind.Ui)]
    [TestCase("source:import", LogSourceKind.Legacy)]
    public void Parse_SourceAliases(string input, LogSourceKind expected)
    {
        var query = LogSearchSyntax.Parse(input);

        Assert.That(query.Kinds, Is.EqualTo(new[] { expected }));
    }

    [TestCase("mod:RealChute")]
    [TestCase("模块:RealChute")]
    [TestCase("标签:RealChute")]
    [TestCase("tag:RealChute")]
    [TestCase("MOD:RealChute")]
    public void Parse_ModuleTagFields(string input)
    {
        var query = LogSearchSyntax.Parse(input);

        Assert.That(query.Tags, Is.Not.Null);
        Assert.That(query.Tags!["mod"], Is.EqualTo("RealChute"));
    }

    [Test]
    public void Parse_UnknownFieldStaysText()
    {
        // "http://..." 不能因为带冒号就被当成字段。
        var query = LogSearchSyntax.Parse("http://example.com");

        Assert.That(query.Text, Is.EqualTo("http://example.com"));
        Assert.That(query.IsUnfiltered, Is.False);
    }

    [Test]
    public void Parse_TrailingColonStaysText()
    {
        var query = LogSearchSyntax.Parse("foo:");

        Assert.That(query.Text, Is.EqualTo("foo:"));
    }

    [Test]
    public void Parse_CombinesFieldsAndText()
    {
        var query = LogSearchSyntax.Parse("级别:error 族:net mod:RealChute connection reset");

        Assert.That(query.Levels, Is.EqualTo(new[] { LogLevel.Error }));
        Assert.That(query.Families, Is.EqualTo(new[] { LogFamily.Net }));
        Assert.That(query.Tags!["mod"], Is.EqualTo("RealChute"));
        Assert.That(query.Text, Is.EqualTo("connection reset"));
    }

    [Test]
    public void Parse_MultipleLevelsAccumulate()
    {
        var query = LogSearchSyntax.Parse("级别:error 级别:warn");

        Assert.That(query.Levels, Is.EqualTo(new[] { LogLevel.Error, LogLevel.Warn }));
    }

    [Test]
    public void Describe_UnfilteredShowsAll()
    {
        Assert.That(LogSearchSyntax.Describe(LogQuery.All), Is.EqualTo("全部"));
    }

    [Test]
    public void Describe_RendersEveryPart()
    {
        var query = new LogQuery
        {
            Text = "boom",
            Levels = [LogLevel.Error],
            Families = [LogFamily.Net],
            Kinds = [LogSourceKind.User],
            Tags = new Dictionary<string, string> { ["mod"] = "RealChute" },
        };

        Assert.That(LogSearchSyntax.Describe(query), Is.EqualTo("boom 级别:error 族:net 源:user mod:RealChute"));
    }

    [Test]
    public void Parse_NormalizedTextIsLowercase()
    {
        var query = LogSearchSyntax.Parse("RealChute");

        Assert.That(query.NormalizedText, Is.EqualTo("realchute"));
    }

    [Test]
    public void Parse_SafeTakeDefaultsWhenNonPositive()
    {
        var query = new LogQuery { Take = 0, Skip = -5 };

        Assert.That(query.SafeTake, Is.EqualTo(500));
        Assert.That(query.SafeSkip, Is.Zero);
    }
}
