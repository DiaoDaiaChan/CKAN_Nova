using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace CkanUI.Tools.Installer;

internal sealed class UninstallerWizard
{
    private const int ID_BTN_UNINSTALL = 1001;
    private const int ID_BTN_CANCEL = 1002;
    private const int ID_BTN_FINISH = 1003;

    private const int WindowWidth = 560;
    private const int WindowHeight = 400;
    private const int HeaderHeight = 72;
    private const int FooterTop = 332;

    private const int ID_CHK_REMOVE_USERDATA = 2001;
    private const int ID_PROGRESS = 3001;
    private const int ID_STATIC_STATUS = 3002;

    private static UninstallerWizard? _current;

    private readonly InstallerOptions _options;
    private readonly string _installDir;
    private IntPtr _hWnd;
    private IntPtr _hFontTitle;

    private int _currentStep; // 0: Confirm, 1: Uninstalling, 2: Finished

    private IntPtr _hBtnUninstall;
    private IntPtr _hBtnCancel;
    private IntPtr _hBtnFinish;
    private IntPtr _hChkUserData;

    private IntPtr _hProgress;
    private IntPtr _hStaticStatus;

    private readonly List<IntPtr> _step0Controls = new();
    private readonly List<IntPtr> _step1Controls = new();
    private readonly List<IntPtr> _step2Controls = new();

    private bool _removeUserData;
    private int _exitCode;
    private string? _uninstallError;

    public UninstallerWizard(InstallerOptions options, string installDir)
    {
        _options = options;
        _installDir = installDir;
    }

    public static int Run(InstallerOptions options, string installDir)
    {
        var icc = new NativeMethods.INITCOMMONCONTROLSEX
        {
            dwSize = Marshal.SizeOf<NativeMethods.INITCOMMONCONTROLSEX>(),
            dwICC = NativeMethods.ICC_PROGRESS_CLASS | NativeMethods.ICC_STANDARD_CLASSES
        };
        NativeMethods.InitCommonControlsEx(ref icc);
        InstallerTheme.Init();

        var wizard = new UninstallerWizard(options, installDir);
        _current = wizard;
        return wizard.Show();
    }

    private unsafe int Show()
    {
        var hInstance = NativeMethods.GetModuleHandle(null);
        var className = "CkanUI_Uninstaller_Wizard";

        var wndClass = new NativeMethods.WNDCLASSEXW
        {
            cbSize = Marshal.SizeOf<NativeMethods.WNDCLASSEXW>(),
            style = 0,
            lpfnWndProc = (IntPtr)(delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr, IntPtr, IntPtr>)&WndProc,
            cbClsExtra = 0,
            cbWndExtra = 0,
            hInstance = hInstance,
            hIcon = IntPtr.Zero,
            hCursor = NativeMethods.LoadCursorW(IntPtr.Zero, (IntPtr)NativeMethods.IDC_ARROW),
            hbrBackground = IntPtr.Zero,
            lpszMenuName = null,
            lpszClassName = className,
            hIconSm = IntPtr.Zero
        };

        NativeMethods.RegisterClassEx(ref wndClass);

        _hFontTitle = InstallerTheme.FontTitle;

        var screenW = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXSCREEN);
        var screenH = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYSCREEN);
        var style = NativeMethods.WS_OVERLAPPED | NativeMethods.WS_CAPTION | NativeMethods.WS_SYSMENU
            | NativeMethods.WS_MINIMIZEBOX | NativeMethods.WS_CLIPCHILDREN;
        var frame = new NativeMethods.RECT { Left = 0, Top = 0, Right = WindowWidth, Bottom = WindowHeight };
        NativeMethods.AdjustWindowRectEx(ref frame, style, false, 0);
        var winW = frame.Right - frame.Left;
        var winH = frame.Bottom - frame.Top;
        var posX = (screenW - winW) / 2;
        var posY = (screenH - winH) / 2;

        _hWnd = NativeMethods.CreateWindowEx(
            0,
            className,
            "CKAN UI 卸载向导",
            style,
            posX,
            posY,
            winW,
            winH,
            IntPtr.Zero,
            IntPtr.Zero,
            hInstance,
            IntPtr.Zero);

        InstallerTheme.ApplyChrome(_hWnd);

        CreateControls();
        UpdateStepView();

        NativeMethods.ShowWindow(_hWnd, NativeMethods.SW_SHOW);
        NativeMethods.UpdateWindow(_hWnd);

        while (NativeMethods.GetMessage(out var msg, IntPtr.Zero, 0, 0))
        {
            NativeMethods.TranslateMessage(ref msg);
            NativeMethods.DispatchMessage(ref msg);
        }

        Cleanup();
        return _exitCode;
    }

    private void Cleanup()
    {
        // 字体/画刷由 InstallerTheme 统一持有并释放。
        InstallerTheme.Dispose();
    }

    private void CreateControls()
    {
        var btnStyle = NativeMethods.WS_CHILD | NativeMethods.WS_VISIBLE | NativeMethods.BS_OWNERDRAW | NativeMethods.WS_TABSTOP;

        // 卸载=危险操作，但按钮仍是主操作样式；取消放左侧远离主按钮，减少误触。
        _hBtnCancel = CreateWindowControl("BUTTON", "取消", btnStyle, 24, 352, 84, 32, ID_BTN_CANCEL);
        _hBtnUninstall = CreateWindowControl("BUTTON", "卸载", btnStyle, 404, 352, 132, 32, ID_BTN_UNINSTALL);
        _hBtnFinish = CreateWindowControl("BUTTON", "完成", btnStyle, 404, 352, 132, 32, ID_BTN_FINISH);

        InstallerTheme.StyleButton(_hBtnCancel, primary: false);
        InstallerTheme.StyleButton(_hBtnUninstall, primary: true);
        InstallerTheme.StyleButton(_hBtnFinish, primary: true);

        // --- 第 0 步：确认 ---
        var c1 = CreateWindowControl("STATIC",
            "是否确定从本机完全卸载 CKAN UI？\r\n\r\n" +
            "目标目录: " + _installDir + "\r\n\r\n" +
            "卸载会删除该目录下的 CKAN UI 程序文件与快捷方式。\r\n" +
            "你的 KSP 游戏实例、已安装模组及共享缓存会默认保留。",
            NativeMethods.WS_CHILD | NativeMethods.SS_LEFT, 24, 96, 512, 140, 0);
        _hChkUserData = CreateWindowControl("BUTTON", "同时删除用户数据（CKAN UI 日志与自有配置）",
            NativeMethods.WS_CHILD | NativeMethods.BS_AUTOCHECKBOX | NativeMethods.WS_TABSTOP, 24, 248, 512, 24, ID_CHK_REMOVE_USERDATA);

        SetFont(c1, InstallerTheme.FontBody);
        InstallerTheme.StyleCheckBox(_hChkUserData);
        _step0Controls.AddRange([c1, _hChkUserData]);

        // --- 第 1 步：卸载中 ---
        _hStaticStatus = CreateWindowControl("STATIC", "正在卸载 CKAN UI...",
            NativeMethods.WS_CHILD | NativeMethods.SS_LEFT, 24, 108, 512, 22, ID_STATIC_STATUS);
        _hProgress = CreateWindowControl("msctls_progress32", "", NativeMethods.WS_CHILD, 24, 140, 512, 10, ID_PROGRESS);
        NativeMethods.SendMessage(_hProgress, NativeMethods.PBM_SETRANGE32, IntPtr.Zero, (IntPtr)100);
        InstallerTheme.StyleProgress(_hProgress);

        SetFont(_hStaticStatus, InstallerTheme.FontStrong);
        _step1Controls.AddRange([_hStaticStatus, _hProgress]);

        // --- 第 2 步：完成 ---
        var f1 = CreateWindowControl("STATIC",
            "CKAN UI 已从本机卸载。\r\n\r\n" +
            "快捷方式、注册表卸载项与程序文件均已清理。\r\n" +
            "开始菜单与桌面上的 CKAN UI 图标此后不应再出现。\r\n\r\n" +
            "感谢使用 CKAN UI！",
            NativeMethods.WS_CHILD | NativeMethods.SS_LEFT, 24, 96, 512, 140, 0);
        SetFont(f1, InstallerTheme.FontBody);
        _step2Controls.Add(f1);
    }

    private IntPtr CreateWindowControl(string className, string text, uint style, int x, int y, int w, int h, int id)
    {
        var hInst = NativeMethods.GetModuleHandle(null);
        return NativeMethods.CreateWindowEx(0, className, text, style, x, y, w, h, _hWnd, (IntPtr)id, hInst, IntPtr.Zero);
    }

    private static void SetFont(IntPtr hWnd, IntPtr hFont)
    {
        NativeMethods.SendMessage(hWnd, NativeMethods.WM_SETFONT, hFont, (IntPtr)1);
    }

    private void UpdateStepView()
    {
        SetControlsVisible(_step0Controls, _currentStep == 0);
        SetControlsVisible(_step1Controls, _currentStep == 1);
        SetControlsVisible(_step2Controls, _currentStep == 2);

        switch (_currentStep)
        {
            case 0:
                NativeMethods.ShowWindow(_hBtnUninstall, NativeMethods.SW_SHOW);
                NativeMethods.ShowWindow(_hBtnCancel, NativeMethods.SW_SHOW);
                NativeMethods.ShowWindow(_hBtnFinish, NativeMethods.SW_HIDE);
                break;

            case 1:
                NativeMethods.EnableWindow(_hBtnUninstall, false);
                NativeMethods.EnableWindow(_hBtnCancel, false);
                break;

            case 2:
                NativeMethods.ShowWindow(_hBtnUninstall, NativeMethods.SW_HIDE);
                NativeMethods.ShowWindow(_hBtnCancel, NativeMethods.SW_HIDE);
                NativeMethods.ShowWindow(_hBtnFinish, NativeMethods.SW_SHOW);
                break;
        }

        NativeMethods.InvalidateRect(_hWnd, IntPtr.Zero, true);
        NativeMethods.UpdateWindow(_hWnd);
    }

    private static void SetControlsVisible(List<IntPtr> controls, bool visible)
    {
        var flag = visible ? NativeMethods.SW_SHOW : NativeMethods.SW_HIDE;
        foreach (var c in controls)
        {
            NativeMethods.ShowWindow(c, flag);
        }
    }

    private void OnUninstall()
    {
        _removeUserData = NativeMethods.SendMessage(_hChkUserData, NativeMethods.BM_GETCHECK, IntPtr.Zero, IntPtr.Zero) == NativeMethods.BST_CHECKED;
        _currentStep = 1;
        UpdateStepView();

        var opt = new InstallerOptions
        {
            IsUninstall = true,
            Quiet = true,
            Force = true,
            InstallDir = _installDir,
            RemoveUserData = _removeUserData
        };

        Task.Run(() =>
        {
            try
            {
                var code = UninstallerEngine.ExecuteUninstall(opt, (pct, status) =>
                {
                    NativeMethods.PostMessage(_hWnd, NativeMethods.WM_APP_PROGRESS, (IntPtr)pct, IntPtr.Zero);
                });

                _exitCode = code;
                NativeMethods.PostMessage(_hWnd, NativeMethods.WM_APP_COMPLETE, (IntPtr)code, IntPtr.Zero);
            }
            catch (Exception ex)
            {
                _uninstallError = ex.Message;
                _exitCode = 3;
                NativeMethods.PostMessage(_hWnd, NativeMethods.WM_APP_COMPLETE, (IntPtr)3, IntPtr.Zero);
            }
        });
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        var inst = _current;
        if (inst == null) return NativeMethods.DefWindowProc(hWnd, msg, wParam, lParam);

        switch (msg)
        {
            case NativeMethods.WM_COMMAND:
            {
                var id = (int)(wParam.ToInt64() & 0xFFFF);
                if (id == ID_BTN_UNINSTALL) inst.OnUninstall();
                else if (id == ID_BTN_CANCEL) NativeMethods.DestroyWindow(hWnd);
                else if (id == ID_BTN_FINISH) NativeMethods.DestroyWindow(hWnd);
                break;
            }

            case NativeMethods.WM_APP_PROGRESS:
            {
                var pct = (int)wParam;
                NativeMethods.SendMessage(inst._hProgress, NativeMethods.PBM_SETPOS, (IntPtr)pct, IntPtr.Zero);
                break;
            }

            case NativeMethods.WM_APP_COMPLETE:
            {
                var code = (int)wParam;
                if (code == 0)
                {
                    inst._currentStep = 2;
                    inst.UpdateStepView();
                }
                else
                {
                    var msgText = "卸载过程中遇到异常：\r\n\r\n" + (inst._uninstallError ?? "未知错误");
                    NativeMethods.MessageBox(hWnd, msgText, "卸载错误", NativeMethods.MB_OK | NativeMethods.MB_ICONERROR);
                    NativeMethods.DestroyWindow(hWnd);
                }
                break;
            }

            case NativeMethods.WM_ERASEBKGND:
            {
                var rect = new NativeMethods.RECT { Left = 0, Top = 0, Right = WindowWidth, Bottom = WindowHeight };
                NativeMethods.FillRect(wParam, ref rect, InstallerTheme.BrushCanvas);
                return (IntPtr)1;
            }

            case NativeMethods.WM_PAINT:
            {
                var hdc = NativeMethods.BeginPaint(hWnd, out var ps);
                inst.Paint(hdc);
                NativeMethods.EndPaint(hWnd, ref ps);
                return IntPtr.Zero;
            }

            case NativeMethods.WM_DRAWITEM:
            {
                var dis = Marshal.PtrToStructure<NativeMethods.DRAWITEMSTRUCT>(lParam);
                var primary = dis.hwndItem == inst._hBtnUninstall || dis.hwndItem == inst._hBtnFinish;
                InstallerTheme.DrawButton(in dis, primary);
                return (IntPtr)1;
            }

            case NativeMethods.WM_CTLCOLORSTATIC:
            case NativeMethods.WM_CTLCOLORBTN:
            {
                var hdc = wParam;
                NativeMethods.SetBkMode(hdc, NativeMethods.TRANSPARENT);
                NativeMethods.SetTextColor(hdc, InstallerTheme.TextPrimary);
                return InstallerTheme.BrushCanvas;
            }

            case NativeMethods.WM_CLOSE:
                NativeMethods.DestroyWindow(hWnd);
                return IntPtr.Zero;

            case NativeMethods.WM_DESTROY:
                NativeMethods.PostQuitMessage(0);
                return IntPtr.Zero;
        }

        return NativeMethods.DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private void Paint(IntPtr hdc)
    {
        var client = new NativeMethods.RECT { Left = 0, Top = 0, Right = WindowWidth, Bottom = WindowHeight };
        NativeMethods.FillRect(hdc, ref client, InstallerTheme.BrushCanvas);
        DrawHeader(hdc);
        DrawFooter(hdc);
    }

    private void DrawHeader(IntPtr hdc)
    {
        var headerRect = new NativeMethods.RECT { Left = 0, Top = 0, Right = WindowWidth, Bottom = HeaderHeight + 1 };
        NativeMethods.FillRect(hdc, ref headerRect, InstallerTheme.BrushSurface);

        var accentBrush = NativeMethods.CreateSolidBrush(InstallerTheme.Accent);
        var accentRect = new NativeMethods.RECT { Left = 0, Top = 0, Right = WindowWidth, Bottom = 3 };
        NativeMethods.FillRect(hdc, ref accentRect, accentBrush);
        NativeMethods.DeleteObject(accentBrush);

        var lineBrush = NativeMethods.CreateSolidBrush(InstallerTheme.Border);
        var lineRect = new NativeMethods.RECT { Left = 0, Top = HeaderHeight, Right = WindowWidth, Bottom = HeaderHeight + 1 };
        NativeMethods.FillRect(hdc, ref lineRect, lineBrush);
        NativeMethods.DeleteObject(lineBrush);

        var (title, subtitle) = _currentStep switch
        {
            0 => ("卸载 CKAN UI", "从本机移除 CKAN UI 及其系统集成。"),
            1 => ("正在卸载", "正在清理程序文件与注册表项，请稍候。"),
            2 => ("卸载完成", "CKAN UI 已从本机移除。"),
            _ => ("CKAN UI 卸载程序", "")
        };

        NativeMethods.SetBkMode(hdc, NativeMethods.TRANSPARENT);

        NativeMethods.SelectObject(hdc, _hFontTitle);
        NativeMethods.SetTextColor(hdc, InstallerTheme.TextPrimary);
        TextOut(hdc, 24, 20, title);

        NativeMethods.SelectObject(hdc, InstallerTheme.FontSubtitle);
        NativeMethods.SetTextColor(hdc, InstallerTheme.TextSecondary);
        TextOut(hdc, 24, 48, subtitle);
    }

    private static void DrawFooter(IntPtr hdc)
    {
        var footerRect = new NativeMethods.RECT { Left = 0, Top = FooterTop, Right = WindowWidth, Bottom = WindowHeight };
        NativeMethods.FillRect(hdc, ref footerRect, InstallerTheme.BrushSurface);

        var lineBrush = NativeMethods.CreateSolidBrush(InstallerTheme.Border);
        var lineRect = new NativeMethods.RECT { Left = 0, Top = FooterTop, Right = WindowWidth, Bottom = FooterTop + 1 };
        NativeMethods.FillRect(hdc, ref lineRect, lineBrush);
        NativeMethods.DeleteObject(lineBrush);
    }

    private static void TextOut(IntPtr hdc, int x, int y, string str)
    {
        NativeMethods.TextOut(hdc, x, y, str, str.Length);
    }
}
