namespace CkanUI.Services.Abstractions;

/// <summary>无返回值的服务结果（01 篇 §8 统一返回类型）。</summary>
public sealed record OperationResult
{
    private OperationResult(bool ok, string? message, string? detail, bool retryable)
    {
        IsSuccess = ok;
        Message = message;
        Detail = detail;
        Retryable = retryable;
    }

    public bool IsSuccess { get; }

    public string? Message { get; }

    public string? Detail { get; }

    public bool Retryable { get; }

    public static OperationResult Ok() => new(true, null, null, false);

    public static OperationResult Fail(string message, string? detail = null, bool retryable = false) =>
        new(false, message, detail, retryable);

    /// <summary>把泛型结果降级成无值结果，保留消息与重试标记。</summary>
    public static OperationResult From<T>(OperationResult<T> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return source.IsSuccess
            ? Ok()
            : Fail(source.Message ?? "操作失败", source.Detail, source.Retryable);
    }
}

/// <summary>服务层统一返回类型：成功值 / 用户可读消息 / 技术细节 / 是否可重试（01 篇 §8）。</summary>
public sealed record OperationResult<T>
{
    private OperationResult(bool ok, T? value, string? message, string? detail, bool retryable)
    {
        IsSuccess = ok;
        Value = value;
        Message = message;
        Detail = detail;
        Retryable = retryable;
    }

    public bool IsSuccess { get; }

    public T? Value { get; }

    public string? Message { get; }

    public string? Detail { get; }

    public bool Retryable { get; }

    public static OperationResult<T> Ok(T value) => new(true, value, null, null, false);

    public static OperationResult<T> Fail(string message, string? detail = null, bool retryable = false) =>
        new(false, default, message, detail, retryable);
}
