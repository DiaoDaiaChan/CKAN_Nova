using CkanUI.Services.Abstractions.ModConfig;

namespace CkanUI.ViewModels.Pages.ModConfig;

/// <summary>
/// 渲染器注册表：页面按字段类型取渲染器，而不是自己 switch 九种类型。
/// </summary>
/// <remarks>
/// CFG-04 起九种类型都有渲染器：bool/int/float/enum/string/path/list 可编辑，
/// color/keybinding 只读展示（行内会写明"本版只读"的原因，不做静默禁用）。
/// 除 <see cref="ModConfigFieldType.ReadOnly"/> 之外的类型都已覆盖；未注册类型仍落到
/// <see cref="UnsupportedFieldRenderer"/>（只读文本展示），后续新增类型用 <see cref="Register"/> 注册即可。
/// </remarks>
public sealed class ModConfigFieldRendererRegistry
{
    private readonly Dictionary<ModConfigFieldType, IModConfigFieldRenderer> _renderers = [];

    /// <summary>默认构造：注册内置九种渲染器。</summary>
    public ModConfigFieldRendererRegistry()
        : this(CreateDefaultRenderers())
    {
    }

    /// <summary>自定义构造：从给定集合注册（测试与后续扩展用）。</summary>
    /// <param name="renderers">渲染器集合；为 null 时用默认九种。</param>
    public ModConfigFieldRendererRegistry(IEnumerable<IModConfigFieldRenderer>? renderers)
    {
        foreach (var renderer in renderers ?? CreateDefaultRenderers())
        {
            Register(renderer);
        }
    }

    /// <summary>已注册的类型（按注册顺序）。</summary>
    public IReadOnlyCollection<ModConfigFieldType> SupportedTypes => _renderers.Keys;

    /// <summary>默认九种渲染器（CFG-04 的九种字段类型）。</summary>
    public static IReadOnlyList<IModConfigFieldRenderer> CreateDefaultRenderers() =>
    [
        new BoolFieldRenderer(),
        new IntFieldRenderer(),
        new FloatFieldRenderer(),
        new EnumFieldRenderer(),
        new StringFieldRenderer(),
        new PathFieldRenderer(),
        new ListFieldRenderer(),
        new ColorFieldRenderer(),
        new KeybindingFieldRenderer(),
    ];

    /// <summary>注册（同类型后注册的覆盖先注册的）。</summary>
    public void Register(IModConfigFieldRenderer renderer)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        _renderers[renderer.FieldType] = renderer;
    }

    /// <summary>按类型取渲染器。</summary>
    public bool TryGet(ModConfigFieldType type, out IModConfigFieldRenderer renderer)
        => _renderers.TryGetValue(type, out renderer!);

    /// <summary>取渲染器；未注册的类型退回只读文本兜底。</summary>
    public IModConfigFieldRenderer Resolve(ModConfigFieldType type)
        => TryGet(type, out var renderer) ? renderer : UnsupportedFieldRenderer.Instance;
}

/// <summary>未注册类型的兜底渲染器：老实展示原文，不做任何猜测性解释。</summary>
public sealed class UnsupportedFieldRenderer : IModConfigFieldRenderer
{
    /// <summary>单例（无状态）。</summary>
    public static readonly UnsupportedFieldRenderer Instance = new();

    private UnsupportedFieldRenderer()
    {
    }

    /// <inheritdoc />
    public ModConfigFieldType FieldType => ModConfigFieldType.ReadOnly;

    /// <inheritdoc />
    public ModConfigEditorKind EditorKind => ModConfigEditorKind.ReadOnlyText;

    /// <inheritdoc />
    public string FormatValue(ModConfigFieldState field)
    {
        ArgumentNullException.ThrowIfNull(field);
        return field.Value ?? string.Empty;
    }
}
