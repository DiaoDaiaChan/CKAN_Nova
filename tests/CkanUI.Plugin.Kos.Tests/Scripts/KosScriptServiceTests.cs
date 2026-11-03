using System.Globalization;
using System.Text;
using CkanUI.Plugin.Kos.Scripts;
using CkanUI.Services.Abstractions;
using NUnit.Framework;

namespace CkanUI.Plugin.Kos.Tests.Scripts;

/// <summary>
/// <see cref="KosScriptService"/> 的扫描、读取、保存、备份与外部改动检测。
/// </summary>
/// <remarks>
/// 夹具用系统临时目录下的 kos-&lt;guid&gt; 造“真实实例形状”的目录（Ships/Script、GameData/kOS），
/// 不碰本机 Steam 里的 KSP 安装；每个用例自带目录，Dispose 时递归删除。
/// 目录形状是必须的：Archive 卷就长在 Ships/Script 上，只用几个文件桩测不出扫描与路径校验的坑。
/// </remarks>
[TestFixture]
public sealed class KosScriptServiceTests
{
    private KosScriptService _service = null!;

    [SetUp]
    public void SetUp() => _service = new KosScriptService();

    [Test]
    public void LoadCatalog_没有ShipsScript时返回空态与期望路径()
    {
        using var game = new TempGameDirectory();

        var catalog = _service.LoadCatalog(game.Path);

        Assert.Multiple(() =>
        {
            Assert.That(catalog.GameDir, Is.EqualTo(game.Path));
            Assert.That(catalog.ScriptDirectory, Is.EqualTo(game.Combine("Ships", "Script")));
            Assert.That(catalog.DirectoryExists, Is.False);
            Assert.That(catalog.Scripts, Is.Empty);
            Assert.That(catalog.Diagnostics, Is.Empty);
        });
    }

    [Test]
    public void LoadCatalog_null实例路径不抛异常并给出诊断()
    {
        KosScriptCatalog catalog = null!;

        Assert.That(() => catalog = _service.LoadCatalog(null), Throws.Nothing);

        Assert.Multiple(() =>
        {
            Assert.That(catalog.DirectoryExists, Is.False);
            Assert.That(catalog.ScriptDirectory, Is.Empty);
            Assert.That(catalog.Diagnostics, Is.Not.Empty);
        });
    }

    [TestCase("")]
    [TestCase("   ")]
    public void LoadCatalog_空白实例路径不抛异常并给出诊断(string gameDir)
    {
        KosScriptCatalog catalog = null!;

        Assert.That(() => catalog = _service.LoadCatalog(gameDir), Throws.Nothing);

        Assert.Multiple(() =>
        {
            Assert.That(catalog.DirectoryExists, Is.False);
            Assert.That(catalog.Scripts, Is.Empty);
            Assert.That(catalog.Diagnostics, Has.Some.Contain("没有选择实例"));
        });
    }

    [Test]
    public void LoadCatalog_按GameData下kOS目录判断模组是否安装()
    {
        using var game = new TempGameDirectory();

        var before = _service.LoadCatalog(game.Path);
        Directory.CreateDirectory(game.Combine("GameData", "kOS"));
        var after = _service.LoadCatalog(game.Path);

        Assert.Multiple(() =>
        {
            Assert.That(before.ModInstalled, Is.False);
            Assert.That(after.ModInstalled, Is.True);
            // 装了 kOS 但 Ships/Script 还没有，是“装了但还没脚本”的空态，不是“没装”。
            Assert.That(after.DirectoryExists, Is.False);
        });
    }

    [Test]
    public void LoadCatalog_只收录ks扩展名并按文件名忽略大小写排序()
    {
        using var game = new TempGameDirectory();
        var directory = game.CreateScriptDirectory();
        File.WriteAllText(Path.Combine(directory, "zeta.ks"), "print 1.\n");
        File.WriteAllText(Path.Combine(directory, "Alpha.KS"), "print 2.\n");
        File.WriteAllText(Path.Combine(directory, "boot.KS"), "print 3.\n");
        File.WriteAllText(Path.Combine(directory, "notes.txt"), "not a script");
        File.WriteAllText(Path.Combine(directory, "backup.ks.bak-20240101-000000"), "not a script");

        var catalog = _service.LoadCatalog(game.Path);

        Assert.Multiple(() =>
        {
            Assert.That(catalog.DirectoryExists, Is.True);
            Assert.That(
                catalog.Scripts.Select(script => script.Name),
                Is.EqualTo(new[] { "Alpha.KS", "boot.KS", "zeta.ks" }));
            Assert.That(catalog.Scripts.Single(script => script.Name == "boot.KS").IsBoot, Is.True);
            Assert.That(catalog.Scripts.Single(script => script.Name == "Alpha.KS").IsBoot, Is.False);
            Assert.That(catalog.Scripts.Single(script => script.Name == "Alpha.KS").LineCount, Is.EqualTo(1));
            Assert.That(catalog.Diagnostics, Is.Empty);
        });
    }

    [Test]
    public void LoadCatalog_行数按末尾换行规则统计()
    {
        using var game = new TempGameDirectory();
        var directory = game.CreateScriptDirectory();
        File.WriteAllText(Path.Combine(directory, "empty.ks"), string.Empty);
        File.WriteAllText(Path.Combine(directory, "single.ks"), "print 1.");
        File.WriteAllText(Path.Combine(directory, "trailing.ks"), "print 1.\n");
        File.WriteAllText(Path.Combine(directory, "two.ks"), "print 1.\nprint 2.");
        File.WriteAllText(Path.Combine(directory, "crlf.ks"), "print 1.\r\nprint 2.\r\n");

        var catalog = _service.LoadCatalog(game.Path);

        Assert.Multiple(() =>
        {
            Assert.That(catalog.Scripts.Single(script => script.Name == "empty.ks").LineCount, Is.EqualTo(0));
            Assert.That(catalog.Scripts.Single(script => script.Name == "single.ks").LineCount, Is.EqualTo(1));
            Assert.That(catalog.Scripts.Single(script => script.Name == "trailing.ks").LineCount, Is.EqualTo(1));
            Assert.That(catalog.Scripts.Single(script => script.Name == "two.ks").LineCount, Is.EqualTo(2));
            Assert.That(catalog.Scripts.Single(script => script.Name == "crlf.ks").LineCount, Is.EqualTo(2));
            Assert.That(catalog.Diagnostics, Is.Empty);
        });
    }

    [Test]
    public void LoadCatalog_单个文件读不了不影响其它脚本()
    {
        using var game = new TempGameDirectory();
        var directory = game.CreateScriptDirectory();
        File.WriteAllText(Path.Combine(directory, "good.ks"), "print 1.\r\n");
        var lockedPath = Path.Combine(directory, "locked.ks");
        File.WriteAllText(lockedPath, "print 2.\r\n");

        using (new FileStream(lockedPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var catalog = _service.LoadCatalog(game.Path);

            Assert.Multiple(() =>
            {
                Assert.That(catalog.DirectoryExists, Is.True);
                Assert.That(catalog.Scripts.Select(script => script.Name), Does.Contain("good.ks"));
                Assert.That(catalog.Scripts.Single(script => script.Name == "good.ks").LineCount, Is.EqualTo(1));
                // 坏文件仍在列表里（界面要能显示它、让用户删掉它），只是行数缺失并进 Diagnostics。
                Assert.That(catalog.Scripts.Single(script => script.Name == "locked.ks").LineCount, Is.Null);
                Assert.That(catalog.Diagnostics, Has.Some.Contain("locked.ks"));
            });
        }
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase("no-extension")]
    [TestCase("script.txt")]
    [TestCase("sub/evil.ks")]
    [TestCase("sub\\evil.ks")]
    [TestCase("C:\\temp\\evil.ks")]
    [TestCase("c:evil.ks")]
    [TestCase("..")]
    [TestCase("../evil.ks")]
    [TestCase("a..ks")]
    [TestCase(" leading.ks")]
    [TestCase(".ks")]
    public void 非法文件名被拒绝且不落盘(string name)
    {
        using var game = new TempGameDirectory();

        var result = _service.Create(game.Path, name, "print 1.");

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Message, Is.Not.Null.And.Not.Empty);
        });
    }

    [Test]
    public void 遍历攻击名字被拒绝且不会写到Archive目录之外()
    {
        using var game = new TempGameDirectory();

        var createResult = _service.Create(game.Path, "../escaped.ks", "print 1.");
        var loadResult = _service.Load(game.Path, "../escaped.ks");
        var saveResult = _service.Save(game.Path, "../escaped.ks", "print 1.");
        var renameResult = _service.Rename(game.Path, "../escaped.ks", "ok.ks");
        var deleteResult = _service.Delete(game.Path, "../escaped.ks");

        Assert.Multiple(() =>
        {
            Assert.That(createResult.IsSuccess, Is.False);
            Assert.That(loadResult.IsSuccess, Is.False);
            Assert.That(saveResult.IsSuccess, Is.False);
            Assert.That(renameResult.IsSuccess, Is.False);
            Assert.That(deleteResult.IsSuccess, Is.False);
            // 校验发生在建目录之前：连 Ships/Script 都不该被创建，更不该有文件逃出去。
            Assert.That(Directory.Exists(game.Combine("Ships", "Script")), Is.False);
            Assert.That(Directory.GetFiles(game.Path, "*.ks", SearchOption.AllDirectories), Is.Empty);
        });
    }

    [Test]
    public void Load_不存在的脚本返回可读失败而不是抛异常()
    {
        using var game = new TempGameDirectory();
        game.CreateScriptDirectory();

        var result = _service.Load(game.Path, "missing.ks");

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Message, Does.Contain("不存在"));
        });
    }

    [Test]
    public void Load_识别UTF8BOM并剥掉()
    {
        using var game = new TempGameDirectory();
        var directory = game.CreateScriptDirectory();
        var path = Path.Combine(directory, "bom.ks");
        File.WriteAllText(path, "print 1.\r\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        var result = _service.Load(game.Path, "bom.ks");

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True, result.Message);
            Assert.That(result.Value!.HadBom, Is.True);
            Assert.That(result.Value.Content, Is.EqualTo("print 1.\n"));
            Assert.That(result.Value.Content.StartsWith('\uFEFF'), Is.False);
        });
    }

    [Test]
    public void Load_换行统一成LF并保留原始换行风格()
    {
        using var game = new TempGameDirectory();
        var directory = game.CreateScriptDirectory();
        File.WriteAllText(Path.Combine(directory, "crlf.ks"), "print 1.\r\nprint 2.\r\n");
        File.WriteAllText(Path.Combine(directory, "lf.ks"), "print 1.\nprint 2.\n");
        File.WriteAllText(Path.Combine(directory, "cr.ks"), "print 1.\rprint 2.");

        var crlf = _service.Load(game.Path, "crlf.ks");
        var lf = _service.Load(game.Path, "lf.ks");
        var cr = _service.Load(game.Path, "cr.ks");

        Assert.Multiple(() =>
        {
            Assert.That(crlf.IsSuccess, Is.True, crlf.Message);
            Assert.That(crlf.Value!.NewLine, Is.EqualTo("\r\n"));
            Assert.That(crlf.Value.Content, Is.EqualTo("print 1.\nprint 2.\n"));
            Assert.That(crlf.Value.HadBom, Is.False);
            Assert.That(lf.Value!.NewLine, Is.EqualTo("\n"));
            Assert.That(cr.Value!.NewLine, Is.EqualTo("\n"));
            Assert.That(cr.Value.Content, Is.EqualTo("print 1.\nprint 2."));
        });
    }

    [Test]
    public void Load_超过2MB的脚本给可读失败而不是塞进编辑器()
    {
        using var game = new TempGameDirectory();
        var directory = game.CreateScriptDirectory();
        var path = Path.Combine(directory, "huge.ks");
        File.WriteAllText(path, new string('a', (int)KosScriptService.MaxScriptBytes + 1));

        var result = _service.Load(game.Path, "huge.ks");
        var catalog = _service.LoadCatalog(game.Path);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Message, Does.Contain("2 MB"));
            Assert.That(catalog.Scripts.Single(script => script.Name == "huge.ks").LineCount, Is.Null);
            Assert.That(catalog.Diagnostics, Has.Some.Contain("huge.ks"));
        });
    }

    [Test]
    public void Load_正好2MB仍能打开()
    {
        using var game = new TempGameDirectory();
        var directory = game.CreateScriptDirectory();
        var path = Path.Combine(directory, "exact.ks");
        File.WriteAllText(path, new string('a', (int)KosScriptService.MaxScriptBytes));

        var result = _service.Load(game.Path, "exact.ks");

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True, result.Message);
            Assert.That(result.Value!.SizeBytes, Is.EqualTo(KosScriptService.MaxScriptBytes));
            Assert.That(result.Value.Content, Has.Length.EqualTo((int)KosScriptService.MaxScriptBytes));
        });
    }

    [Test]
    public void Save_按原换行写回且不强行补末尾换行()
    {
        using var game = new TempGameDirectory();
        var path = game.WriteScript("main.ks", "print 1.\nprint 2.\n");
        var document = _service.Load(game.Path, "main.ks").Value!;

        var result = _service.Save(game.Path, "main.ks", "print 1.\nprint 2.", document.LastWriteUtc);

        var bytes = File.ReadAllBytes(path);
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True, result.Message);
            Assert.That(Encoding.UTF8.GetString(bytes), Is.EqualTo("print 1.\r\nprint 2."));
            Assert.That(bytes[0], Is.Not.EqualTo(0xEF));
            Assert.That(result.Value!.LineCount, Is.EqualTo(2));
            Assert.That(result.Value.SizeBytes, Is.EqualTo(bytes.Length));
        });
    }

    [Test]
    public void Save_LF文件写回仍是LF且输入里的CRLF会被归一()
    {
        using var game = new TempGameDirectory();
        var path = game.WriteScript("lf.ks", "print 1.\nprint 2.\n", newLine: "\n");
        var document = _service.Load(game.Path, "lf.ks").Value!;
        Assert.That(document.NewLine, Is.EqualTo("\n"));

        var result = _service.Save(game.Path, "lf.ks", "print 1.\r\nprint 2.", document.LastWriteUtc);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True, result.Message);
            Assert.That(File.ReadAllText(path), Is.EqualTo("print 1.\nprint 2."));
            Assert.That(File.ReadAllText(path), Does.Not.Contain("\r"));
        });
    }

    [Test]
    public void Save_正文null按空处理()
    {
        using var game = new TempGameDirectory();
        var path = game.WriteScript("empty.ks", "print 1.\n");
        var document = _service.Load(game.Path, "empty.ks").Value!;

        var result = _service.Save(game.Path, "empty.ks", null!, document.LastWriteUtc);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True, result.Message);
            Assert.That(new FileInfo(path).Length, Is.Zero);
            Assert.That(result.Value!.LineCount, Is.Zero);
        });
    }

    [Test]
    public void Save_覆盖前生成时间戳备份且备份保留旧内容()
    {
        using var game = new TempGameDirectory();
        var path = game.WriteScript("main.ks", "print old.\n");
        var document = _service.Load(game.Path, "main.ks").Value!;

        var result = _service.Save(game.Path, "main.ks", "print new.\n", document.LastWriteUtc);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True, result.Message);
            Assert.That(result.Value!.BackupPath, Is.Not.Null.And.Not.Empty);
            Assert.That(File.Exists(result.Value.BackupPath), Is.True);
            Assert.That(Path.GetDirectoryName(result.Value.BackupPath), Is.EqualTo(Path.GetDirectoryName(path)));
            Assert.That(
                Path.GetFileName(result.Value.BackupPath!),
                Does.Match(@"^main\.ks\.bak-\d{8}-\d{6}(-\d+)?$"));
            Assert.That(File.ReadAllText(result.Value.BackupPath!), Is.EqualTo("print old.\r\n"));
            Assert.That(File.ReadAllText(path), Is.EqualTo("print new.\r\n"));
        });
    }

    [Test]
    public void Save_连续保存两次保留两份备份()
    {
        using var game = new TempGameDirectory();
        game.WriteScript("main.ks", "print 1.\n");
        var document = _service.Load(game.Path, "main.ks").Value!;

        var first = _service.Save(game.Path, "main.ks", "print 2.\n", document.LastWriteUtc);
        var second = _service.Save(game.Path, "main.ks", "print 3.\n");

        Assert.Multiple(() =>
        {
            Assert.That(first.IsSuccess, Is.True, first.Message);
            Assert.That(second.IsSuccess, Is.True, second.Message);
            Assert.That(Directory.GetFiles(game.Combine("Ships", "Script"), "main.ks.bak-*"), Has.Length.EqualTo(2));
        });
    }

    [Test]
    public void Save_同一秒已有同名备份时改用后缀且不覆盖旧备份()
    {
        using var game = new TempGameDirectory();
        var path = game.WriteScript("main.ks", "print 1.\n");
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var squatted = path + ".bak-" + stamp;
        File.WriteAllText(squatted, "pre-existing");

        var result = _service.Save(game.Path, "main.ks", "print 2.\n");

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True, result.Message);
            Assert.That(result.Value!.BackupPath, Is.Not.EqualTo(squatted));
            Assert.That(File.ReadAllText(squatted), Is.EqualTo("pre-existing"));
        });
    }

    [Test]
    public void Save_磁盘时间被外部改过超过一秒时拒绝覆盖()
    {
        using var game = new TempGameDirectory();
        var path = game.WriteScript("main.ks", "print old.\n");
        var document = _service.Load(game.Path, "main.ks").Value!;
        File.SetLastWriteTimeUtc(path, document.LastWriteUtc.AddSeconds(5));

        var result = _service.Save(game.Path, "main.ks", "print new.\n", document.LastWriteUtc);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Message, Does.Contain("外部修改"));
            Assert.That(File.ReadAllText(path), Is.EqualTo("print old.\r\n"));
            // 拒绝发生在备份之前：不该留下没有意义的备份。
            Assert.That(Directory.GetFiles(game.Combine("Ships", "Script"), "*.bak-*"), Is.Empty);
        });
    }

    [Test]
    public void Save_一秒内的时钟抖动不算外部修改()
    {
        using var game = new TempGameDirectory();
        var path = game.WriteScript("main.ks", "print old.\n");
        var document = _service.Load(game.Path, "main.ks").Value!;
        File.SetLastWriteTimeUtc(path, document.LastWriteUtc.AddMilliseconds(400));

        var result = _service.Save(game.Path, "main.ks", "print new.\n", document.LastWriteUtc);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True, result.Message);
            Assert.That(File.ReadAllText(path), Is.EqualTo("print new.\r\n"));
        });
    }

    [Test]
    public void Save_不存在的脚本返回可读失败()
    {
        using var game = new TempGameDirectory();
        game.CreateScriptDirectory();

        var result = _service.Save(game.Path, "missing.ks", "print 1.");

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Message, Does.Contain("不存在"));
        });
    }

    [Test]
    public void Save_替换失败时返回失败且不留下临时文件()
    {
        using var game = new TempGameDirectory();
        var directory = game.CreateScriptDirectory();
        var path = Path.Combine(directory, "readonly.ks");
        File.WriteAllText(path, "print old.\r\n");
        File.SetAttributes(path, FileAttributes.ReadOnly);

        OperationResult<KosScriptSaveOutcome> result;
        try
        {
            result = _service.Save(game.Path, "readonly.ks", "print new.\n");
        }
        finally
        {
            File.SetAttributes(path, FileAttributes.Normal);
        }

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Message, Is.Not.Null.And.Not.Empty);
            Assert.That(Directory.GetFiles(directory, "*.tmp-*"), Is.Empty);
            Assert.That(File.ReadAllText(path), Is.EqualTo("print old.\r\n"));
        });
    }

    [Test]
    public void Create_自动创建目录并写CRLF无BOM()
    {
        using var game = new TempGameDirectory();

        var result = _service.Create(game.Path, "hello.ks", "print 1.\nprint 2.");

        var path = game.Combine("Ships", "Script", "hello.ks");
        var bytes = File.ReadAllBytes(path);
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True, result.Message);
            Assert.That(result.Value!.BackupPath, Is.Null);
            Assert.That(result.Value.Name, Is.EqualTo("hello.ks"));
            Assert.That(result.Value.LineCount, Is.EqualTo(2));
            Assert.That(result.Value.SizeBytes, Is.EqualTo(bytes.Length));
            Assert.That(Encoding.UTF8.GetString(bytes), Is.EqualTo("print 1.\r\nprint 2."));
            Assert.That(bytes[0], Is.Not.EqualTo(0xEF));
        });
    }

    [Test]
    public void Create_同名已存在时失败且不覆盖()
    {
        using var game = new TempGameDirectory();
        var path = game.WriteScript("main.ks", "keep\n");

        var result = _service.Create(game.Path, "main.ks", "overwrite\n");

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Message, Does.Contain("已存在"));
            Assert.That(File.ReadAllText(path), Is.EqualTo("keep\r\n"));
        });
    }

    [Test]
    public void Create_扩展名统一成小写ks()
    {
        using var game = new TempGameDirectory();

        var result = _service.Create(game.Path, "Hello.KS", "print 1.");

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True, result.Message);
            Assert.That(result.Value!.Name, Is.EqualTo("Hello.ks"));
            Assert.That(File.Exists(game.Combine("Ships", "Script", "Hello.ks")), Is.True);
        });
    }

    [Test]
    public void Rename_成功改名并返回新名字()
    {
        using var game = new TempGameDirectory();
        var oldPath = game.WriteScript("old.ks", "print 1.\n");

        var result = _service.Rename(game.Path, "old.ks", "new.ks");

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True, result.Message);
            Assert.That(result.Value, Is.EqualTo("new.ks"));
            Assert.That(File.Exists(oldPath), Is.False);
            Assert.That(File.ReadAllText(game.Combine("Ships", "Script", "new.ks")), Is.EqualTo("print 1.\r\n"));
        });
    }

    [Test]
    public void Rename_目标已存在时失败且不覆盖()
    {
        using var game = new TempGameDirectory();
        var oldPath = game.WriteScript("old.ks", "print old.\n");
        var newPath = game.WriteScript("new.ks", "keep\n");

        var result = _service.Rename(game.Path, "old.ks", "new.ks");

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Message, Does.Contain("已存在"));
            Assert.That(File.Exists(oldPath), Is.True);
            Assert.That(File.ReadAllText(newPath), Is.EqualTo("keep\r\n"));
        });
    }

    [Test]
    public void Rename_原文件不存在时返回可读失败()
    {
        using var game = new TempGameDirectory();
        game.CreateScriptDirectory();

        var result = _service.Rename(game.Path, "missing.ks", "new.ks");

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Message, Does.Contain("不存在"));
        });
    }

    [Test]
    public void Delete_先备份再删除并返回备份路径()
    {
        using var game = new TempGameDirectory();
        var path = game.WriteScript("gone.ks", "print gone.\n");

        var result = _service.Delete(game.Path, "gone.ks");

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True, result.Message);
            Assert.That(File.Exists(path), Is.False);
            Assert.That(result.Value, Is.Not.Null.And.Not.Empty);
            Assert.That(File.Exists(result.Value), Is.True);
            Assert.That(
                Path.GetFileName(result.Value!),
                Does.Match(@"^gone\.ks\.bak-\d{8}-\d{6}(-\d+)?$"));
            Assert.That(File.ReadAllText(result.Value!), Is.EqualTo("print gone.\r\n"));
        });
    }

    [Test]
    public void Delete_文件不存在时返回可读失败()
    {
        using var game = new TempGameDirectory();
        game.CreateScriptDirectory();

        var result = _service.Delete(game.Path, "missing.ks");

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Message, Does.Contain("不存在"));
        });
    }

    /// <summary>系统临时目录下的 kos-&lt;guid&gt; 假实例；Dispose 时递归删除。</summary>
    private sealed class TempGameDirectory : IDisposable
    {
        public TempGameDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "kos-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string Combine(params string[] parts) =>
            System.IO.Path.Combine(new[] { Path }.Concat(parts).ToArray());

        public string CreateScriptDirectory()
        {
            var directory = Combine("Ships", "Script");
            Directory.CreateDirectory(directory);
            return directory;
        }

        /// <summary>写一个脚本；content 用 \n 表达换行，按 newLine 落盘。</summary>
        public string WriteScript(string name, string content, bool bom = false, string newLine = "\r\n")
        {
            var directory = CreateScriptDirectory();
            var path = System.IO.Path.Combine(directory, name);
            var text = string.Equals(newLine, "\n", StringComparison.Ordinal)
                ? content
                : content.Replace("\n", newLine, StringComparison.Ordinal);
            File.WriteAllText(path, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: bom));
            return path;
        }

        public void Dispose()
        {
            try
            {
                if (!Directory.Exists(Path))
                {
                    return;
                }

                ClearReadOnly(Path);
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
                // 系统临时目录清理失败不该把用例判红。
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private static void ClearReadOnly(string directory)
        {
            foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                var attributes = File.GetAttributes(file);
                if ((attributes & FileAttributes.ReadOnly) != 0)
                {
                    File.SetAttributes(file, attributes & ~FileAttributes.ReadOnly);
                }
            }
        }
    }
}
