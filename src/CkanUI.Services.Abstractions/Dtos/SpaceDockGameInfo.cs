namespace CkanUI.Services.Abstractions.Dtos;

/// <summary>SpaceDock 游戏条目（/api/games）。</summary>
/// <param name="Id">游戏数字 ID；浏览与版本接口都按它取数，不能硬编码。</param>
/// <param name="Name">显示名，例如 Kerbal Space Program。</param>
/// <param name="Short">URL 短名，例如 kerbal-space-program。</param>
public sealed record SpaceDockGameInfo(int Id, string Name, string Short);
