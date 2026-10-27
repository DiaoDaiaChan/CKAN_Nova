namespace CkanUI.Core;

/// <summary>版本与标识常量。PackId 与版本号格式只允许在这里出现一次（01 篇 §16）。</summary>
public static class BuildInfo
{
    public const string PackId = "CkanUI";
    public const string ProductName = "CKAN UI";
    public const string Publisher = "CKAN Contributors";
    public const string DataDirectoryName = "CKAN";

    public static string Version => typeof(BuildInfo).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
}
