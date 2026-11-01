using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace CkanUI.Tools.Installer;

internal sealed class UninstallerWizard
{
    private const int ID_BTN_UNINSTALL = 1001;
    private const int ID_BTN_CANCEL = 1002;
    private const int ID_BTN_FINISH = 1003;

    private const int ID_CHK_REMOVE_USERDATA = 2001;
    private const int ID_PROGRESS = 3001;
    private const int ID_STATIC_STATUS = 3002;

    private static UninstallerWizard? _current;

    private readonly InstallerOptions _options;
    private readonly string _installDir;
    private IntPtr _hWnd;
    private IntPtr _hFontTitle;
    private IntPtr _hFontNormal;
    private IntPtr _hFontBold;
    private IntPtr _hBrushHeader;
    private IntPtr _hBrushBody;

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
            hbrBackground = (IntPtr)(NativeMethods.COLOR_BTNFACE + 1),
            lpszMenuName = null,
            lpszClassName = className,
            hIconSm = IntPtr.Zero
        };

        NativeMethods.RegisterClassEx(ref wndClass);

        _hFontTitle = NativeMethods.CreateFont(-16, 0, 0, 0, 700, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe UI");
        _hFontBold = NativeMethods.CreateFont(-12, 0, 0, 0, 700, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe UI");
        _hFontNormal = NativeMethods.CreateFont(-12, 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe UI");

        _hBrushHeader = NativeMethods.CreateSolidBrush(0x00FFFFFF); // White
        _hBrushBody = NativeMethods.GetStockObject(NativeMethods.COLOR_BTNFACE);

        var screenW = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXSCREEN);
        var screenH = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYSCREEN);
        var winW = 520;
        var winH = 380;
        var posX = (screenW - winW) / 2;
        var posY = (screenH - winH) / 2;

        _hWnd = NativeMethods.CreateWindowEx(
            0,
            className,
            "CKAN UI 卸载向导",
            NativeMethods.WS_OVERLAPPED | NativeMethods.WS_CAPTION | NativeMethods.WS_SYSMENU | NativeMethods.WS_MINIMIZEBOX,
            posX,
            posY,
            winW,
            winH,
            IntPtr.Zero,
            IntPtr.Zero,
            hInstance,
            IntPtr.Zero);

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
        if (_hFontTitle != IntPtr.Zero) NativeMethods.DeleteObject(_hFontTitle);
        if (_hFontBold != IntPtr.Zero) NativeMethods.DeleteObject(_hFontBold);
        if (_hFontNormal != IntPtr.Zero) NativeMethods.DeleteObject(_hFontNormal);
        if (_hBrushHeader != IntPtr.Zero) NativeMethods.DeleteObject(_hBrushHeader);
    }

    private void CreateControls()
    {
        // Bottom separator
        CreateWindowControl("STATIC", "", NativeMethods.WS_CHILD | NativeMethods.WS_VISIBLE | NativeMethods.SS_ETCHEDHORZ, 0, 290, 520, 2, 0);

        // Buttons
        _hBtnUninstall = CreateWindowControl("BUTTON", "卸载 (&Uninstall)", NativeMethods.WS_CHILD | NativeMethods.WS_VISIBLE | NativeMethods.BS_DEFPUSHBUTTON | NativeMethods.WS_TABSTOP, 310, 302, 95, 28, ID_BTN_UNINSTALL);
        _hBtnCancel = CreateWindowControl("BUTTON", "取消", NativeMethods.WS_CHILD | NativeMethods.WS_VISIBLE | NativeMethods.BS_PUSHBUTTON | NativeMethods.WS_TABSTOP, 415, 302, 80, 28, ID_BTN_CANCEL);
        _hBtnFinish = CreateWindowControl("BUTTON", "完成 (&Finish)", NativeMethods.WS_CHILD | NativeMethods.BS_DEFPUSHBUTTON | NativeMethods.WS_TABSTOP, 415, 302, 80, 28, ID_BTN_FINISH);

        SetFont(_hBtnUninstall, _hFontNormal);
        SetFont(_hBtnCancel, _hFontNormal);
        SetFont(_hBtnFinish, _hFontNormal);

        // Step 0: Confirm Controls
        var c1 = CreateWindowControl("STATIC",
            "是否确定从本地计算机完全卸载 CKAN UI？\r\n\r\n" +
            "目标卸载目录: " + _installDir + "\r\n\r\n" +
            "注意：卸载程序将删除该目录下的 CKAN UI 核心程序与快捷方式。\r\n" +
            "默认保留您的 KSP 游戏实例、已安装模组及共享缓存。",
            NativeMethods.WS_CHILD | NativeMethods.SS_LEFT, 30, 80, 460, 120, 0);
        _hChkUserData = CreateWindowControl("BUTTON", "同时删除用户数据（CKAN UI 日志与自有配置）", NativeMethods.WS_CHILD | NativeMethods.BS_AUTOCHECKBOX | NativeMethods.WS_TABSTOP, 30, 220, 460, 22, ID_CHK_REMOVE_USERDATA);

        SetFont(c1, _hFontNormal);
        SetFont(_hChkUserData, _hFontNormal);
        _step0Controls.AddRange([c1, _hChkUserData]);

        // Step 1: Progress Controls
        _hStaticStatus = CreateWindowControl("STATIC", "正在卸载 CKAN UI...", NativeMethods.WS_CHILD | NativeMethods.SS_LEFT, 30, 100, 460, 22, ID_STATIC_STATUS);
        _hProgress = CreateWindowControl("msctls_progress32", "", NativeMethods.WS_CHILD | NativeMethods.WS_BORDER, 30, 135, 455, 22, ID_PROGRESS);
        NativeMethods.SendMessage(_hProgress, NativeMethods.PBM_SETRANGE32, IntPtr.Zero, (IntPtr)100);

        SetFont(_hStaticStatus, _hFontBold);
        _step1Controls.AddRange([_hStaticStatus, _hProgress]);

        // Step 2: Finished Controls
        var f1 = CreateWindowControl("STATIC",
            "CKAN UI 已成功从计算机中卸载。\r\n\r\n" +
            "快捷方式、系统注册项及程序文件均已清除。\r\n\r\n" +
            "感谢您使用 CKAN UI！",
            NativeMethods.WS_CHILD | NativeMethods.SS_LEFT, 30, 90, 460, 120, 0);
        SetFont(f1, _hFontNormal);
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

            case NativeMethods.WM_PAINT:
            {
                var hdc = NativeMethods.BeginPaint(hWnd, out var ps);
                inst.DrawHeader(hdc);
                NativeMethods.EndPaint(hWnd, ref ps);
                return IntPtr.Zero;
            }

            case NativeMethods.WM_CTLCOLORSTATIC:
            {
                var hdc = wParam;
                NativeMethods.SetBkMode(hdc, NativeMethods.TRANSPARENT);
                return inst._hBrushBody;
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

    private void DrawHeader(IntPtr hdc)
    {
        var headerRect = new NativeMethods.RECT { Left = 0, Top = 0, Right = 520, Bottom = 65 };
        NativeMethods.FillRect(hdc, ref headerRect, _hBrushHeader);

        var lineRect = new NativeMethods.RECT { Left = 0, Top = 64, Right = 520, Bottom = 65 };
        var hbrLine = NativeMethods.CreateSolidBrush(0x00D0D0D0);
        NativeMethods.FillRect(hdc, ref lineRect, hbrLine);
        NativeMethods.DeleteObject(hbrLine);

        NativeMethods.SetBkMode(hdc, NativeMethods.TRANSPARENT);

        var (title, subtitle) = _currentStep switch
        {
            0 => ("卸载 CKAN UI", "从本地计算机完全移除 CKAN UI。"),
            1 => ("正在卸载 CKAN UI", "向导正在清理文件与系统注册项，请稍候..."),
            2 => ("卸载完成", "CKAN UI 已成功从计算机移除。"),
            _ => ("CKAN UI 卸载向导", "")
        };

        NativeMethods.SelectObject(hdc, _hFontTitle);
        NativeMethods.SetTextColor(hdc, 0x00202020);
        TextOut(hdc, 24, 12, title);

        NativeMethods.SelectObject(hdc, _hFontNormal);
        NativeMethods.SetTextColor(hdc, 0x00555555);
        TextOut(hdc, 36, 36, subtitle);
    }

    private static void TextOut(IntPtr hdc, int x, int y, string str)
    {
        NativeMethods.TextOut(hdc, x, y, str, str.Length);
    }
}
