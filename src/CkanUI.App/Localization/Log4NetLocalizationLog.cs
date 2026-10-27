using CkanUI.Localization;
using log4net;

namespace CkanUI.App.Localization;

/// <summary>把本地化层的告警接到 log4net（本地化层自己不引日志包，见 docs/ops/i18n-guide.md §6.1）。</summary>
public sealed class Log4NetLocalizationLog : ILocalizationLog
{
    private static readonly ILog Log = LogManager.GetLogger("CKAN.UI.Localization");

    /// <inheritdoc />
    public void Warn(string message) => Log.Warn(message);
}
