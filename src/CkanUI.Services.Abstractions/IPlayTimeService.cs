using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.Services.Abstractions;

/// <summary>读取并更新 UI 自有的实例游戏时长。</summary>
public interface IPlayTimeService
{
    /// <summary>读取持久化的累计时长；文件缺失或损坏返回空列表。</summary>
    IReadOnlyList<PlayTimeEntry> Load();

    /// <summary>覆盖指定实例的累计时长（小时），写入成功后返回真。</summary>
    bool Save(string instanceName, double hours);
}
