using System;
using System.Collections.Generic;
using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.ViewModels.Pages.ModPacks;

/// <summary>modpack 编辑器的中文文案与枚举标签（页面与行视图模型共用）。</summary>
public static class ModPackEditorText
{
    /// <summary>页面标题。</summary>
    public const string PageTitle = "modpack 编辑器";

    /// <summary>关系类型的中文标签。</summary>
    public static string RelationTypeLabel(string? relationType) => relationType switch
    {
        ModPackRelationTypes.Depends => "依赖",
        ModPackRelationTypes.Recommends => "推荐",
        ModPackRelationTypes.Suggests => "建议",
        ModPackRelationTypes.Conflicts => "冲突",
        ModPackRelationTypes.Supports => "支持",
        _ => "未知关系",
    };

    /// <summary>语义模式的中文标签。</summary>
    public static string ModeLabel(string? mode) => mode switch
    {
        ModPackRelationModes.Single => "单选",
        ModPackRelationModes.Multi => "多选",
        ModPackRelationModes.AnyOf => "任意一个",
        _ => "未知模式",
    };

    /// <summary>发布状态的中文标签。</summary>
    public static string ReleaseStatusLabel(string? status) => status switch
    {
        "stable" => "稳定",
        "testing" => "测试",
        "development" => "开发",
        _ => status ?? "—",
    };

    /// <summary>包类型的中文标签。</summary>
    public static string KindLabel(string? kind) => kind switch
    {
        "metapackage" => "元数据包（metapackage）",
        "package" => "实体包（package）",
        "dlc" => "官方 DLC（dlc）",
        _ => kind ?? "—",
    };

    /// <summary>把一条校验问题拼成"级别 · 位置 · 原因"的一行文字。</summary>
    public static string DescribeIssue(ModPackEditorIssue issue)
    {
        ArgumentNullException.ThrowIfNull(issue);
        var severity = issue.Severity == ModPackEditorIssueSeverity.Error ? "错误" : "提醒";
        var location = issue.RowIndex >= 0 ? "第 " + (issue.RowIndex + 1) + " 行" : FieldLabel(issue.Field);
        return string.IsNullOrWhiteSpace(location)
            ? severity + "：" + issue.Message
            : severity + "（" + location + "）：" + issue.Message;
    }

    /// <summary>字段名 → 中文字段名。</summary>
    public static string FieldLabel(string? field) => field switch
    {
        null or "" => "整份文档",
        "FilePath" => "保存路径",
        "Identifier" => "标识",
        "Name" => "名称",
        "Abstract" => "摘要",
        "Description" => "描述",
        "Authors" => "作者",
        "Version" => "版本",
        "Licenses" => "许可证",
        "ReleaseStatus" => "发布状态",
        "Kind" => "包类型",
        "KspVersion" => "目标游戏版本",
        "KspVersionMin" => "游戏版本下限",
        "KspVersionMax" => "游戏版本上限",
        "SpecVersion" => "规范版本",
        "DownloadUrl" => "下载地址",
        "Relations" => "关系",
        "RelationType" => "关系类型",
        "Mode" => "语义模式",
        "Candidates" => "候选标识",
        _ => field!,
    };

    /// <summary>关系类型下拉的展示项（值 → 标签）。</summary>
    public static IReadOnlyList<string> RelationTypeOptions { get; } = ModPackRelationTypes.All;

    /// <summary>语义模式下拉的展示项。</summary>
    public static IReadOnlyList<string> ModeOptions { get; } = ModPackRelationModes.All;

    /// <summary>发布状态下拉的展示项。</summary>
    public static IReadOnlyList<string> ReleaseStatusOptions { get; } = new[] { "stable", "testing", "development" };

    /// <summary>包类型下拉的展示项。</summary>
    public static IReadOnlyList<string> KindOptions { get; } = new[] { "metapackage", "package", "dlc" };
}
