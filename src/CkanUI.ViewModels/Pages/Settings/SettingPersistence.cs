namespace CkanUI.ViewModels.Pages.Settings;

/// <summary>设置项的持久化去向（决定保存时走哪个服务）。</summary>
public enum SettingPersistence
{
    /// <summary>写入 Core 的 config.json（<c>ISettingsService.SetAndVerifyAsync</c>，改一项写一项 + 读回校验）。</summary>
    Core,

    /// <summary>写入 UI 自有的 ckan-ui.json（<c>IUiSettingsStore.Save</c>，外观类设置不进 Core 配置）。</summary>
    UiSettings,

    /// <summary>
    /// 写入实例级文件（如 <c>&lt;实例目录&gt;/CKAN/stability_tolerance.json</c>）：
    /// 既不是 Core 的 config.json，也不是 ckan-ui.json，由页面按文件契约直接读写。
    /// </summary>
    InstanceFile,
}
