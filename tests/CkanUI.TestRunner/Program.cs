using System.Reflection;
using NUnitLite;

// 进程内跑全部测试程序集：每个测试项目仍是普通 NUnit 类库，CI 用 dotnet test，
// 本地用本入口（沙箱里 VSTest testhost 无法打开父进程句柄）。
var assemblies = new[]
{
    typeof(CkanUI.Core.Tests.BuildInfoTests).Assembly,
    typeof(CkanUI.Architecture.Tests.DependencyRulesTests).Assembly,
    typeof(CkanUI.Localization.Tests.LocalizerTests).Assembly,
    typeof(CkanUI.ModText.Tests.ModTextTests).Assembly,
    typeof(CkanUI.Services.Tests.LoggingSetupTests).Assembly,
    typeof(CkanUI.Sfs.Tests.SfsDocumentTests).Assembly,
    typeof(CkanUI.ViewModels.Tests.ShellViewModelTests).Assembly,
};

var exitCode = 0;
foreach (var assembly in assemblies)
{
    Console.WriteLine($"== {assembly.GetName().Name} ==");
    var result = new AutoRun(assembly).Execute(new[] { "--noresult", "--labels=Off" });
    if (result != 0)
    {
        exitCode = result;
    }
}

return exitCode;
