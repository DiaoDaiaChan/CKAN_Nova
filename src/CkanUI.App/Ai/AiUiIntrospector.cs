using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Windows.Input;

using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Ai;

namespace CkanUI.App.Ai;

/// <summary>
/// 界面自省器：把一个页面视图模型反射成「可读字段 + 可执行命令」。
/// </summary>
/// <remarks>
/// <para><b>为什么反射而不是清单</b>：手写清单每加一个页面、一个按钮就要改一处，忘了改的表现是
/// 「AI 说它做不到」，而不是编译错误——这类缺口没人会去测。反射的代价是输出面失控，所以这里
/// 只认三类东西，别的一律不暴露：</para>
/// <list type="number">
/// <item><b>字段</b>：公共实例属性里能一句话说清值的（字符串 / 布尔 / 数字 / 枚举 / 时间 / 集合条数）。
/// 复杂对象、<see cref="ICommand"/>、索引器、泛型容器一律跳过——AI 拿到 <c>ViewModel</c> 的
/// <c>ToString()</c> 只会浪费上下文。</item>
/// <item><b>动作</b>：公共实例属性里类型实现了 <see cref="ICommand"/> 的，等价于界面上能点的按钮。
/// 名字就是绑定名，调用方不用记第二套键。</item>
/// <item><b>敏感值</b>：名字里带 key / token / secret / password / credential 的字段只回 <c>***</c>。
/// AI 的对话要进日志，密钥不能跟着进。</item>
/// </list>
/// <para>
/// 反射本身会抛（索引器、显式接口实现、getter 内部异常、插件页类型被卸载），所以每个取值都包在
/// try/catch 里：单个属性炸掉只丢这一个字段，不能让整页快照变成异常。
/// </para>
/// <para>本类不引用 CommunityToolkit：异步命令按接口全名鸭子类型识别，避免 App 层多一个编译期依赖。</para>
/// </remarks>
public static class AiUiIntrospector
{
    /// <summary>单页字段上限：再多模型也读不完，还会把真正重要的计数挤出上下文。</summary>
    public const int MaxFields = 40;

    /// <summary>单页动作上限。</summary>
    public const int MaxActions = 120;

    /// <summary>单个字符串值的截断长度。</summary>
    public const int MaxValueLength = 160;

    /// <summary>异步命令的等待上限；超时只报告「仍在后台跑」，不取消——取消别人的命令比慢更危险。</summary>
    private static readonly TimeSpan AsyncActionTimeout = TimeSpan.FromSeconds(30);

    /// <summary>名字里带这些词的命令先问用户一声再执行（对磁盘或进程有副作用）。</summary>
    private static readonly string[] DestructiveMarkers =
    [
        "uninstall", "remove", "delete", "clear", "purge", "wipe", "reset",
        "rollback", "revert", "restore", "install", "import", "export",
        "dedupe", "prune", "unload", "disable", "cancel", "stop", "kill",
        "terminate", "shutdown", "launch",
    ];

    /// <summary>名字里带这些词的字段值是密钥，只回掩码。</summary>
    private static readonly string[] SensitiveMarkers =
    [
        "key", "token", "secret", "password", "passwd", "credential", "apikey",
    ];

    /// <summary>读一个页面视图模型的公共标量字段（顺序按属性名稳定排序）。</summary>
    /// <param name="viewModel">页面视图模型；为 <c>null</c> 时返回空列表。</param>
    public static IReadOnlyList<AiUiField> DescribeFields(object? viewModel)
    {
        if (viewModel is null)
        {
            return Array.Empty<AiUiField>();
        }

        var properties = viewModel.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
        Array.Sort(properties, static (left, right) => string.CompareOrdinal(left.Name, right.Name));

        var fields = new List<AiUiField>();
        foreach (var property in properties)
        {
            if (fields.Count >= MaxFields)
            {
                break;
            }

            if (property.GetIndexParameters().Length > 0)
            {
                continue;
            }

            var getter = property.GetMethod;
            if (getter is null || !getter.IsPublic || getter.IsStatic)
            {
                continue;
            }

            // 命令走 DescribeActions，混进字段里只会让模型以为那是个值。
            if (typeof(ICommand).IsAssignableFrom(property.PropertyType))
            {
                continue;
            }

            var kind = KindOf(property.PropertyType);
            if (kind is null)
            {
                continue;
            }

            object? raw;
            try
            {
                raw = property.GetValue(viewModel);
            }
            catch (Exception)
            {
                // 插件页的 getter 可能查磁盘、连网络、访问已卸载类型：坏一个字段不影响整页。
                continue;
            }

            var value = IsSensitive(property.Name) ? "***" : FormatValue(raw);
            fields.Add(new AiUiField(property.Name, kind, value));
        }

        return fields;
    }

    /// <summary>读一个页面视图模型上全部可执行命令。</summary>
    /// <param name="viewModel">页面视图模型；为 <c>null</c> 时返回空列表。</param>
    public static IReadOnlyList<AiPageAction> DescribeActions(object? viewModel)
    {
        if (viewModel is null)
        {
            return Array.Empty<AiPageAction>();
        }

        var actions = new List<AiPageAction>();
        foreach (var (property, command) in EnumerateCommands(viewModel))
        {
            if (actions.Count >= MaxActions)
            {
                break;
            }

            var parameterType = ParameterTypeOf(command.GetType());
            actions.Add(new AiPageAction(
                property.Name,
                StripCommandSuffix(property.Name),
                CanExecute(command, parameter: null),
                parameterType?.Name));
        }

        return actions;
    }

    /// <summary>名字是否指向一个有副作用的命令（安装 / 卸载 / 应用 / 启动……）。</summary>
    /// <param name="name">命令属性名，可带 <c>Command</c> 后缀。</param>
    public static bool IsDestructive(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var lower = name.ToLowerInvariant();
        if (lower.Contains("apply", StringComparison.Ordinal)
            && !lower.Contains("filter", StringComparison.Ordinal)
            && !lower.Contains("sort", StringComparison.Ordinal)
            && !lower.Contains("search", StringComparison.Ordinal))
        {
            return true;
        }

        return DestructiveMarkers.Any(marker => lower.Contains(marker, StringComparison.Ordinal));
    }

    /// <summary>按名字查找页面上的命令（大小写不敏感，<c>Command</c> 后缀可省）。</summary>
    public static bool TryFindAction(object? viewModel, string action, out ICommand? command, out string? propertyName)
    {
        command = null;
        propertyName = null;
        if (viewModel is null || string.IsNullOrWhiteSpace(action))
        {
            return false;
        }

        var wanted = action.Trim();
        foreach (var (property, candidate) in EnumerateCommands(viewModel))
        {
            if (string.Equals(property.Name, wanted, StringComparison.OrdinalIgnoreCase)
                || string.Equals(property.Name, wanted + "Command", StringComparison.OrdinalIgnoreCase))
            {
                command = candidate;
                propertyName = property.Name;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 执行页面上的一个命令：查名字 → 转换参数 → 查可用性 → 执行（异步命令最多等 <see cref="AsyncActionTimeout"/>）。
    /// </summary>
    /// <param name="viewModel">页面视图模型。</param>
    /// <param name="action">命令属性名。</param>
    /// <param name="argument">参数文本；无参命令传 <c>null</c>。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public static async Task<OperationResult<string>> InvokeAsync(
        object? viewModel,
        string action,
        string? argument,
        CancellationToken cancellationToken = default)
    {
        if (viewModel is null)
        {
            return OperationResult<string>.Fail("当前没有打开的页面，未执行任何操作。");
        }

        if (!TryFindAction(viewModel, action, out var command, out var propertyName) || command is null)
        {
            var available = string.Join("、", DescribeActions(viewModel).Select(item => item.Name));
            return OperationResult<string>.Fail(
                $"`{viewModel.GetType().Name}` 上没有叫「{action}」的命令。",
                available.Length == 0 ? "这一页没有可执行命令。" : "可用命令：" + available);
        }

        var parameterType = ParameterTypeOf(command.GetType());
        if (!TryConvert(argument, parameterType, out var payload, out var convertError))
        {
            return OperationResult<string>.Fail(
                $"命令「{propertyName}」的参数「{argument}」无法转换成 {parameterType!.Name}：{convertError}");
        }

        if (!CanExecute(command, payload))
        {
            return OperationResult<string>.Fail(
                $"命令「{propertyName}」当前不可执行（界面上那个按钮是灰的），可能要先选中一行或等当前任务结束。");
        }

        try
        {
            var execution = ExecuteAsync(command, payload);
            if (execution is null)
            {
                return OperationResult<string>.Ok($"已触发「{propertyName}」。");
            }

            var completed = await Task.WhenAny(execution, Task.Delay(AsyncActionTimeout, cancellationToken))
                .ConfigureAwait(true);
            if (!ReferenceEquals(completed, execution))
            {
                return OperationResult<string>.Ok(
                    $"已触发「{propertyName}」，30 秒内没跑完，仍在后台执行；用 ckan_ui_state 看它的结果。");
            }

            await execution.ConfigureAwait(true);
            return OperationResult<string>.Ok($"已执行「{propertyName}」。");
        }
        catch (OperationCanceledException)
        {
            return OperationResult<string>.Fail($"命令「{propertyName}」已取消。", retryable: true);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            return OperationResult<string>.Fail($"命令「{propertyName}」执行失败。", ex.InnerException.Message);
        }
        catch (Exception ex)
        {
            return OperationResult<string>.Fail($"命令「{propertyName}」执行失败。", ex.Message);
        }
    }

    /// <summary>把 CLR 类型归纳成给模型看的值种类；不认识返回 <c>null</c>（表示「不暴露」）。</summary>
    public static string? KindOf(Type type)
    {
        var actual = Nullable.GetUnderlyingType(type) ?? type;
        if (actual == typeof(string) || actual == typeof(char))
        {
            return "text";
        }

        if (actual == typeof(bool))
        {
            return "bool";
        }

        if (actual.IsEnum)
        {
            return "enum";
        }

        if (actual == typeof(byte) || actual == typeof(sbyte)
            || actual == typeof(short) || actual == typeof(ushort)
            || actual == typeof(int) || actual == typeof(uint)
            || actual == typeof(long) || actual == typeof(ulong)
            || actual == typeof(float) || actual == typeof(double) || actual == typeof(decimal))
        {
            return "number";
        }

        if (actual == typeof(DateTime) || actual == typeof(DateTimeOffset) || actual == typeof(TimeSpan))
        {
            return "time";
        }

        if (typeof(IEnumerable).IsAssignableFrom(actual))
        {
            return "collection";
        }

        return null;
    }

    /// <summary>枚举型字段的可选值（其它类型返回空列表）。</summary>
    public static IReadOnlyList<string> ChoicesOf(Type type)
    {
        var actual = Nullable.GetUnderlyingType(type) ?? type;
        return actual.IsEnum ? Enum.GetNames(actual) : Array.Empty<string>();
    }

    /// <summary>把文本参数转成命令签名要求的类型；无参命令允许 <paramref name="argument"/> 为空。</summary>
    /// <param name="argument">参数文本。</param>
    /// <param name="targetType">命令参数类型；<c>null</c> 表示无参/非泛型命令。</param>
    /// <param name="payload">转换结果。</param>
    /// <param name="error">失败原因。</param>
    public static bool TryConvert(string? argument, Type? targetType, out object? payload, out string? error)
    {
        payload = null;
        error = null;

        if (targetType is null || targetType == typeof(object))
        {
            // 非泛型 ICommand：参数类型由实现决定，原样把文本递过去。
            payload = argument;
            return true;
        }

        var underlying = Nullable.GetUnderlyingType(targetType);
        var actual = underlying ?? targetType;
        var text = argument?.Trim();

        if (string.IsNullOrEmpty(text))
        {
            if (!targetType.IsValueType || underlying is not null)
            {
                payload = null;
                return true;
            }

            error = "该命令需要一个参数。";
            return false;
        }

        if (actual == typeof(string))
        {
            payload = argument;
            return true;
        }

        if (actual == typeof(bool))
        {
            switch (text.ToLowerInvariant())
            {
                case "true" or "1" or "yes" or "on" or "是" or "开":
                    payload = true;
                    return true;
                case "false" or "0" or "no" or "off" or "否" or "关":
                    payload = false;
                    return true;
                default:
                    error = "布尔值只认 true/false、1/0、yes/no、on/off。";
                    return false;
            }
        }

        if (actual.IsEnum)
        {
            foreach (var name in Enum.GetNames(actual))
            {
                if (string.Equals(name, text, StringComparison.OrdinalIgnoreCase))
                {
                    payload = Enum.Parse(actual, name);
                    return true;
                }
            }

            error = "可选值：" + string.Join("、", Enum.GetNames(actual));
            return false;
        }

        if (actual == typeof(int) && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intValue))
        {
            payload = intValue;
            return true;
        }

        if (actual == typeof(long) && long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var longValue))
        {
            payload = longValue;
            return true;
        }

        if (actual == typeof(double) && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var doubleValue))
        {
            payload = doubleValue;
            return true;
        }

        if (actual == typeof(decimal) && decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var decimalValue))
        {
            payload = decimalValue;
            return true;
        }

        if (actual == typeof(Guid) && Guid.TryParse(text, out var guidValue))
        {
            payload = guidValue;
            return true;
        }

        error = $"不支持自动转换到 {actual.Name}；请让用户在界面上直接操作这一项。";
        return false;
    }

    /// <summary>一个值的一句话表示（字符串截断、集合只给条数、其它用不变文化格式化）。</summary>
    public static string? FormatValue(object? value)
    {
        switch (value)
        {
            case null:
                return null;
            case string text:
                return text.Length <= MaxValueLength ? text : text[..MaxValueLength] + "…";
            case bool flag:
                return flag ? "true" : "false";
            case Enum enumValue:
                return enumValue.ToString();
            case ICollection collection:
                return $"{collection.Count} 项";
            case IEnumerable sequence:
                return $"{CountOf(sequence)} 项";
            case IFormattable formattable:
                return formattable.ToString(format: null, CultureInfo.InvariantCulture);
            default:
                return value.GetType().Name;
        }
    }

    /// <summary>
    /// 给页面视图模型的一个可写标量字段赋值（等价于用户在输入框 / 下拉框里改值）。
    /// </summary>
    /// <param name="viewModel">页面视图模型。</param>
    /// <param name="field">公共属性名，大小写不敏感。</param>
    /// <param name="value">新值文本；布尔用 true/false，枚举用名字。</param>
    /// <param name="result">成功时带「旧值 → 新值」，失败时带原因。</param>
    /// <returns>字段存在、可写、值能转换且写入成功时为 <c>true</c>。</returns>
    public static bool TrySetField(object? viewModel, string field, string? value, out OperationResult<string> result)
    {
        if (viewModel is null)
        {
            result = OperationResult<string>.Fail("当前没有打开的页面，没有写入任何字段。");
            return false;
        }

        if (string.IsNullOrWhiteSpace(field))
        {
            result = OperationResult<string>.Fail("字段名不能为空。", "用 ckan_ui_state 看这一页有哪些字段。");
            return false;
        }

        var wanted = field.Trim();
        PropertyInfo? property = null;
        foreach (var candidate in viewModel.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (candidate.GetIndexParameters().Length == 0
                && string.Equals(candidate.Name, wanted, StringComparison.OrdinalIgnoreCase))
            {
                property = candidate;
                break;
            }
        }

        if (property is null)
        {
            var writable = DescribeFields(viewModel)
                .Where(item => item.Kind != "collection" && !IsSensitive(item.Name))
                .Select(item => item.Name);
            result = OperationResult<string>.Fail(
                $"`{viewModel.GetType().Name}` 上没有叫「{wanted}」的字段。",
                "可写字段：" + string.Join("、", writable));
            return false;
        }

        var setter = property.SetMethod;
        if (setter is null || !setter.IsPublic || setter.IsStatic)
        {
            result = OperationResult<string>.Fail($"字段「{property.Name}」是只读的，不能赋值。");
            return false;
        }

        if (IsSensitive(property.Name))
        {
            result = OperationResult<string>.Fail(
                $"字段「{property.Name}」属于密钥类值，拒绝从对话写入。",
                "这类值请让用户在设置页自己填。");
            return false;
        }

        var kind = KindOf(property.PropertyType);
        if (kind is null || kind == "collection")
        {
            result = OperationResult<string>.Fail(
                $"字段「{property.Name}」的类型是 {property.PropertyType.Name}，不支持直接赋值。",
                "只能改文本 / 布尔 / 数字 / 枚举 / 时间字段；集合和复杂对象请用页面命令操作。");
            return false;
        }

        if (!TryConvert(value, property.PropertyType, out var payload, out var convertError))
        {
            var reason = string.IsNullOrWhiteSpace(value) && property.PropertyType.IsValueType
                ? "该字段需要一个非空值。"
                : convertError;
            result = OperationResult<string>.Fail($"字段「{property.Name}」的值「{value}」无法转换：{reason}");
            return false;
        }

        string oldText;
        try
        {
            oldText = FormatValue(property.GetValue(viewModel)) ?? "null";
        }
        catch (Exception)
        {
            oldText = "?";
        }

        try
        {
            property.SetValue(viewModel, payload);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            result = OperationResult<string>.Fail($"字段「{property.Name}」写入失败。", ex.InnerException.Message);
            return false;
        }
        catch (Exception ex)
        {
            result = OperationResult<string>.Fail($"字段「{property.Name}」写入失败。", ex.Message);
            return false;
        }

        string newText;
        try
        {
            newText = FormatValue(property.GetValue(viewModel)) ?? "null";
        }
        catch (Exception)
        {
            newText = value ?? "null";
        }

        result = OperationResult<string>.Ok($"「{property.Name}」：{oldText} → {newText}。");
        return true;
    }

    private static IEnumerable<(PropertyInfo Property, ICommand Command)> EnumerateCommands(object viewModel)
    {
        var properties = viewModel.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
        Array.Sort(properties, static (left, right) => string.CompareOrdinal(left.Name, right.Name));

        foreach (var property in properties)
        {
            if (property.GetIndexParameters().Length > 0
                || !typeof(ICommand).IsAssignableFrom(property.PropertyType))
            {
                continue;
            }

            var getter = property.GetMethod;
            if (getter is null || !getter.IsPublic || getter.IsStatic)
            {
                continue;
            }

            ICommand? command = null;
            try
            {
                command = property.GetValue(viewModel) as ICommand;
            }
            catch (Exception)
            {
                continue;
            }

            if (command is not null)
            {
                yield return (property, command);
            }
        }
    }

    private static bool CanExecute(ICommand command, object? parameter)
    {
        try
        {
            return command.CanExecute(parameter);
        }
        catch (Exception)
        {
            // 泛型命令收到 null 会当场 cast 失败；这种情况等同于「不可执行」。
            return false;
        }
    }

    /// <summary>执行命令；异步命令返回它的 Task，同步命令返回 <c>null</c>。</summary>
    private static Task? ExecuteAsync(ICommand command, object? parameter)
    {
        foreach (var contract in command.GetType().GetInterfaces())
        {
            if (contract.FullName is not "CommunityToolkit.Mvvm.Input.IAsyncRelayCommand"
                && contract.FullName is not "CommunityToolkit.Mvvm.Input.IAsyncRelayCommand`1")
            {
                continue;
            }

            var method = contract.GetMethod("ExecuteAsync", [typeof(object)]);
            if (method?.Invoke(command, [parameter]) is Task task)
            {
                return task;
            }
        }

        command.Execute(parameter);
        return null;
    }

    private static Type? ParameterTypeOf(Type commandType)
    {
        foreach (var contract in commandType.GetInterfaces())
        {
            if (!contract.IsGenericType || contract.GetGenericArguments().Length != 1)
            {
                continue;
            }

            var name = contract.GetGenericTypeDefinition().Name;
            if (name is "IRelayCommand`1" or "IAsyncRelayCommand`1" or "ICommand`1")
            {
                var argument = contract.GetGenericArguments()[0];
                return argument == typeof(object) ? null : argument;
            }
        }

        return null;
    }

    private static bool IsSensitive(string name)
    {
        var lower = name.ToLowerInvariant();
        return SensitiveMarkers.Any(marker => lower.Contains(marker, StringComparison.Ordinal));
    }

    private static string StripCommandSuffix(string name) =>
        name.EndsWith("Command", StringComparison.Ordinal) && name.Length > "Command".Length
            ? name[..^"Command".Length]
            : name;

    private static int CountOf(IEnumerable sequence)
    {
        try
        {
            var count = 0;
            foreach (var _ in sequence)
            {
                count++;
                if (count >= 1000)
                {
                    break;
                }
            }

            return count;
        }
        catch (Exception)
        {
            return 0;
        }
    }
}
