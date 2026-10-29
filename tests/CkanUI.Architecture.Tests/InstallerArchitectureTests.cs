using System.IO;
using System.Linq;
using NUnit.Framework;

namespace CkanUI.Architecture.Tests;

/// <summary>
/// 架构门禁：原生安装程序 CkanUISetup 与发布出包流水线约束。
/// 确保原生安装程序具有正确的工程配置、严格的系统安全守卫、完整的静默安装参数协议以及发布流水线接线。
/// </summary>
[TestFixture]
public sealed class InstallerArchitectureTests
{
    private static string Read(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { RepoRoot.Path }.Concat(parts).ToArray()));

    [Test]
    public void InstallerProject_ConfiguredAsNativeWindowsApplication()
    {
        var csproj = Read("tools", "installer", "CkanUI.Tools.Installer.csproj");

        Assert.That(csproj, Does.Contain("<OutputType>WinExe</OutputType>"), "安装器必须为 Windows GUI 应用程序（WinExe，避免控制台黑框闪现）。");
        Assert.That(csproj, Does.Contain("<AssemblyName>CkanUISetup</AssemblyName>"), "安装器产物程序集名必须为 CkanUISetup。");
        Assert.That(csproj, Does.Contain("<TargetFramework>net10.0</TargetFramework>"), "安装器目标框架与全仓保持一致 net10.0。");
        Assert.That(csproj, Does.Contain("<AllowUnsafeBlocks>true</AllowUnsafeBlocks>"), "安装器使用 LibraryImport 需要允许不安全代码块。");
        Assert.That(csproj, Does.Contain("<PayloadZip"), "安装器工程支持通过 MSBuild 属性内嵌 payload.zip。");
    }

    [Test]
    public void InstallerCode_EnforcesSafetyGuards()
    {
        var uninstaller = Read("tools", "installer", "UninstallerEngine.cs");

        Assert.That(uninstaller, Does.Contain("ValidateInstallDirectoryGuard"), "卸载器必须包含安装目录安全守卫函数。");
        Assert.That(uninstaller, Does.Contain("ForbiddenLeaves"), "守卫必须包含敏感保留目录黑名单。");
        Assert.That(uninstaller, Does.Contain("Path.GetPathRoot"), "守卫必须拦截对磁盘驱动器根目录的误删请求。");
        Assert.That(uninstaller, Does.Contain("hasMarker"), "守卫必须在删除前校验 CKAN UI 安装标记。");
    }

    [Test]
    public void InstallerCode_ImplementsFullCommandLineOptions()
    {
        var options = Read("tools", "installer", "InstallerOptions.cs");

        Assert.That(options, Does.Contain("-installdir"), "必须支持 -installdir 参数。");
        Assert.That(options, Does.Contain("/DIR="), "必须兼容 Inno /DIR= 风格参数。");
        Assert.That(options, Does.Contain("-sourcedir"), "必须支持 -sourcedir 参数。");
        Assert.That(options, Does.Contain("-quiet"), "必须支持 -quiet 参数。");
        Assert.That(options, Does.Contain("-silent"), "必须支持 -silent 参数。");
        Assert.That(options, Does.Contain("/s"), "必须兼容 /s 静默参数。");
        Assert.That(options, Does.Contain("-noshortcuts"), "必须支持 -noshortcuts 参数。");
        Assert.That(options, Does.Contain("-nolaunch"), "必须支持 -nolaunch 参数。");
        Assert.That(options, Does.Contain("-force"), "必须支持 -force 参数。");
        Assert.That(options, Does.Contain("-uninstall"), "必须支持 -uninstall 参数。");
        Assert.That(options, Does.Contain("-removeuserdata"), "必须支持 -removeuserdata 参数。");
    }

    [Test]
    public void ReleaseScript_WiresCkanUISetupArtifact()
    {
        var script = Read("tools", "release", "release.ps1");

        Assert.That(script, Does.Contain("[switch] $NoInstaller"), "发布脚本必须支持 -NoInstaller 参数跳过安装器构建。");
        Assert.That(script, Does.Contain("CkanUI.Tools.Installer.csproj"), "发布脚本必须引用安装器工程文件。");
        Assert.That(script, Does.Contain("-setup"), "发布脚本必须将安装器命名包含 -setup。");
        Assert.That(script, Does.Contain("Sign-Artifacts.ps1"), "发布脚本必须复用代码签名脚本对安装器签名。");
        Assert.That(script, Does.Contain("PayloadZip="), "发布脚本必须将便携 zip 作为 PayloadZip 内嵌至安装器。");
    }
}
