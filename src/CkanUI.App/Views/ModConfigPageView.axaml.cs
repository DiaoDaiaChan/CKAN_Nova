using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using CkanUI.ViewModels;
using CkanUI.ViewModels.Pages;
using CkanUI.ViewModels.Pages.ModConfig;

namespace CkanUI.App.Views;

/// <summary>
/// Mod 配置页视图（M9 / UI-CFG-01 骨架 + CFG-04 编辑控件）：只做绑定、键盘快捷方式与生命周期接线，
/// 业务全在 <see cref="CkanUI.ViewModels.Pages.ModConfigPageViewModel"/> 里。
/// </summary>
/// <remarks>
/// 键盘：Ctrl+S 保存整页（隧道阶段接住，焦点在搜索框/文本框里也能用）；
/// 单行编辑框里 Enter 提交当前行，多行（list）编辑框里 Ctrl+Enter 才提交，Enter 留给换行。
/// 视图挂到可视树时激活 VM（每次进页面重读配置），移出时取消激活。
/// </remarks>
public partial class ModConfigPageView : UserControl
{
    /// <summary>构造视图（ViewLocator 无参构造）。</summary>
    public ModConfigPageView()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnRootKeyDown, RoutingStrategies.Tunnel);
        AttachedToVisualTree += (_, _) =>
        {
            // 路径字段的「浏览…」要走系统选择器，而 StorageProvider 只有视图层拿得到：
            // 每次挂上可视树都重挂一次（页面是单例，DataContext 可能在首次挂载之后才设）。
            if (DataContext is ModConfigPageViewModel page)
            {
                page.PathPicker = PickPathAsync;
                page.AdapterPackagePicker = PickAdapterPackageAsync;
            }

            (DataContext as PageViewModel)?.Activate();
        };
        DetachedFromVisualTree += (_, _) => (DataContext as PageViewModel)?.Deactivate();
    }

    private void OnRootKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not ModConfigPageViewModel vm
            || e.Key != Key.S
            || !e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            return;
        }

        if (vm.SaveCommand.CanExecute(null))
        {
            vm.SaveCommand.Execute(null);
        }

        e.Handled = true;
    }

    private void OnEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter
            || DataContext is not ModConfigPageViewModel vm
            || sender is not TextBox { DataContext: ModConfigFieldRowViewModel row })
        {
            return;
        }

        // 多行（list）框里 Enter 是换行，提交要按 Ctrl+Enter。
        if (row.IsMultilineEditor && !e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            return;
        }

        if (row.HasPendingChange && vm.SaveRowCommand.CanExecute(row))
        {
            vm.SaveRowCommand.Execute(row);
        }

        e.Handled = true;
    }

    /// <summary>
    /// 按键字段的捕获（keybinding）：按住 Ctrl/Alt/Win 时把组合键写进草稿。
    /// </summary>
    /// <remarks>
    /// 只拦"带命令修饰键"的按法，好处是 Shift+字母仍然是正常输入大写字母，
    /// 用户也可以直接手打 KSP 自己的键名（例如 <c>escape</c>、<c>keypadmultiply</c>）。
    /// Esc 清空；Tab 与方向键一律放行，别抢键盘导航与读屏。
    /// </remarks>
    private void OnKeyCaptureKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox { DataContext: ModConfigFieldRowViewModel row })
        {
            return;
        }

        if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None)
        {
            row.EditValue = string.Empty;
            e.Handled = true;
            return;
        }

        if (e.Key is Key.Tab or Key.Left or Key.Right or Key.Up or Key.Down
            or Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
            or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin)
        {
            return;
        }

        var modifiers = e.KeyModifiers;
        var captures = modifiers.HasFlag(KeyModifiers.Control)
            || modifiers.HasFlag(KeyModifiers.Alt)
            || modifiers.HasFlag(KeyModifiers.Meta);
        if (!captures)
        {
            return;
        }

        row.EditValue = KeyGestureText.Format(
            ctrl: modifiers.HasFlag(KeyModifiers.Control),
            alt: modifiers.HasFlag(KeyModifiers.Alt),
            shift: modifiers.HasFlag(KeyModifiers.Shift),
            meta: modifiers.HasFlag(KeyModifiers.Meta),
            key: e.Key.ToString());
        e.Handled = true;
    }

    /// <summary>adapter 包选择器（ADP-04）：挑一个 .json 或 .zip，取不到 TopLevel 时返回 null。</summary>
    private async Task<string?> PickAdapterPackageAsync()
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is not { CanOpen: true })
        {
            return null;
        }

        var files = await storage
            .OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "选择适配器包（.json 或 .zip）",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("适配器包")
                    {
                        Patterns = ["*.json", "*.zip"],
                        MimeTypes = ["application/json", "application/zip"],
                    },
                ],
            })
            .ConfigureAwait(true);

        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    /// <summary>目录选择器：「浏览…」挑一个路径填进当前行（取不到 TopLevel 时返回 null，按钮点了没反应但不会崩）。</summary>
    /// <param name="row">当前字段行（用它的现值当起始目录）。</param>
    private async Task<string?> PickPathAsync(ModConfigFieldRowViewModel row)
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is not { CanPickFolder: true })
        {
            return null;
        }

        // 现值是相对路径（相对游戏目录）时无法定位，起始目录交给系统默认。
        var start = default(IStorageFolder);
        var current = row.EditValue?.Trim();
        if (!string.IsNullOrEmpty(current) && Path.IsPathRooted(current))
        {
            var dir = Directory.Exists(current) ? current : Path.GetDirectoryName(current);
            if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
            {
                start = await storage.TryGetFolderFromPathAsync(dir).ConfigureAwait(true);
            }
        }

        var folders = await storage
            .OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "选择「" + row.Label + "」的路径",
                AllowMultiple = false,
                SuggestedStartLocation = start,
            })
            .ConfigureAwait(true);

        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }
}
