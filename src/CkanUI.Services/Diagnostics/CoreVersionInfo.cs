using CKAN;

namespace CkanUI.Services.Diagnostics;

/// <summary>
/// 把 CKAN 库的版本事实暴露给上层（12 篇 F-55 关于框 / 设置页诊断区）。
/// 只有 Services 层允许出现 CKAN 命名空间（01 篇 §5），所以这层薄壳必须留在这里。
/// </summary>
public static class CoreVersionInfo
{
    /// <summary>CKAN 库版本，形如 <c>v1.36.0</c>（<see cref="Meta.ReleaseVersion"/> 自带 v 前缀）。</summary>
    public static string ReleaseVersion => Meta.ReleaseVersion.ToString();

    /// <summary>CKAN 产品名（通常是 "CKAN"）。</summary>
    public static string ProductName => Meta.ProductName;
}
