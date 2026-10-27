namespace CkanUI.Services.Abstractions.Dtos;

/// <summary>某 CKAN 实例累计游戏时间（小时）。</summary>
public sealed record PlayTimeEntry(string InstanceName, double Hours);
