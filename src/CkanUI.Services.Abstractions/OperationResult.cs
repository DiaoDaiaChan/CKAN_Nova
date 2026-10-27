namespace CkanUI.Services.Abstractions;

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
