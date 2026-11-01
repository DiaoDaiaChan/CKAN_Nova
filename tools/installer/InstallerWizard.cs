using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace CkanUI.Tools.Installer;

internal sealed class InstallerWizard
{
    private const int ID_BTN_BACK = 1001;
    private const int ID_BTN_NEXT = 1002;
    private const int ID_BTN_CANCEL = 1003;
    private const int ID_BTN_BROWSE = 1004;

    private const int ID_EDIT_PATH = 2001;
    private const int ID_CHK_DESKTOP = 2002;
    private const int ID_CHK_STARTMENU = 2003;
    private const int ID_CHK_APPPATH = 2004;
    private const int ID_CHK_LAUNCH = 2005;

    private const int ID_PROGRESS = 3001;
    private const int ID_STATIC_STATUS = 3002;
    private const int ID_STATIC_DETAIL = 3003;
    private const int ID_STATIC_SPACE_REQ = 3004;
    private const int ID_STATIC_SPACE_AVAIL = 3005;

    private static InstallerWizard? _current;

    private readonly InstallerOptions _options;
    private IntPtr _hWnd;
    private IntPtr _hFontTitle;
    private IntPtr _hFontNormal;
    private IntPtr _hFontBold;
    private IntPtr _hBrushHeader;
    private IntPtr _hBrushBody;

    private int _currentStep; // 0: Welcome, 1: Directory, 2: Options, 3: Installing, 4: Finished

    // Controls
    private IntPtr _hBtnBack;
    private IntPtr _hBtnNext;
    private IntPtr _hBtnCancel;
    private IntPtr _hBtnBrowse;

    private IntPtr _hEditPath;
    private IntPtr _hChkDesktop;
    private IntPtr _hChkStartMenu;
    private IntPtr _hChkAppPath;
    private IntPtr _hChkLaunch;

    private IntPtr _hProgress;
    private IntPtr _hStaticStatus;
    private IntPtr _hStaticDetail;
    private IntPtr _hStaticSpaceReq;
    private IntPtr _hStaticSpaceAvail;

    private readonly List<IntPtr> _step0Controls = new();
    private readonly List<IntPtr> _step1Controls = new();
    private readonly List<IntPtr> _step2Controls = new();
    private readonly List<IntPtr> _step3Controls = new();
    private readonly List<IntPtr> _step4Controls = new();

    private string _targetDir = string.Empty;
    private bool _createDesktop = true;
    private bool _createStartMenu = true;
    private bool _registerAppPath = true;
    private bool _launchOnFinish = true;

    private string _installedVersion = "0.1.0";
    private string _installedAppExe = string.Empty;
    private int _exitCode;
    private string? _installError;

    public InstallerWizard(InstallerOptions options)
    {
        _options = options;
    }

    public static int Run(InstallerOptions options)
    {
        var icc = new NativeMethods.INITCOMMONCONTROLSEX
        {
            dwSize = Marshal.SizeOf<NativeMethods.INITCOMMONCONTROLSEX>(),
            dwICC = NativeMethods.ICC_PROGRESS_CLASS | NativeMethods.ICC_STANDARD_CLASSES
        };
        NativeMethods.InitCommonControlsEx(ref icc);

        var wizard = new InstallerWizard(options);
        _current = wizard;
        return wizard.Show();
    }

    private unsafe int Show()
    {
        var hInstance = NativeMethods.GetModuleHandle(null);
        var className = "CkanUI_Installer_Wizard";

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
        var winW = 550;
        var winH = 430;
        var posX = (screenW - winW) / 2;
        var posY = (screenH - winH) / 2;

        _targetDir = _options.InstallDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CKAN UI");

        _hWnd = NativeMethods.CreateWindowEx(
            0,
            className,
            "CKAN UI 安装程序",
            NativeMethods.WS_OVERLAPPED | NativeMethods.WS_CAPTION | NativeMethods.WS_SYSMENU | NativeMethods.WS_MINIMIZEBOX,
            posX,
            posY,
            winW,
            winH,
            IntPtr.Zero,
            IntPtr.Zero,
            hInstance,
            IntPtr.Zero);

        CreateWizardControls();
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

    private void CreateWizardControls()
    {
        var hInst = NativeMethods.GetModuleHandle(null);

        // Bottom separator
        CreateWindowControl("STATIC", "", NativeMethods.WS_CHILD | NativeMethods.WS_VISIBLE | NativeMethods.SS_ETCHEDHORZ, 0, 335, 550, 2, 0);

        // Bottom Buttons
        _hBtnBack = CreateWindowControl("BUTTON", "< 上一步", NativeMethods.WS_CHILD | NativeMethods.WS_VISIBLE | NativeMethods.BS_PUSHBUTTON | NativeMethods.WS_TABSTOP, 275, 348, 80, 28, ID_BTN_BACK);
        _hBtnNext = CreateWindowControl("BUTTON", "下一步 >", NativeMethods.WS_CHILD | NativeMethods.WS_VISIBLE | NativeMethods.BS_DEFPUSHBUTTON | NativeMethods.WS_TABSTOP, 360, 348, 80, 28, ID_BTN_NEXT);
        _hBtnCancel = CreateWindowControl("BUTTON", "取消", NativeMethods.WS_CHILD | NativeMethods.WS_VISIBLE | NativeMethods.BS_PUSHBUTTON | NativeMethods.WS_TABSTOP, 445, 348, 80, 28, ID_BTN_CANCEL);

        SetFont(_hBtnBack, _hFontNormal);
        SetFont(_hBtnNext, _hFontNormal);
        SetFont(_hBtnCancel, _hFontNormal);

        // --- Step 0: Welcome Controls ---
        var w1 = CreateWindowControl("STATIC",
            "欢迎使用 CKAN UI 安装向导！\r\n\r\n" +
            "CKAN UI 是专为 Kerbal Space Program (KSP) 打造的下一代现代化模组管理器。\r\n\r\n" +
            "  • 全新 Fluent UI 现代设计，支持深色/浅色主题与平滑动效\r\n" +
            "  • 内置完整 CKAN 模组检索、自动依赖解析与版本兼容检查\r\n" +
            "  • 原生双语言支持（简体中文 / English）与离线归档能力\r\n" +
            "  • 单文件绿色免外部运行时依赖，快速无感冷启动\r\n\r\n" +
            "推荐在继续前关闭正在运行的 CKAN 或 KSP 相关程序。\r\n\r\n" +
            "点击「下一步」继续，或点击「取消」退出安装向导。",
            NativeMethods.WS_CHILD | NativeMethods.SS_LEFT, 30, 80, 480, 240, 0);
        SetFont(w1, _hFontNormal);
        _step0Controls.Add(w1);

        // --- Step 1: Directory Selection Controls ---
        var d1 = CreateWindowControl("STATIC", "程序将安装到以下文件夹。若要选择其他文件夹，请点击「浏览」。", NativeMethods.WS_CHILD | NativeMethods.SS_LEFT, 30, 80, 480, 20, 0);
        var d2 = CreateWindowControl("STATIC", "目标文件夹(&D)：", NativeMethods.WS_CHILD | NativeMethods.SS_LEFT, 30, 115, 480, 18, 0);
        _hEditPath = CreateWindowControl("EDIT", _targetDir, NativeMethods.WS_CHILD | NativeMethods.WS_BORDER | NativeMethods.ES_AUTOHSCROLL | NativeMethods.WS_TABSTOP, 30, 135, 380, 24, ID_EDIT_PATH);
        _hBtnBrowse = CreateWindowControl("BUTTON", "浏览(&B)...", NativeMethods.WS_CHILD | NativeMethods.BS_PUSHBUTTON | NativeMethods.WS_TABSTOP, 420, 134, 85, 26, ID_BTN_BROWSE);

        _hStaticSpaceReq = CreateWindowControl("STATIC", "所需磁盘空间: 约 110 MB", NativeMethods.WS_CHILD | NativeMethods.SS_LEFT, 30, 190, 480, 18, ID_STATIC_SPACE_REQ);
        _hStaticSpaceAvail = CreateWindowControl("STATIC", "可用磁盘空间: 计算中...", NativeMethods.WS_CHILD | NativeMethods.SS_LEFT, 30, 215, 480, 18, ID_STATIC_SPACE_AVAIL);

        SetFont(d1, _hFontNormal);
        SetFont(d2, _hFontNormal);
        SetFont(_hEditPath, _hFontNormal);
        SetFont(_hBtnBrowse, _hFontNormal);
        SetFont(_hStaticSpaceReq, _hFontNormal);
        SetFont(_hStaticSpaceAvail, _hFontNormal);

        _step1Controls.AddRange([d1, d2, _hEditPath, _hBtnBrowse, _hStaticSpaceReq, _hStaticSpaceAvail]);

        // --- Step 2: Options Controls ---
        var o1 = CreateWindowControl("STATIC", "选择您希望在安装过程中创建的快捷方式与系统配置：", NativeMethods.WS_CHILD | NativeMethods.SS_LEFT, 30, 80, 480, 20, 0);
        _hChkDesktop = CreateWindowControl("BUTTON", "创建桌面快捷方式 (&Desktop)", NativeMethods.WS_CHILD | NativeMethods.BS_AUTOCHECKBOX | NativeMethods.WS_TABSTOP, 30, 115, 400, 22, ID_CHK_DESKTOP);
        _hChkStartMenu = CreateWindowControl("BUTTON", "创建「开始」菜单快捷方式 (&Start Menu)", NativeMethods.WS_CHILD | NativeMethods.BS_AUTOCHECKBOX | NativeMethods.WS_TABSTOP, 30, 145, 400, 22, ID_CHK_STARTMENU);
        _hChkAppPath = CreateWindowControl("BUTTON", "注册至 Windows App Paths (运行/CMD中输入 CkanUI 启动)", NativeMethods.WS_CHILD | NativeMethods.BS_AUTOCHECKBOX | NativeMethods.WS_TABSTOP, 30, 175, 450, 22, ID_CHK_APPPATH);

        NativeMethods.SendMessage(_hChkDesktop, NativeMethods.BM_SETCHECK, NativeMethods.BST_CHECKED, IntPtr.Zero);
        NativeMethods.SendMessage(_hChkStartMenu, NativeMethods.BM_SETCHECK, NativeMethods.BST_CHECKED, IntPtr.Zero);
        NativeMethods.SendMessage(_hChkAppPath, NativeMethods.BM_SETCHECK, NativeMethods.BST_CHECKED, IntPtr.Zero);

        SetFont(o1, _hFontNormal);
        SetFont(_hChkDesktop, _hFontNormal);
        SetFont(_hChkStartMenu, _hFontNormal);
        SetFont(_hChkAppPath, _hFontNormal);

        _step2Controls.AddRange([o1, _hChkDesktop, _hChkStartMenu, _hChkAppPath]);

        // --- Step 3: Installing Progress Controls ---
        _hStaticStatus = CreateWindowControl("STATIC", "准备开始安装...", NativeMethods.WS_CHILD | NativeMethods.SS_LEFT, 30, 100, 480, 20, ID_STATIC_STATUS);
        _hProgress = CreateWindowControl("msctls_progress32", "", NativeMethods.WS_CHILD | NativeMethods.WS_BORDER, 30, 130, 475, 22, ID_PROGRESS);
        _hStaticDetail = CreateWindowControl("STATIC", "", NativeMethods.WS_CHILD | NativeMethods.SS_LEFT, 30, 160, 480, 40, ID_STATIC_DETAIL);

        NativeMethods.SendMessage(_hProgress, NativeMethods.PBM_SETRANGE32, IntPtr.Zero, (IntPtr)100);

        SetFont(_hStaticStatus, _hFontBold);
        SetFont(_hStaticDetail, _hFontNormal);

        _step3Controls.AddRange([_hStaticStatus, _hProgress, _hStaticDetail]);

        // --- Step 4: Finished Controls ---
        var f1 = CreateWindowControl("STATIC",
            "向导已完成 CKAN UI 的安装与桌面环境配置。\r\n\r\n" +
            "安装位置: " + _targetDir + "\r\n\r\n" +
            "若以后需要卸载，可在 Windows「设置 -> 应用」或控制面板中卸载，\r\n" +
            "也可直接运行安装目录下的 CkanUISetup.exe -Uninstall。\r\n\r\n" +
            "感谢您使用 CKAN UI！点击「完成」退出安装程序。",
            NativeMethods.WS_CHILD | NativeMethods.SS_LEFT, 30, 80, 480, 170, 0);
        _hChkLaunch = CreateWindowControl("BUTTON", "立即启动 CKAN UI (&Launch)", NativeMethods.WS_CHILD | NativeMethods.BS_AUTOCHECKBOX | NativeMethods.WS_TABSTOP, 30, 260, 400, 22, ID_CHK_LAUNCH);
        NativeMethods.SendMessage(_hChkLaunch, NativeMethods.BM_SETCHECK, NativeMethods.BST_CHECKED, IntPtr.Zero);

        SetFont(f1, _hFontNormal);
        SetFont(_hChkLaunch, _hFontNormal);

        _step4Controls.AddRange([f1, _hChkLaunch]);
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
        SetControlsVisible(_step3Controls, _currentStep == 3);
        SetControlsVisible(_step4Controls, _currentStep == 4);

        switch (_currentStep)
        {
            case 0:
                NativeMethods.EnableWindow(_hBtnBack, false);
                NativeMethods.EnableWindow(_hBtnNext, true);
                NativeMethods.SetWindowText(_hBtnNext, "下一步 >");
                NativeMethods.EnableWindow(_hBtnCancel, true);
                break;

            case 1:
                NativeMethods.EnableWindow(_hBtnBack, true);
                NativeMethods.EnableWindow(_hBtnNext, true);
                NativeMethods.SetWindowText(_hBtnNext, "下一步 >");
                NativeMethods.EnableWindow(_hBtnCancel, true);
                UpdateSpaceInfo();
                break;

            case 2:
                NativeMethods.EnableWindow(_hBtnBack, true);
                NativeMethods.EnableWindow(_hBtnNext, true);
                NativeMethods.SetWindowText(_hBtnNext, "安装 (&Install)");
                NativeMethods.EnableWindow(_hBtnCancel, true);
                break;

            case 3:
                NativeMethods.EnableWindow(_hBtnBack, false);
                NativeMethods.EnableWindow(_hBtnNext, false);
                NativeMethods.EnableWindow(_hBtnCancel, false);
                break;

            case 4:
                NativeMethods.ShowWindow(_hBtnBack, NativeMethods.SW_HIDE);
                NativeMethods.ShowWindow(_hBtnCancel, NativeMethods.SW_HIDE);
                NativeMethods.EnableWindow(_hBtnNext, true);
                NativeMethods.SetWindowText(_hBtnNext, "完成 (&Finish)");
                break;
        }

        // Trigger header redraw
        var rect = new NativeMethods.RECT { Left = 0, Top = 0, Right = 550, Bottom = 65 };
        NativeMethods.FillRect(IntPtr.Zero, ref rect, IntPtr.Zero);
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

    private void UpdateSpaceInfo()
    {
        try
        {
            var path = GetEditPath();
            var root = Path.GetPathRoot(path);
            if (!string.IsNullOrEmpty(root) && Directory.Exists(root))
            {
                if (NativeMethods.GetDiskFreeSpaceEx(root, out var freeBytes, out _, out _))
                {
                    var freeGb = freeBytes / (1024.0 * 1024.0 * 1024.0);
                    NativeMethods.SetWindowText(_hStaticSpaceAvail, $"可用磁盘空间: {freeGb:F1} GB (驱动器 {root})");
                    return;
                }
            }
        }
        catch
        {
            // 忽略计算异常
        }

        NativeMethods.SetWindowText(_hStaticSpaceAvail, "可用磁盘空间: 未知");
    }

    private string GetEditPath()
    {
        var len = NativeMethods.GetWindowTextLength(_hEditPath);
        var sb = new StringBuilder(len + 1);
        NativeMethods.GetWindowText(_hEditPath, sb, sb.Capacity);
        return sb.ToString().Trim();
    }

    private void OnBrowse()
    {
        var selected = NativeMethods.BrowseForFolder(_hWnd, "请选择 CKAN UI 安装目录：");
        if (!string.IsNullOrWhiteSpace(selected))
        {
            _targetDir = selected;
            NativeMethods.SetWindowText(_hEditPath, _targetDir);
            UpdateSpaceInfo();
        }
    }

    private void OnNext()
    {
        if (_currentStep == 0)
        {
            _currentStep = 1;
            UpdateStepView();
        }
        else if (_currentStep == 1)
        {
            _targetDir = GetEditPath();
            if (string.IsNullOrWhiteSpace(_targetDir))
            {
                NativeMethods.MessageBox(_hWnd, "请输入有效的安装目录。", "提示", NativeMethods.MB_OK | NativeMethods.MB_ICONWARNING);
                return;
            }

            _currentStep = 2;
            UpdateStepView();
        }
        else if (_currentStep == 2)
        {
            _createDesktop = NativeMethods.SendMessage(_hChkDesktop, NativeMethods.BM_GETCHECK, IntPtr.Zero, IntPtr.Zero) == NativeMethods.BST_CHECKED;
            _createStartMenu = NativeMethods.SendMessage(_hChkStartMenu, NativeMethods.BM_GETCHECK, IntPtr.Zero, IntPtr.Zero) == NativeMethods.BST_CHECKED;
            _registerAppPath = NativeMethods.SendMessage(_hChkAppPath, NativeMethods.BM_GETCHECK, IntPtr.Zero, IntPtr.Zero) == NativeMethods.BST_CHECKED;

            _currentStep = 3;
            UpdateStepView();
            StartInstallation();
        }
        else if (_currentStep == 4)
        {
            _launchOnFinish = NativeMethods.SendMessage(_hChkLaunch, NativeMethods.BM_GETCHECK, IntPtr.Zero, IntPtr.Zero) == NativeMethods.BST_CHECKED;
            if (_launchOnFinish && !string.IsNullOrEmpty(_installedAppExe) && File.Exists(_installedAppExe))
            {
                try
                {
                    Process.Start(new ProcessStartInfo(_installedAppExe)
                    {
                        WorkingDirectory = Path.GetDirectoryName(_installedAppExe),
                        UseShellExecute = true
                    });
                }
                catch
                {
                    // 启动异常忽略
                }
            }

            _exitCode = 0;
            NativeMethods.DestroyWindow(_hWnd);
        }
    }

    private void OnBack()
    {
        if (_currentStep > 0 && _currentStep < 3)
        {
            _currentStep--;
            UpdateStepView();
        }
    }

    private void OnCancel()
    {
        if (_currentStep < 3)
        {
            var res = NativeMethods.MessageBox(_hWnd, "您确定要退出 CKAN UI 安装向导吗？", "退出安装", NativeMethods.MB_YESNO | NativeMethods.MB_ICONQUESTION);
            if (res == NativeMethods.IDYES)
            {
                _exitCode = 1;
                NativeMethods.DestroyWindow(_hWnd);
            }
        }
    }

    private void StartInstallation()
    {
        var opt = new InstallerOptions
        {
            InstallDir = _targetDir,
            NoShortcuts = !_createDesktop && !_createStartMenu,
            Quiet = true, // 核心逻辑执行静默操作
            SourceDir = _options.SourceDir
        };

        Task.Run(() =>
        {
            try
            {
                var code = InstallerEngine.ExecuteInstall(opt, (pct, status) =>
                {
                    NativeMethods.PostMessage(_hWnd, NativeMethods.WM_APP_PROGRESS, (IntPtr)pct, IntPtr.Zero);
                }, out var appExe, out var ver);

                _installedAppExe = appExe;
                _installedVersion = ver;
                _exitCode = code;

                NativeMethods.PostMessage(_hWnd, NativeMethods.WM_APP_COMPLETE, (IntPtr)code, IntPtr.Zero);
            }
            catch (Exception ex)
            {
                _installError = ex.Message;
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
                if (id == ID_BTN_NEXT) inst.OnNext();
                else if (id == ID_BTN_BACK) inst.OnBack();
                else if (id == ID_BTN_CANCEL) inst.OnCancel();
                else if (id == ID_BTN_BROWSE) inst.OnBrowse();
                break;
            }

            case NativeMethods.WM_APP_PROGRESS:
            {
                var pct = (int)wParam;
                NativeMethods.SendMessage(inst._hProgress, NativeMethods.PBM_SETPOS, (IntPtr)pct, IntPtr.Zero);
                NativeMethods.SetWindowText(inst._hStaticStatus, $"正在安装 CKAN UI... ({pct}%)");
                break;
            }

            case NativeMethods.WM_APP_COMPLETE:
            {
                var code = (int)wParam;
                if (code == 0)
                {
                    inst._currentStep = 4;
                    inst.UpdateStepView();
                }
                else
                {
                    var msgText = "安装遇到错误中断，已安全回滚变更。\r\n\r\n" + (inst._installError ?? "未知错误");
                    NativeMethods.MessageBox(hWnd, msgText, "安装失败", NativeMethods.MB_OK | NativeMethods.MB_ICONERROR);
                    inst._exitCode = code;
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
                inst.OnCancel();
                return IntPtr.Zero;

            case NativeMethods.WM_DESTROY:
                NativeMethods.PostQuitMessage(0);
                return IntPtr.Zero;
        }

        return NativeMethods.DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private void DrawHeader(IntPtr hdc)
    {
        // Draw white header rectangle
        var headerRect = new NativeMethods.RECT { Left = 0, Top = 0, Right = 550, Bottom = 65 };
        NativeMethods.FillRect(hdc, ref headerRect, _hBrushHeader);

        // Header separator line
        var lineRect = new NativeMethods.RECT { Left = 0, Top = 64, Right = 550, Bottom = 65 };
        var hbrLine = NativeMethods.CreateSolidBrush(0x00D0D0D0);
        NativeMethods.FillRect(hdc, ref lineRect, hbrLine);
        NativeMethods.DeleteObject(hbrLine);

        NativeMethods.SetBkMode(hdc, NativeMethods.TRANSPARENT);

        var (title, subtitle) = _currentStep switch
        {
            0 => ("欢迎使用 CKAN UI 安装向导", "此向导将指引您在本地计算机上安装 CKAN UI。"),
            1 => ("选择目标安装目录", "请选择 CKAN UI 核心文件与资源的存放位置。"),
            2 => ("选择附加安装选项", "自定义快捷方式创建与 Windows 环境集成设置。"),
            3 => ("正在安装 CKAN UI", "向导正在解压程序文件并登记桌面环境配置，请稍候..."),
            4 => ("安装完成", "CKAN UI 已成功安装至您的计算机。"),
            _ => ("CKAN UI 安装程序", "")
        };

        // Title
        NativeMethods.SelectObject(hdc, _hFontTitle);
        NativeMethods.SetTextColor(hdc, 0x00202020);
        TextOut(hdc, 24, 12, title);

        // Subtitle
        NativeMethods.SelectObject(hdc, _hFontNormal);
        NativeMethods.SetTextColor(hdc, 0x00555555);
        TextOut(hdc, 36, 36, subtitle);
    }

    private static void TextOut(IntPtr hdc, int x, int y, string str)
    {
        NativeMethods.TextOut(hdc, x, y, str, str.Length);
    }
}
