using System.Collections.Generic;

namespace CkanUI.Services.Abstractions.Dtos;

/// <summary>存档工作台的一行键值（已带上词表标签与展示串，UI 不再做语义判断）。</summary>
public sealed record SaveOutlineFieldDto
{
    public string Path { get; init; } = string.Empty;

    public string Key { get; init; } = string.Empty;

    /// <summary>词表中文名；未知键回退成键名本身。</summary>
    public string Label { get; init; } = string.Empty;

    public string Value { get; init; } = string.Empty;

    /// <summary>带单位/前缀的展示串（资金 √15000、游戏时间 3y 42d）。</summary>
    public string DisplayValue { get; init; } = string.Empty;

    public int Line { get; init; }

    public string Kind { get; init; } = "Unknown";

    public string Group { get; init; } = string.Empty;

    public string SectionId { get; init; } = string.Empty;

    public int Priority { get; init; }

    public bool IsKnown { get; init; }

    public bool IsHighlighted { get; init; }
}

/// <summary>分区里可展开的块（飞行器/部件/场景）。</summary>
public sealed record SaveOutlineNodeDto
{
    public string Path { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string Label { get; init; } = string.Empty;

    public string Summary { get; init; } = string.Empty;

    public int Line { get; init; }

    public int Depth { get; init; }

    public int ChildCount { get; init; }

    public int PairCount { get; init; }

    public int NodeCount { get; init; }

    /// <summary>块估算字节数（大文件模式用来指认是哪个 mod 塞了大块数据）。</summary>
    public long ByteLength { get; init; }

    /// <summary>块的结束行（大文件模式直接给出，不必再展开）。</summary>
    public int EndLine { get; init; }

    public string SectionId { get; init; } = string.Empty;
}

/// <summary>一个展示分区（存档信息 / 资金 / 每个 SCENARIO / 乘员 / 消息 / 其余）。</summary>
public sealed record SaveOutlineSectionDto
{
    public string Id { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public string Subtitle { get; init; } = string.Empty;

    public string Kind { get; init; } = string.Empty;

    public int Rank { get; init; }

    public int Order { get; init; }

    public bool Truncated { get; init; }

    public int TotalPairCount { get; init; }

    public int TotalNodeCount { get; init; }

    public IReadOnlyList<SaveOutlineFieldDto> Fields { get; init; } = [];

    public IReadOnlyList<SaveOutlineNodeDto> Nodes { get; init; } = [];
}

/// <summary>整份存档的语义投影（SFS-05/06 的服务层输出）。</summary>
public sealed record SaveOutlineDto
{
    public string FilePath { get; init; } = string.Empty;

    public string SaveName { get; init; } = string.Empty;

    public string Version { get; init; } = string.Empty;

    public string Mode { get; init; } = string.Empty;

    public string Funds { get; init; } = "—";

    public string Science { get; init; } = "—";

    public string Reputation { get; init; } = "—";

    public string GameTime { get; init; } = "—";

    public int VesselCount { get; init; }

    public int PartCount { get; init; }

    public int ScenarioCount { get; init; }

    public int CrewCount { get; init; }

    public int PairCount { get; init; }

    public int NodeCount { get; init; }

    public int LineCount { get; init; }

    public bool Truncated { get; init; }

    public IReadOnlyList<SaveOutlineFieldDto> Highlights { get; init; } = [];

    public IReadOnlyList<SaveOutlineSectionDto> Sections { get; init; } = [];

    /// <summary>是否走了流式（大文件）路径：界面据此禁用整块复制/删除。</summary>
    public bool LargeFileMode { get; init; }

    /// <summary>大文件模式提示原文（含文件大小与限制），空串表示不需要提示。</summary>
    public string LargeFileNote { get; init; } = string.Empty;

    /// <summary>大到不能按普通块展开的块（多为 mod 私有数据），只列不展开。</summary>
    public IReadOnlyList<SaveOutlineNodeDto> OversizedBlocks { get; init; } = [];
}

/// <summary>搜索命中项：把行号、分区标题、键值和路径一起带出来，点一下就能定位。</summary>
public sealed record SaveOutlineMatchDto
{
    public int Line { get; init; }

    public string SectionTitle { get; init; } = string.Empty;

    public string Path { get; init; } = string.Empty;

    public string Key { get; init; } = string.Empty;

    public string Label { get; init; } = string.Empty;

    public string Value { get; init; } = string.Empty;

    public string DisplayValue { get; init; } = string.Empty;

    public string Match { get; init; } = string.Empty;

    /// <summary>命中片段之前的部分（界面用三段拼高亮，VM 不碰 Avalonia）。</summary>
    public string HighlightPrefix { get; init; } = string.Empty;

    /// <summary>真正命中的那段原文；没有可高亮片段时为空。</summary>
    public string HighlightHit { get; init; } = string.Empty;

    /// <summary>命中片段之后的部分。</summary>
    public string HighlightSuffix { get; init; } = string.Empty;
}

/// <summary>块详情：看 MODULE/资源/乘员明细用。</summary>
public sealed record SaveNodeDetailDto
{
    public string Path { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public int Line { get; init; }

    public bool Truncated { get; init; }

    public IReadOnlyList<SaveOutlineFieldDto> Fields { get; init; } = [];

    public IReadOnlyList<SaveOutlineNodeDto> Nodes { get; init; } = [];
}