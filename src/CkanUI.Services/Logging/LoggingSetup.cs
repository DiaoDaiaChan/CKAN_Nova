using log4net;
using log4net.Config;

namespace CkanUI.Services.Logging;

/// <summary>日志落位（01 篇 §7 / 02 篇 §2）：与 Core 共用 %LocalAppData%/CKAN/CKAN.log。</summary>
public static class LoggingSetup
{
    public static ILog GetLogger(string name) => LogManager.GetLogger(name);

    public static void Initialize(string configPath)
    {
        if (File.Exists(configPath))
        {
            XmlConfigurator.Configure(new FileInfo(configPath));
        }
        else
        {
            BasicConfigurator.Configure();
        }
    }
}
