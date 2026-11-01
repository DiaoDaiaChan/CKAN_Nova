using CkanUI.Services.Abstractions.Dtos;
using CkanUI.Services.Repositories;
using NUnit.Framework;

namespace CkanUI.Services.Tests;

[TestFixture]
public sealed class OfficialRepositoryDefaultsTests
{
    [Test]
    public void Seed_IsTheOfficialCkanMetaRepoOnly()
    {
        // 名字必须与 KSP-CKAN/repositories.json 第一条一致：Core 的「默认仓库」标记按名字匹配。
        Assert.That(OfficialRepositoryDefaults.PrimaryName, Is.EqualTo("KSP-default"));
        Assert.That(OfficialRepositoryDefaults.PrimaryUri,
            Is.EqualTo("https://github.com/KSP-CKAN/CKAN-meta/archive/master.tar.gz"));

        // 只写主仓库：镜像指向同一份 CKAN-meta，同时启用会让首装下载翻倍。
        Assert.That(OfficialRepositoryDefaults.Seed, Has.Count.EqualTo(1));
        Assert.That(OfficialRepositoryDefaults.Seed[0].Name, Is.EqualTo(OfficialRepositoryDefaults.PrimaryName));
        Assert.That(OfficialRepositoryDefaults.Seed[0].Uri, Is.EqualTo(OfficialRepositoryDefaults.PrimaryUri));
        Assert.That(OfficialRepositoryDefaults.Seed[0].Priority, Is.Zero);
    }

    [Test]
    public void PrimaryUri_IsAnAbsoluteHttpsUri()
    {
        Assert.That(Uri.TryCreate(OfficialRepositoryDefaults.PrimaryUri, UriKind.Absolute, out var uri), Is.True);
        Assert.That(uri!.Scheme, Is.EqualTo(Uri.UriSchemeHttps));
        Assert.That(uri.Host, Is.EqualTo("github.com"));
    }

    [Test]
    public void ShouldSeed_OnlyWhenRepositoryListIsEmpty()
    {
        Assert.That(OfficialRepositoryDefaults.ShouldSeed(null), Is.True);
        Assert.That(OfficialRepositoryDefaults.ShouldSeed(RepositoryEntryInfo.Empty), Is.True);
        Assert.That(OfficialRepositoryDefaults.ShouldSeed([Row()]), Is.False);
    }

    private static RepositoryEntryInfo Row()
        => new(OfficialRepositoryDefaults.PrimaryName, OfficialRepositoryDefaults.PrimaryUri, 0,
            null, false, false, true, false, null);
}
