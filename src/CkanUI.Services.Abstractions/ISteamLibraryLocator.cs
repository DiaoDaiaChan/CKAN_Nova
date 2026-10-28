namespace CkanUI.Services.Abstractions;

/// <summary>
/// Steam 库探测（03 篇 §5「添加实例 → 自动查找」，06 篇 UI-32「Steam 导入」）。
/// </summary>
/// <remarks>
/// <para>只回答「Steam 库里有哪些疑似 KSP 的安装目录」，不碰实例注册表、不去重、不加实例——
/// 这些是视图模型层的事。</para>
/// <para>实现要读注册表与 vdf：注册表只用 <c>HKCU\Software\Valve\Steam\SteamPath</c>（上游
/// <c>external/CKAN/Core/IO/SteamLibrary.cs:145-151</c> 的唯一来源，没有 HKLM 32/64 回退），
/// vdf 读 <c>&lt;根&gt;/config/libraryfolders.vdf</c>，读不到再退到旧版
/// <c>&lt;根&gt;/steamapps/libraryfolders.vdf</c>。</para>
/// </remarks>
public interface ISteamLibraryLocator
{
    /// <summary>返回 Steam 库中疑似 KSP 的安装目录（未与实例注册表去重）。</summary>
    /// <param name="ct">取消令牌。</param>
    Task<OperationResult<IReadOnlyList<string>>> DiscoverGameDirsAsync(CancellationToken ct = default);
}
