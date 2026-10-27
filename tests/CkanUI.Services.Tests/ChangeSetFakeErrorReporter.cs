using System;
using System.Collections.Generic;
using CkanUI.Services.Abstractions;

namespace CkanUI.Services.Tests;

/// <summary>记录所有上报的 IErrorReporter fake。</summary>
public sealed class ChangeSetFakeErrorReporter : IErrorReporter
{
    /// <summary>已上报的条目，按上报顺序排列。</summary>
    public List<ErrorEntry> Entries { get; } = new List<ErrorEntry>();

    /// <inheritdoc />
    public IReadOnlyList<ErrorEntry> Recent => Entries;

    /// <inheritdoc />
    public event Action<ErrorEntry>? Reported;

    /// <inheritdoc />
    public void Report(Exception exception, string context, string? userMessage = null)
        => Record(new ErrorEntry(DateTime.UtcNow, context, userMessage ?? exception.Message,
                                 exception.Message, exception));

    /// <inheritdoc />
    public void Report(string context, string userMessage, string? detail = null)
        => Record(new ErrorEntry(DateTime.UtcNow, context, userMessage, detail, null));

    private void Record(ErrorEntry entry)
    {
        Entries.Add(entry);
        Reported?.Invoke(entry);
    }
}
