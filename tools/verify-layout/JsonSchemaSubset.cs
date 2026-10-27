using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CkanUI.Tools.VerifyLayout;

/// <summary>
/// 最小 JSON Schema 子集校验器（draft-07 里 adapter.json 实际用到的关键字）。
/// 不引第三方包（21 篇 §11 / 团队简报 §5 禁止新增 NuGet），够用即可：
/// type / required / properties / additionalProperties:false / items / enum / pattern / minLength /
/// minItems / minimum / maximum。
/// </summary>
internal static class JsonSchemaSubset
{
    private static readonly JsonDocumentOptions ParseOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    /// <summary>按 schema 校验一个 JSON 文件，返回所有违规（空序列 = 通过）。</summary>
    public static IReadOnlyList<string> Validate(string schemaPath, JsonElement instance)
    {
        using var schemaDocument = JsonDocument.Parse(File.ReadAllText(schemaPath), ParseOptions);
        var errors = new List<string>();
        ValidateElement(schemaDocument.RootElement, instance, "$", errors);
        return errors;
    }

    private static void ValidateElement(JsonElement schema, JsonElement instance, string pointer, List<string> errors)
    {
        if (schema.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (schema.TryGetProperty("type", out var typeNode) && !MatchesType(typeNode, instance))
        {
            errors.Add($"{pointer} 类型应为 {DescribeType(typeNode)}，实际 {instance.ValueKind}");
            return;
        }

        if (schema.TryGetProperty("enum", out var enumNode) && enumNode.ValueKind == JsonValueKind.Array)
        {
            var allowed = enumNode.EnumerateArray().Select(item => item.GetRawText()).ToList();
            if (!allowed.Contains(instance.GetRawText(), StringComparer.Ordinal))
            {
                errors.Add($"{pointer} 取值必须是 {string.Join(" / ", allowed)}，实际 {instance.GetRawText()}");
            }
        }

        switch (instance.ValueKind)
        {
            case JsonValueKind.String:
                ValidateString(schema, instance, pointer, errors);
                break;
            case JsonValueKind.Number:
                ValidateNumber(schema, instance, pointer, errors);
                break;
            case JsonValueKind.Array:
                ValidateArray(schema, instance, pointer, errors);
                break;
            case JsonValueKind.Object:
                ValidateObject(schema, instance, pointer, errors);
                break;
        }
    }

    private static void ValidateString(JsonElement schema, JsonElement instance, string pointer, List<string> errors)
    {
        var value = instance.GetString() ?? string.Empty;
        if (schema.TryGetProperty("minLength", out var minLength) && minLength.TryGetInt32(out var min) && value.Length < min)
        {
            errors.Add($"{pointer} 长度不足 {min}");
        }

        if (schema.TryGetProperty("pattern", out var pattern) && pattern.ValueKind == JsonValueKind.String)
        {
            var regex = pattern.GetString() ?? string.Empty;
            try
            {
                if (!Regex.IsMatch(value, regex, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
                {
                    errors.Add($"{pointer} 不匹配模式 {regex}");
                }
            }
            catch (ArgumentException ex)
            {
                errors.Add($"{pointer} schema 的 pattern 非法: {ex.Message}");
            }
        }
    }

    private static void ValidateNumber(JsonElement schema, JsonElement instance, string pointer, List<string> errors)
    {
        var value = instance.GetDouble();
        if (schema.TryGetProperty("minimum", out var minimum) && minimum.TryGetDouble(out var min) && value < min)
        {
            errors.Add($"{pointer} 应 ≥ {min.ToString(CultureInfo.InvariantCulture)}");
        }

        if (schema.TryGetProperty("maximum", out var maximum) && maximum.TryGetDouble(out var max) && value > max)
        {
            errors.Add($"{pointer} 应 ≤ {max.ToString(CultureInfo.InvariantCulture)}");
        }
    }

    private static void ValidateArray(JsonElement schema, JsonElement instance, string pointer, List<string> errors)
    {
        var count = instance.GetArrayLength();
        if (schema.TryGetProperty("minItems", out var minItems) && minItems.TryGetInt32(out var min) && count < min)
        {
            errors.Add($"{pointer} 至少 {min} 项，实际 {count}");
        }

        if (schema.TryGetProperty("maxItems", out var maxItems) && maxItems.TryGetInt32(out var max) && count > max)
        {
            errors.Add($"{pointer} 至多 {max} 项，实际 {count}");
        }

        if (!schema.TryGetProperty("items", out var itemsSchema))
        {
            return;
        }

        var index = 0;
        foreach (var item in instance.EnumerateArray())
        {
            ValidateElement(itemsSchema, item, $"{pointer}[{index.ToString(CultureInfo.InvariantCulture)}]", errors);
            index++;
        }
    }

    private static void ValidateObject(JsonElement schema, JsonElement instance, string pointer, List<string> errors)
    {
        var hasProperties = schema.TryGetProperty("properties", out var properties);

        if (schema.TryGetProperty("required", out var required) && required.ValueKind == JsonValueKind.Array)
        {
            foreach (var name in required.EnumerateArray())
            {
                var key = name.GetString();
                if (key is null)
                {
                    continue;
                }

                if (!instance.TryGetProperty(key, out _))
                {
                    errors.Add($"{pointer}.{key} 缺失（required）");
                }
            }
        }

        if (!hasProperties || properties.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var known = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in properties.EnumerateObject())
        {
            known.Add(property.Name);
            if (instance.TryGetProperty(property.Name, out var value))
            {
                ValidateElement(property.Value, value, $"{pointer}.{property.Name}", errors);
            }
        }

        if (schema.TryGetProperty("additionalProperties", out var additional) &&
            additional.ValueKind == JsonValueKind.False)
        {
            foreach (var property in instance.EnumerateObject())
            {
                if (!known.Contains(property.Name))
                {
                    errors.Add($"{pointer}.{property.Name} 未在 schema 中声明");
                }
            }
        }
    }

    private static bool MatchesType(JsonElement typeNode, JsonElement instance)
    {
        if (typeNode.ValueKind == JsonValueKind.String)
        {
            return MatchesSingleType(typeNode.GetString(), instance);
        }

        if (typeNode.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in typeNode.EnumerateArray())
            {
                if (MatchesSingleType(item.GetString(), instance))
                {
                    return true;
                }
            }

            return false;
        }

        return true;
    }

    private static bool MatchesSingleType(string? type, JsonElement instance) => type switch
    {
        "object" => instance.ValueKind == JsonValueKind.Object,
        "array" => instance.ValueKind == JsonValueKind.Array,
        "string" => instance.ValueKind == JsonValueKind.String,
        "number" => instance.ValueKind == JsonValueKind.Number,
        "integer" => instance.ValueKind == JsonValueKind.Number && instance.TryGetInt64(out _),
        "boolean" => instance.ValueKind is JsonValueKind.True or JsonValueKind.False,
        "null" => instance.ValueKind == JsonValueKind.Null,
        _ => true,
    };

    private static string DescribeType(JsonElement typeNode) => typeNode.ValueKind == JsonValueKind.Array
        ? string.Join(" | ", typeNode.EnumerateArray().Select(item => item.GetString()))
        : typeNode.GetString() ?? "?";
}
