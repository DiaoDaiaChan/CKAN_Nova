using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.ViewModels.Pages.InstallHistory;

/// <summary>安装历史页的文案拼装（全中文，错误一律标注可否重试；与 <c>ModPacksErrorText</c> 同构）。</summary>
public static class InstallHistoryText
{
    /// <summary>操作类型的中文标签。</summary>
    public static string OperationLabel(InstallHistoryOperation operation) => operation switch
    {
        InstallHistoryOperation.Install => "安装",
        InstallHistoryOperation.Upgrade => "升级",
        InstallHistoryOperation.Downgrade => "降级",
        InstallHistoryOperation.Remove => "卸载",
        InstallHistoryOperation.Replace => "替换",
        InstallHistoryOperation.Restore => "从历史重装",
        _ => "变更",
    };

    /// <summary>结果的中文标签。</summary>
    public static string OutcomeLabel(InstallHistoryOutcome outcome) => outcome switch
    {
        InstallHistoryOutcome.Succeeded => "成功",
        InstallHistoryOutcome.PartiallyFailed => "部分失败",
        InstallHistoryOutcome.Failed => "失败",
        InstallHistoryOutcome.Cancelled => "已取消",
        _ => "未知",
    };

    /// <summary>模组在快照与当前注册表之间的状态文案。</summary>
    /// <param name="module">快照里的模组。</param>
    public static string ModuleStateLabel(InstallHistoryModule module)
    {
        ArgumentNullException.ThrowIfNull(module);
        if (!module.IsInstalled)
        {
            return "未安装";
        }

        return module.IsChanged
            ? "版本不同（当前 " + (module.InstalledVersion ?? "未知") + "）"
            : "已安装";
    }

    /// <summary>把服务层消息与可否重试拼成一句用户可读的话。</summary>
    public static string Compose(string? message, string fallback, bool retryable)
    {
        var body = string.IsNullOrWhiteSpace(message) ? fallback : message!;
        return retryable ? body + "（可重试）" : body + "（不可重试）";
    }

    /// <summary>把异常转成中文错误文案（页面不把异常抛给界面）。</summary>
    public static string FromException(Exception exception, string fallback)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return fallback + "：" + exception.Message + "（不可重试）";
    }
}
