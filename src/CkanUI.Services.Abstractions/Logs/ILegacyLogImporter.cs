namespace CkanUI.Services.Abstractions.Logs;

/// <summary>
/// 旧 <c>CKAN.log</c> 导入（14 篇 §8 LOG-02）：用户升级后想查上次崩溃的原因，历史日志必须能进同一套视图。
/// 返回导入条数——UI 用它提示「已导入 N 行」。
/// </summary>
public interface ILegacyLogImporter
{
    /// <summary>解析并导入旧日志文件。文件不存在/格式不符返回失败结果（不抛）。</summary>
    OperationResult<int> Import(string logPath);
}
