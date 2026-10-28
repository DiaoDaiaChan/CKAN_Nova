using CkanUI.Services.Abstractions.ModConfig;

namespace CkanUI.Services.ModConfig;

/// <summary>
/// adapter 目录扫描与加载（CFG-02）。
/// <para>
/// 两个来源：随包发布的内置目录（默认 <c>&lt;程序目录&gt;/adapters</c>）与用户目录
/// （<c>%LocalAppData%\CKAN\ui\adapters</c>，§3）。同名 id 时用户版覆盖内置版。
/// </para>
/// <para>
/// 隔离原则：单个 adapter.json 坏掉只停用它自己并记诊断，绝不影响其它 adapter（T-CFG-06）。
/// </para>
/// </summary>
public sealed class AdapterCatalog : IAdapterCatalog
{
    private readonly IReadOnlyList<string> _builtinDirectories;
    private readonly IReadOnlyList<string> _userDirectories;
    private readonly Lock _gate = new();
    private IReadOnlyList<ModConfigAdapterDescription>? _adapters;
    private IReadOnlyList<ModConfigDiagnostic>? _diagnostics;

    /// <summary>构造。</summary>
    /// <param name="builtinDirectories">内置 adapter 根目录；为 null 时用 &lt;程序目录&gt;/adapters。</param>
    /// <param name="userDirectories">用户 adapter 根目录；为 null 时用 %LocalAppData%\CKAN\ui\adapters。</param>
    public AdapterCatalog(
        IReadOnlyList<string>? builtinDirectories = null,
        IReadOnlyList<string>? userDirectories = null)
    {
        _builtinDirectories = builtinDirectories ?? [Path.Combine(AppContext.BaseDirectory, "adapters")];
        _userDirectories = userDirectories ?? [GetDefaultUserDirectory()];
    }

    /// <inheritdoc />
    public IReadOnlyList<ModConfigAdapterDescription> Adapters
    {
        get
        {
            EnsureLoaded();
            return _adapters ?? [];
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<ModConfigDiagnostic> Diagnostics
    {
        get
        {
            EnsureLoaded();
            return _diagnostics ?? [];
        }
    }

    /// <inheritdoc />
    public void Reload()
    {
        lock (_gate)
        {
            _adapters = null;
            _diagnostics = null;
        }
    }

    /// <summary>用户 adapter 的默认位置（与 CKAN 的 AppData 约定一致，见 CKANPathUtils.AppDataPath）。</summary>
    public static string GetDefaultUserDirectory()
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CKAN",
            "ui",
            "adapters");

    private void EnsureLoaded()
    {
        lock (_gate)
        {
            if (_adapters is not null)
            {
                return;
            }

            var diagnostics = new List<ModConfigDiagnostic>();
            var loaded = new Dictionary<string, ModConfigAdapterDescription>(StringComparer.OrdinalIgnoreCase);

            foreach (var directory in _builtinDirectories)
            {
                LoadFrom(directory, isUserProvided: false, required: true, loaded, diagnostics);
            }

            foreach (var directory in _userDirectories)
            {
                LoadFrom(directory, isUserProvided: true, required: false, loaded, diagnostics);
            }

            var list = new List<ModConfigAdapterDescription>(loaded.Count);
            list.AddRange(loaded.Values);
            list.Sort(static (left, right) => string.CompareOrdinal(left.Name, right.Name));

            _adapters = list;
            _diagnostics = diagnostics;
        }
    }

    private static void LoadFrom(
        string root,
        bool isUserProvided,
        bool required,
        Dictionary<string, ModConfigAdapterDescription> loaded,
        List<ModConfigDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            // 用户目录不存在是常态（没人写过用户 adapter），只有内置目录缺失才值得警告。
            if (required)
            {
                diagnostics.Add(ModConfigDiagnostic.Warning(
                    "adapter-dir-missing",
                    $"内置 adapter 目录不存在：{root}（适配器可能没随包复制）",
                    root));
            }

            return;
        }

        string[] entries;
        try
        {
            entries = Directory.GetDirectories(root);
        }
        catch (IOException ex)
        {
            diagnostics.Add(ModConfigDiagnostic.Error("adapter-dir-io", $"枚举 adapter 目录失败：{ex.Message}", root));
            return;
        }
        catch (UnauthorizedAccessException ex)
        {
            diagnostics.Add(ModConfigDiagnostic.Error("adapter-dir-io", $"枚举 adapter 目录被拒绝：{ex.Message}", root));
            return;
        }

        Array.Sort(entries, StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries)
        {
            var adapterJson = Path.Combine(entry, "adapter.json");
            if (!File.Exists(adapterJson))
            {
                continue;
            }

            var result = AdapterJsonReader.ReadFile(adapterJson, isUserProvided);
            foreach (var diagnostic in result.Diagnostics)
            {
                diagnostics.Add(diagnostic);
            }

            var adapter = result.Adapter;
            if (adapter is null)
            {
                // 坏掉的 adapter 只停用它自己：这里刻意不抛，继续加载其余 adapter。
                continue;
            }

            if (loaded.TryGetValue(adapter.Id, out var existing))
            {
                if (!isUserProvided && existing.IsUserProvided)
                {
                    // 内置版已经被用户版覆盖过，内置版与用户版同 id 时保留用户版。
                    continue;
                }

                if (isUserProvided)
                {
                    diagnostics.Add(ModConfigDiagnostic.Warning(
                        "adapter-overridden",
                        $"内置 adapter「{adapter.Id}」被用户目录里的同名 adapter 覆盖",
                        adapterJson));
                }
                else
                {
                    diagnostics.Add(ModConfigDiagnostic.Warning(
                        "adapter-duplicate",
                        $"adapter id「{adapter.Id}」重复，保留先加载的那一份",
                        adapterJson));
                    continue;
                }
            }

            loaded[adapter.Id] = adapter;
        }
    }
}
