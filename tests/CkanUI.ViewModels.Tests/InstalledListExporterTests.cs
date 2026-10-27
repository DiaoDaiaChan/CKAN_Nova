using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using CkanUI.Services;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using NUnit.Framework;

namespace CkanUI.ViewModels.Tests;

[TestFixture]
public sealed class InstalledListExporterTests
{
    private static List<InstalledModExportItem> CreateSampleMods()
    {
        return new List<InstalledModExportItem>
        {
            new(
                Identifier: "ModuleManager",
                Name: "Module Manager",
                InstalledVersion: "4.2.3",
                Author: "sarbian, Blowfish",
                Homepage: "https://forum.kerbalspaceprogram.com/index.php?/topic/50533-module-manager",
                DownloadUrl: "https://github.com/sarbian/ModuleManager/releases/download/v4.2.3/ModuleManager-4.2.3.zip",
                Description: "Modify scripts without modifying files.",
                License: "CC-BY-SA",
                DownloadSizeBytes: 123456),
            new(
                Identifier: "B9PartSwitch",
                Name: "B9 Part Switch",
                InstalledVersion: "v2.20.0",
                Author: "blowfish",
                Homepage: "https://github.com/blowfishpro/B9PartSwitch",
                DownloadUrl: null,
                Description: "Switch parts on the fly.",
                License: "LGPL-3.0",
                DownloadSizeBytes: 78910)
        };
    }

    [Test]
    public void ExportToString_NullOrEmptyList_DoesNotThrow()
    {
        foreach (InstalledListExportFormat format in Enum.GetValues<InstalledListExportFormat>())
        {
            var resNull = InstalledListExporter.ExportToString(null, format);
            var resEmpty = InstalledListExporter.ExportToString(Array.Empty<InstalledModExportItem>(), format);

            Assert.That(resNull, Is.Not.Null);
            Assert.That(resEmpty, Is.Not.Null);

            if (format == InstalledListExportFormat.PlainText)
            {
                Assert.That(resNull, Is.EqualTo(string.Empty));
                Assert.That(resEmpty, Is.EqualTo(string.Empty));
            }
            else if (format is InstalledListExportFormat.Csv or InstalledListExportFormat.Tsv)
            {
                StringAssert.Contains("Identifier", resNull);
                StringAssert.Contains("Identifier", resEmpty);
            }
        }
    }

    [Test]
    public void ExportToString_PlainText_FormatsExpectedLines()
    {
        var mods = CreateSampleMods();
        var result = InstalledListExporter.ExportToString(mods, InstalledListExportFormat.PlainText);

        StringAssert.Contains("B9 Part Switch v2.20.0", result);
        StringAssert.Contains("Module Manager 4.2.3", result);

        var lines = result.Split(new string[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        Assert.That(lines.Length, Is.EqualTo(2));
        Assert.That(lines[0], Is.EqualTo("B9 Part Switch v2.20.0"));
        Assert.That(lines[1], Is.EqualTo("Module Manager 4.2.3"));
    }

    [Test]
    public void ExportToString_PlainText_FallbackToIdentifierWhenNameEmpty()
    {
        var mods = new List<InstalledModExportItem>
        {
            new(Identifier: "OnlyIdMod", Name: "", InstalledVersion: "1.0.0")
        };
        var result = InstalledListExporter.ExportToString(mods, InstalledListExportFormat.PlainText);
        StringAssert.Contains("OnlyIdMod 1.0.0", result);
    }

    [Test]
    public void ExportToString_Markdown_GeneratesValidTable()
    {
        var mods = CreateSampleMods();
        var result = InstalledListExporter.ExportToString(mods, InstalledListExportFormat.Markdown);

        StringAssert.Contains("| Mod 名称 | 标识符 | 已装版本 | 作者 | 链接 | 描述 |", result);
        StringAssert.Contains("| :--- | :--- | :--- | :--- | :--- | :--- |", result);
        StringAssert.Contains("| B9 Part Switch | B9PartSwitch | v2.20.0 | blowfish | [主页](https://github.com/blowfishpro/B9PartSwitch) | Switch parts on the fly. |", result);
        StringAssert.Contains("| Module Manager | ModuleManager | 4.2.3 | sarbian, Blowfish | [主页](https://forum.kerbalspaceprogram.com/index.php?/topic/50533-module-manager) | Modify scripts without modifying files. |", result);
    }

    [Test]
    public void ExportToString_Markdown_EscapesPipeCharacters()
    {
        var mods = new List<InstalledModExportItem>
        {
            new(Identifier: "PipeMod", Name: "Pipe | In | Name", InstalledVersion: "1.0", Description: "Description | with pipe")
        };
        var result = InstalledListExporter.ExportToString(mods, InstalledListExportFormat.Markdown);

        StringAssert.Contains("Pipe \\| In \\| Name", result);
        StringAssert.Contains("Description \\| with pipe", result);
    }

    [Test]
    public void ExportToString_BbCode_GeneratesTableTags()
    {
        var mods = CreateSampleMods();
        var result = InstalledListExporter.ExportToString(mods, InstalledListExportFormat.BbCode);

        StringAssert.StartsWith("[table]", result);
        StringAssert.EndsWith("[/table]", result.TrimEnd());
        StringAssert.Contains("[th]Mod 名称[/th]", result);
        StringAssert.Contains("[td][b]Module Manager[/b][/td]", result);
        StringAssert.Contains("[url=https://forum.kerbalspaceprogram.com/index.php?/topic/50533-module-manager]主页[/url]", result);
    }

    [Test]
    public void ExportToString_BbCode_EscapesBrackets()
    {
        var mods = new List<InstalledModExportItem>
        {
            new(Identifier: "BracketMod", Name: "Mod [Lite]", InstalledVersion: "1.0")
        };
        var result = InstalledListExporter.ExportToString(mods, InstalledListExportFormat.BbCode);

        StringAssert.Contains("Mod &#91;Lite&#93;", result);
    }

    [Test]
    public void ExportToString_Csv_EscapesCommasAndQuotes()
    {
        var mods = new List<InstalledModExportItem>
        {
            new(
                Identifier: "TestMod",
                Name: "Test, \"Special\" Mod",
                InstalledVersion: "1.0.0",
                Author: "Author A, Author B",
                License: "MIT",
                DownloadSizeBytes: 1024)
        };
        var result = InstalledListExporter.ExportToString(mods, InstalledListExportFormat.Csv);

        var lines = result.Split(new string[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        Assert.That(lines.Length, Is.EqualTo(2));
        Assert.That(lines[0], Is.EqualTo("Identifier,Name,InstalledVersion,Author,License,DownloadSize"));
        Assert.That(lines[1], Is.EqualTo("TestMod,\"Test, \"\"Special\"\" Mod\",1.0.0,\"Author A, Author B\",MIT,1024"));
    }

    [Test]
    public void ExportToString_Tsv_DelimitsWithTabs()
    {
        var mods = new List<InstalledModExportItem>
        {
            new(
                Identifier: "TestMod",
                Name: "SimpleMod",
                InstalledVersion: "1.0.0",
                Author: "Author A",
                License: "GPL",
                DownloadSizeBytes: 2048)
        };
        var result = InstalledListExporter.ExportToString(mods, InstalledListExportFormat.Tsv);

        var lines = result.Split(new string[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        Assert.That(lines.Length, Is.EqualTo(2));
        Assert.That(lines[0], Is.EqualTo("Identifier\tName\tInstalledVersion\tAuthor\tLicense\tDownloadSize"));
        Assert.That(lines[1], Is.EqualTo("TestMod\tSimpleMod\t1.0.0\tAuthor A\tGPL\t2048"));
    }

    [Test]
    public void ExportToStream_WritesUtf8Stream()
    {
        var mods = CreateSampleMods();
        using var ms = new MemoryStream();
        InstalledListExporter.ExportToStream(mods, InstalledListExportFormat.PlainText, ms);

        var bytes = ms.ToArray();
        var str = Encoding.UTF8.GetString(bytes);

        StringAssert.Contains("Module Manager 4.2.3", str);
    }
}
