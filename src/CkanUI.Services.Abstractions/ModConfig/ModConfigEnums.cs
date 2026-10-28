namespace CkanUI.Services.Abstractions.ModConfig;

/// <summary>
/// 字段类型（15 篇 §4.2）。值名称与 adapter.json 里的 `type` 字面量一一对应，
/// 因为 adapter 是"人写的声明"，任何重命名都会让已发布的 adapter 失效，故用 wire 名做唯一真源。
/// </summary>
public enum ModConfigFieldType
{
    /// <summary>开关。</summary>
    Bool = 0,

    /// <summary>整数。</summary>
    Int = 1,

    /// <summary>浮点。</summary>
    Float = 2,

    /// <summary>枚举下拉（白名单）。</summary>
    Enum = 3,

    /// <summary>单行文本。</summary>
    String = 4,

    /// <summary>路径。</summary>
    Path = 5,

    /// <summary>颜色。</summary>
    Color = 6,

    /// <summary>按键绑定（Unity KeyCode 名）。</summary>
    Keybinding = 7,

    /// <summary>字符串列表。</summary>
    List = 8,

    /// <summary>只读展示项（越界版本/临时项）。</summary>
    ReadOnly = 9,
}

/// <summary>文件形态（15 篇 §4.5）。`custom` 是 mod 私有语法，v1 只允许只读。</summary>
public enum ModConfigFileLayout
{
    /// <summary>有根节点 + 缩进的 ConfigNode。</summary>
    Node = 0,

    /// <summary>无根节点、无缩进的扁平 key = value。</summary>
    Flat = 1,

    /// <summary>私有语法（如 padheap.cfg 的冒号表），只读。</summary>
    Custom = 2,
}

/// <summary>文件级读写模式。</summary>
public enum ModConfigWriteMode
{
    /// <summary>可编辑。</summary>
    Editable = 0,

    /// <summary>只读（只展示 + 解析校验）。</summary>
    ReadOnly = 1,
}

/// <summary>文件缺失时的策略。</summary>
public enum ModConfigMissingFilePolicy
{
    /// <summary>按 template 生成。</summary>
    CreateFromTemplate = 0,

    /// <summary>视为错误（不提供写入口）。</summary>
    Error = 1,

    /// <summary>降级为只读。</summary>
    ReadOnly = 2,
}

/// <summary>文件里未在 adapter 声明的键怎么处理。默认 preserve：新的 mod 版本加了键也不能丢。</summary>
public enum ModConfigUnlistedPolicy
{
    /// <summary>原样保留（默认）。</summary>
    Preserve = 0,

    /// <summary>不显示。</summary>
    Hide = 1,

    /// <summary>显示为"未知项"。</summary>
    MarkUnknown = 2,
}

/// <summary>修改风险等级（R-35：high 需要二次确认）。</summary>
public enum ModConfigFieldRisk
{
    /// <summary>低。</summary>
    Low = 0,

    /// <summary>中。</summary>
    Medium = 1,

    /// <summary>高。</summary>
    High = 2,
}

/// <summary>行尾策略。默认 preserve：.cfg 的行尾不可假设（KSPCF 是 LF，.sfs 是 CRLF）。</summary>
public enum ModConfigLineEndingPolicy
{
    /// <summary>保留原样（默认）。</summary>
    Preserve = 0,

    /// <summary>统一 CRLF。</summary>
    Crlf = 1,

    /// <summary>统一 LF。</summary>
    Lf = 2,
}

/// <summary>adapter 级状态（15 篇 §5 状态机）。</summary>
public enum ModConfigAdapterState
{
    /// <summary>mod 未安装。</summary>
    NotInstalled = 0,

    /// <summary>已装 + 版本在区间 + 文件可读 → 可编辑。</summary>
    Editable = 1,

    /// <summary>已装但版本越界 → 强制只读。</summary>
    ReadOnlyVersion = 2,

    /// <summary>已装但文件缺失且允许按模板生成。</summary>
    CreatableByTemplate = 3,

    /// <summary>没有 adapter（原始只读）。</summary>
    ReadOnlyRaw = 4,
}

/// <summary>单个文件的状态。</summary>
public enum ModConfigFileState
{
    /// <summary>文件不存在。</summary>
    Missing = 0,

    /// <summary>可编辑。</summary>
    Editable = 1,

    /// <summary>只读。</summary>
    ReadOnly = 2,

    /// <summary>缺失但可按模板生成。</summary>
    CreatableByTemplate = 3,
}

/// <summary>加载期诊断级别。Error 表示"这个 adapter 不启用"，Warning 表示"启用但降级"。</summary>
public enum ModConfigDiagnosticLevel
{
    /// <summary>警告。</summary>
    Warning = 0,

    /// <summary>错误。</summary>
    Error = 1,
}

/// <summary>wire 名与枚举的双向映射。解析失败一律返回 false（由调用方转成诊断），绝不抛异常。</summary>
public static class ModConfigWireNames
{
    /// <summary>字段类型的 wire 名。</summary>
    public static string ToWireName(this ModConfigFieldType type) => type switch
    {
        ModConfigFieldType.Bool => "bool",
        ModConfigFieldType.Int => "int",
        ModConfigFieldType.Float => "float",
        ModConfigFieldType.Enum => "enum",
        ModConfigFieldType.String => "string",
        ModConfigFieldType.Path => "path",
        ModConfigFieldType.Color => "color",
        ModConfigFieldType.Keybinding => "keybinding",
        ModConfigFieldType.List => "list",
        ModConfigFieldType.ReadOnly => "readonly",
        _ => "readonly",
    };

    /// <summary>文件形态的 wire 名。</summary>
    public static string ToWireName(this ModConfigFileLayout layout) => layout switch
    {
        ModConfigFileLayout.Node => "node",
        ModConfigFileLayout.Flat => "flat",
        ModConfigFileLayout.Custom => "custom",
        _ => "node",
    };

    /// <summary>读写模式的 wire 名。</summary>
    public static string ToWireName(this ModConfigWriteMode mode) => mode switch
    {
        ModConfigWriteMode.Editable => "editable",
        ModConfigWriteMode.ReadOnly => "readonly",
        _ => "readonly",
    };

    /// <summary>缺失策略的 wire 名。</summary>
    public static string ToWireName(this ModConfigMissingFilePolicy policy) => policy switch
    {
        ModConfigMissingFilePolicy.CreateFromTemplate => "create-from-template",
        ModConfigMissingFilePolicy.Error => "error",
        ModConfigMissingFilePolicy.ReadOnly => "readonly",
        _ => "error",
    };

    /// <summary>未声明键策略的 wire 名。</summary>
    public static string ToWireName(this ModConfigUnlistedPolicy policy) => policy switch
    {
        ModConfigUnlistedPolicy.Preserve => "preserve",
        ModConfigUnlistedPolicy.Hide => "hide",
        ModConfigUnlistedPolicy.MarkUnknown => "mark-unknown",
        _ => "preserve",
    };

    /// <summary>风险等级的 wire 名。</summary>
    public static string ToWireName(this ModConfigFieldRisk risk) => risk switch
    {
        ModConfigFieldRisk.Low => "low",
        ModConfigFieldRisk.Medium => "medium",
        ModConfigFieldRisk.High => "high",
        _ => "low",
    };

    /// <summary>行尾策略的 wire 名。</summary>
    public static string ToWireName(this ModConfigLineEndingPolicy policy) => policy switch
    {
        ModConfigLineEndingPolicy.Preserve => "preserve",
        ModConfigLineEndingPolicy.Crlf => "crlf",
        ModConfigLineEndingPolicy.Lf => "lf",
        _ => "preserve",
    };

    /// <summary>解析字段类型 wire 名。</summary>
    public static bool TryParseFieldType(string? wire, out ModConfigFieldType type)
    {
        switch (wire)
        {
            case "bool": type = ModConfigFieldType.Bool; return true;
            case "int": type = ModConfigFieldType.Int; return true;
            case "float": type = ModConfigFieldType.Float; return true;
            case "enum": type = ModConfigFieldType.Enum; return true;
            case "string": type = ModConfigFieldType.String; return true;
            case "path": type = ModConfigFieldType.Path; return true;
            case "color": type = ModConfigFieldType.Color; return true;
            case "keybinding": type = ModConfigFieldType.Keybinding; return true;
            case "list": type = ModConfigFieldType.List; return true;
            case "readonly": type = ModConfigFieldType.ReadOnly; return true;
            default: type = ModConfigFieldType.ReadOnly; return false;
        }
    }

    /// <summary>解析文件形态 wire 名。</summary>
    public static bool TryParseLayout(string? wire, out ModConfigFileLayout layout)
    {
        switch (wire)
        {
            case "node": layout = ModConfigFileLayout.Node; return true;
            case "flat": layout = ModConfigFileLayout.Flat; return true;
            case "custom": layout = ModConfigFileLayout.Custom; return true;
            default: layout = ModConfigFileLayout.Node; return false;
        }
    }

    /// <summary>解析读写模式 wire 名。</summary>
    public static bool TryParseWriteMode(string? wire, out ModConfigWriteMode mode)
    {
        switch (wire)
        {
            case "editable": mode = ModConfigWriteMode.Editable; return true;
            case "readonly": mode = ModConfigWriteMode.ReadOnly; return true;
            default: mode = ModConfigWriteMode.ReadOnly; return false;
        }
    }

    /// <summary>解析缺失策略 wire 名。</summary>
    public static bool TryParseMissingPolicy(string? wire, out ModConfigMissingFilePolicy policy)
    {
        switch (wire)
        {
            case "create-from-template": policy = ModConfigMissingFilePolicy.CreateFromTemplate; return true;
            case "error": policy = ModConfigMissingFilePolicy.Error; return true;
            case "readonly": policy = ModConfigMissingFilePolicy.ReadOnly; return true;
            default: policy = ModConfigMissingFilePolicy.Error; return false;
        }
    }

    /// <summary>解析未声明键策略 wire 名。</summary>
    public static bool TryParseUnlistedPolicy(string? wire, out ModConfigUnlistedPolicy policy)
    {
        switch (wire)
        {
            case "preserve": policy = ModConfigUnlistedPolicy.Preserve; return true;
            case "hide": policy = ModConfigUnlistedPolicy.Hide; return true;
            case "mark-unknown": policy = ModConfigUnlistedPolicy.MarkUnknown; return true;
            default: policy = ModConfigUnlistedPolicy.Preserve; return false;
        }
    }

    /// <summary>解析风险等级 wire 名。</summary>
    public static bool TryParseRisk(string? wire, out ModConfigFieldRisk risk)
    {
        switch (wire)
        {
            case "low": risk = ModConfigFieldRisk.Low; return true;
            case "medium": risk = ModConfigFieldRisk.Medium; return true;
            case "high": risk = ModConfigFieldRisk.High; return true;
            default: risk = ModConfigFieldRisk.Low; return false;
        }
    }

    /// <summary>解析行尾策略 wire 名。</summary>
    public static bool TryParseLineEnding(string? wire, out ModConfigLineEndingPolicy policy)
    {
        switch (wire)
        {
            case "preserve": policy = ModConfigLineEndingPolicy.Preserve; return true;
            case "crlf": policy = ModConfigLineEndingPolicy.Crlf; return true;
            case "lf": policy = ModConfigLineEndingPolicy.Lf; return true;
            default: policy = ModConfigLineEndingPolicy.Preserve; return false;
        }
    }
}
