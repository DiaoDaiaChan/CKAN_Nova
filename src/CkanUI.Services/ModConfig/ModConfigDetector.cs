using CkanUI.Services.Abstractions.ModConfig;

namespace CkanUI.Services.ModConfig;

/// <summary>
/// 适配器状态机（CFG-03）。全部是纯函数：不读文件、不碰进程，只吃"已装事实 + 版本 + 文件存在性"三个输入。
/// <para>
/// 之所以必须是纯函数：状态判定是"越界即只读"这类安全规则的唯一入口（§5），
/// 只有不依赖 IO 才能在单测里穷举所有分支，也才能让 UI 每次都算出同一结果。
/// 真正去查注册表/文件系统的是 <see cref="ModConfigService"/>，它把结果喂给这里的函数。
/// </para>
/// </summary>
public static class ModConfigDetector
{
    /// <summary>已装判定：CKAN 注册表命中 **或** adapter 声明的路径里有文件存在（§5）。</summary>
    public static bool IsInstalled(bool registryHit, bool anyDeclaredFileExists)
        => registryHit || anyDeclaredFileExists;

    /// <summary>
    /// 判定一个 adapter 对当前实例的整体状态。
    /// </summary>
    /// <param name="registryHit">CKAN 注册表里是否有该 mod。</param>
    /// <param name="anyDeclaredFileExists">adapter 声明的文件是否至少存在一个。</param>
    /// <param name="supportedVersionRange">adapter 的 <c>modVersionRange</c>；空表示不限制。</param>
    /// <param name="installedVersion">注册表里的已装版本；可为 null（只靠文件命中时常见）。</param>
    /// <param name="anyCreatableFile">是否有文件可由 template 创建。</param>
    /// <param name="anyWritableFile">是否已有可写文件。</param>
    /// <param name="diagnostic">需要展示给用户的说明（越界原因、版本无法判定等）。</param>
    public static ModConfigAdapterState DetectAdapterState(
        bool registryHit,
        bool anyDeclaredFileExists,
        IReadOnlyList<string>? supportedVersionRange,
        string? installedVersion,
        bool anyCreatableFile,
        bool anyWritableFile,
        out ModConfigDiagnostic? diagnostic)
    {
        diagnostic = null;

        if (!IsInstalled(registryHit, anyDeclaredFileExists))
        {
            return ModConfigAdapterState.NotInstalled;
        }

        // 版本越界优先于一切：宁可只读，也不拿未知版本的配置文件去试验 verifier。
        if (!ModConfigVersionRange.TryIsSatisfied(supportedVersionRange, installedVersion, out var satisfied, out var versionError))
        {
            // 拿不到版本（只有文件命中）本身不是错误，但要留痕：UI 上提示"版本未确认"。
            diagnostic = ModConfigDiagnostic.Warning(
                "version-unknown",
                versionError ?? "无法判定已装版本是否在支持范围内，按支持处理");
        }
        else if (!satisfied)
        {
            diagnostic = ModConfigDiagnostic.Warning(
                "version-out-of-range",
                $"已装版本 {installedVersion} 不在适配器支持范围（{ModConfigVersionRange.FormatForDisplay(supportedVersionRange)}）内，已禁用编辑");
            return ModConfigAdapterState.ReadOnlyVersion;
        }

        if (!anyDeclaredFileExists)
        {
            if (anyCreatableFile)
            {
                return ModConfigAdapterState.CreatableByTemplate;
            }

            diagnostic = ModConfigDiagnostic.Warning(
                "files-missing",
                "adapter 声明的配置文件都不存在，且没有可从模板创建的文件");
            return ModConfigAdapterState.ReadOnlyRaw;
        }

        if (!anyWritableFile && !anyCreatableFile)
        {
            // 有 adapter 但当前一份可写文件都没有（例如只声明了 padheap.cfg 这类语义危险文件）。
            diagnostic = ModConfigDiagnostic.Warning(
                "files-readonly",
                "adapter 声明的文件当前全部为只读；请在原始视图中查看");
            return ModConfigAdapterState.ReadOnlyRaw;
        }

        return ModConfigAdapterState.Editable;
    }

    /// <summary>判定单个文件的状态（<paramref name="exists"/> 由调用方探过磁盘后传入）。</summary>
    public static ModConfigFileState DetectFileState(ModConfigFileDescription file, bool exists)
    {
        ArgumentNullException.ThrowIfNull(file);

        if (!exists)
        {
            return file.OnMissing == ModConfigMissingFilePolicy.CreateFromTemplate
                && !string.IsNullOrWhiteSpace(file.Template)
                    ? ModConfigFileState.CreatableByTemplate
                    : ModConfigFileState.Missing;
        }

        return file.IsWritable ? ModConfigFileState.Editable : ModConfigFileState.ReadOnly;
    }

    /// <summary>
    /// 版本越界横幅原文（§4.3）。集中在这里拼是为了让 UI 与测试共用同一句中文，
    /// 避免各页面各写一版导致措辞漂移。
    /// </summary>
    public static string BuildVersionOutOfRangeMessage(string? installedVersion, IReadOnlyList<string>? supportedVersionRange)
    {
        var version = string.IsNullOrWhiteSpace(installedVersion) ? "未知" : installedVersion;
        var range = ModConfigVersionRange.FormatForDisplay(supportedVersionRange);
        return $"此 mod 版本 {version} 超出适配器支持范围（{range}），为避免写坏配置已禁用编辑；请更新适配器或原始视图查看";
    }

    /// <summary>
    /// 字段值是否满足声明约束（type/min/max/values/pattern）。写盘前用它做第一道拦截（CFG-05 校验阶段）。
    /// </summary>
    /// <param name="field">字段声明。</param>
    /// <param name="value">待写入的原文。</param>
    /// <param name="error">不通过时的中文说明。</param>
    public static bool ValidateFieldValue(ModConfigFieldDescription field, string? value, out string? error)
    {
        ArgumentNullException.ThrowIfNull(field);
        error = null;

        if (field.Type == ModConfigFieldType.ReadOnly)
        {
            error = $"字段「{field.Key}」是只读的";
            return false;
        }

        var text = value ?? string.Empty;
        if (text.Length == 0)
        {
            error = $"字段「{field.Key}」的值不能为空";
            return false;
        }

        // 值里的换行会打穿一行一键的假设，直接拒绝（写盘层还会再拦一次）。
        if (text.Contains('\n') || text.Contains('\r'))
        {
            error = $"字段「{field.Key}」的值不能包含换行";
            return false;
        }

        switch (field.Type)
        {
            case ModConfigFieldType.Bool:
                var lowered = text.Trim().ToLowerInvariant();
                if (lowered is not ("true" or "false" or "1" or "0"))
                {
                    error = $"字段「{field.Key}」是布尔值，只能写 true 或 false";
                    return false;
                }

                break;

            case ModConfigFieldType.Int:
                if (!int.TryParse(text.Trim(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var intValue))
                {
                    error = $"字段「{field.Key}」需要整数，实际是「{text}」";
                    return false;
                }

                if (!WithinRange(field, intValue, out error))
                {
                    return false;
                }

                break;

            case ModConfigFieldType.Float:
                // ConfigNode 里允许 Unity 风格的浮点后缀（实测 SystemHeat 的 "2.7f"），
                // 词法合法但 double.TryParse 会拒；按共用解析器剥后缀再解析，原文照旧写回。
                if (!ModConfigValueComparer.TryParseNumber(text, out var floatValue)
                    || double.IsNaN(floatValue) || double.IsInfinity(floatValue))
                {
                    error = $"字段「{field.Key}」需要数字，实际是「{text}」";
                    return false;
                }

                if (!WithinRange(field, floatValue, out error))
                {
                    return false;
                }

                break;

            case ModConfigFieldType.Enum:
                if (!ContainsValue(field.Values, text))
                {
                    error = $"字段「{field.Key}」只能取 {string.Join(" / ", field.Values ?? [])}，实际是「{text}」";
                    return false;
                }

                break;
        }

        if (!string.IsNullOrEmpty(field.Pattern))
        {
            try
            {
                if (!System.Text.RegularExpressions.Regex.IsMatch(
                        text,
                        field.Pattern,
                        System.Text.RegularExpressions.RegexOptions.CultureInvariant))
                {
                    error = $"字段「{field.Key}」的值「{text}」不匹配格式要求（{field.Pattern}）";
                    return false;
                }
            }
            catch (ArgumentException)
            {
                // 正则本身写坏了不该阻断用户操作，降级成不校验（加载期已经报过 diagnostics）。
            }
        }

        return true;
    }

    private static bool WithinRange(ModConfigFieldDescription field, double value, out string? error)
    {
        error = null;
        if (field.Min is not null && value < field.Min)
        {
            error = $"字段「{field.Key}」不能小于 {field.Min}";
            return false;
        }

        if (field.Max is not null && value > field.Max)
        {
            error = $"字段「{field.Key}」不能大于 {field.Max}";
            return false;
        }

        return true;
    }

    private static bool ContainsValue(IReadOnlyList<string>? values, string text)
    {
        if (values is null || values.Count == 0)
        {
            return true;
        }

        foreach (var candidate in values)
        {
            if (string.Equals(candidate, text, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
