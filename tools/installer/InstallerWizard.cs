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

    private const int WindowWidth = 560;
    private const int WindowHeight = 440;
    private const int HeaderHeight = 72;
    private const int FooterTop = 372;

    private const int ID_PROGRESS = 3001;
    private const int ID_STATIC_STATUS = 3002;
    private const int ID_STATIC_DETAIL = 3003;
    private const int ID_STATIC_SPACE_REQ = 3004;
    private const int ID_STATIC_SPACE_AVAIL = 3005;

    private static InstallerWizard? _current;

    private readonly InstallerOptions _options;
    private IntPtr _hWnd;
    private IntPtr _hFontTitle;

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
    private InstallerOptions? _installOptions;
    private IntPtr _hStaticFinished;

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
        InstallerTheme.Init();

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
            // 背景由 WM_ERASEBKGND 用主题画布色填充，不走系统刷子。
            hbrBackground = IntPtr.Zero,
            lpszMenuName = null,
            lpszClassName = className,
            hIconSm = IntPtr.Zero
        };

        NativeMethods.RegisterClassEx(ref wndClass);

        _hFontTitle = InstallerTheme.FontTitle;

        var screenW = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXSCREEN);
        var screenH = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYSCREEN);
        // 窗口尺寸按“客户区”算：直接拿 440 当窗口高会被标题栏吃掉 30 多像素，页脚按钮就被裁掉。
        var style = NativeMethods.WS_OVERLAPPED | NativeMethods.WS_CAPTION | NativeMethods.WS_SYSMENU
            | NativeMethods.WS_MINIMIZEBOX | NativeMethods.WS_CLIPCHILDREN;
        var frame = new NativeMethods.RECT { Left = 0, Top = 0, Right = WindowWidth, Bottom = WindowHeight };
        NativeMethods.AdjustWindowRectEx(ref frame, style, false, 0);
        var winW = frame.Right - frame.Left;
        var winH = frame.Bottom - frame.Top;
        var posX = (screenW - winW) / 2;
        var posY = (screenH - winH) / 2;

        _targetDir = _options.InstallDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CKAN UI");

        _hWnd = NativeMethods.CreateWindowEx(
            0,
            className,
            "CKAN UI 安装程序",
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
        // 字体/画刷归 InstallerTheme 所有，这里统一释放，避免重复 DeleteObject。
        InstallerTheme.Dispose();
    }

    private void CreateWizardControls()
    {
        // 页脚按钮：主操作(下一步/安装)=强调色实心，其余=卡片底 + 发丝边。全部自绘，
        // 否则 Win32 原生按钮会是浅色 Aero 风，跟主程序的 Fluent 深色对不上。
        var btnStyle = NativeMethods.WS_CHILD | NativeMethods.WS_VISIBLE | NativeMethods.BS_OWNERDRAW | NativeMethods.WS_TABSTOP;

        _hBtnCancel = CreateWindowControl("BUTTON", "取消", btnStyle, 24, 392, 84, 32, ID_BTN_CANCEL);
        _hBtnBack = CreateWindowControl("BUTTON", "上一步", btnStyle, 300, 392, 92, 32, ID_BTN_BACK);
        _hBtnNext = CreateWindowControl("BUTTON", "下一步", btnStyle, 404, 392, 132, 32, ID_BTN_NEXT);

        InstallerTheme.StyleButton(_hBtnCancel, primary: false);
        InstallerTheme.StyleButton(_hBtnBack, primary: false);
        InstallerTheme.StyleButton(_hBtnNext, primary: true);

        // --- 第 0 步：欢迎 ---
        var w1 = CreateWindowControl("STATIC",
            "CKAN UI 是专为 Kerbal Space Program (KSP) 打造的下一代现代化模组管理器。\r\n\r\n" +
            "  • Fluent 现代界面，深色/浅色主题与平滑动效\r\n" +
            "  • 内置 CKAN 模组检索、依赖解析与版本兼容检查\r\n" +
            "  • 简体中文 / English 双语与离线归档\r\n" +
            "  • 单文件绿色、无外部运行时依赖\r\n\r\n" +
            "继续前建议关闭正在运行的 CKAN 或 KSP。\r\n\r\n" +
            "点击「下一步」继续，或点击「取消」退出向导。",
            NativeMethods.WS_CHILD | NativeMethods.SS_LEFT, 24, 96, 512, 258, 0);
        SetFont(w1, InstallerTheme.FontBody);
        _step0Controls.Add(w1);

        // --- 第 1 步：目录 ---
        var d1 = CreateWindowControl("STATIC", "程序将安装到以下文件夹。若要选择其他文件夹，请点击「浏览」。",
            NativeMethods.WS_CHILD | NativeMethods.SS_LEFT, 24, 92, 512, 20, 0);
        var d2 = CreateWindowControl("STATIC", "目标文件夹",
            NativeMethods.WS_CHILD | NativeMethods.SS_LEFT, 24, 126, 512, 18, 0);
        _hEditPath = CreateWindowControl("EDIT", _targetDir,
            NativeMethods.WS_CHILD | NativeMethods.WS_BORDER | NativeMethods.ES_AUTOHSCROLL | NativeMethods.WS_TABSTOP,
            24, 146, 412, 30, ID_EDIT_PATH);
        _hBtnBrowse = CreateWindowControl("BUTTON", "浏览…",
            btnStyle, 446, 146, 90, 30, ID_BTN_BROWSE);

        _hStaticSpaceReq = CreateWindowControl("STATIC", "所需磁盘空间: 约 110 MB",
            NativeMethods.WS_CHILD | NativeMethods.SS_LEFT, 24, 200, 512, 18, ID_STATIC_SPACE_REQ);
        _hStaticSpaceAvail = CreateWindowControl("STATIC", "可用磁盘空间: 计算中...",
            NativeMethods.WS_CHILD | NativeMethods.SS_LEFT, 24, 222, 512, 18, ID_STATIC_SPACE_AVAIL);

        SetFont(d1, InstallerTheme.FontBody);
        SetFont(d2, InstallerTheme.FontBody);
        SetFont(_hStaticSpaceReq, InstallerTheme.FontSmall);
        SetFont(_hStaticSpaceAvail, InstallerTheme.FontSmall);

        InstallerTheme.StyleEdit(_hEditPath);
        InstallerTheme.StyleButton(_hBtnBrowse, primary: false);

        _step1Controls.AddRange([d1, d2, _hEditPath, _hBtnBrowse, _hStaticSpaceReq, _hStaticSpaceAvail]);

        // --- 第 2 步：选项 ---
        var o1 = CreateWindowControl("STATIC", "选择安装过程中要创建的快捷方式与系统集成项：",
            NativeMethods.WS_CHILD | NativeMethods.SS_LEFT, 24, 92, 512, 20, 0);
        _hChkDesktop = CreateWindowControl("BUTTON", "在桌面创建快捷方式",
            NativeMethods.WS_CHILD | NativeMethods.BS_AUTOCHECKBOX | NativeMethods.WS_TABSTOP, 24, 128, 500, 24, ID_CHK_DESKTOP);
        _hChkStartMenu = CreateWindowControl("BUTTON", "在「开始」菜单创建快捷方式",
            NativeMethods.WS_CHILD | NativeMethods.BS_AUTOCHECKBOX | NativeMethods.WS_TABSTOP, 24, 160, 500, 24, ID_CHK_STARTMENU);
        _hChkAppPath = CreateWindowControl("BUTTON", "注册到 App Paths（在「运行」或 CMD 中直接输入 CkanUI 启动）",
            NativeMethods.WS_CHILD | NativeMethods.BS_AUTOCHECKBOX | NativeMethods.WS_TABSTOP, 24, 192, 500, 24, ID_CHK_APPPATH);

        NativeMethods.SendMessage(_hChkDesktop, NativeMethods.BM_SETCHECK, NativeMethods.BST_CHECKED, IntPtr.Zero);
        NativeMethods.SendMessage(_hChkStartMenu, NativeMethods.BM_SETCHECK, NativeMethods.BST_CHECKED, IntPtr.Zero);
        NativeMethods.SendMessage(_hChkAppPath, NativeMethods.BM_SETCHECK, NativeMethods.BST_CHECKED, IntPtr.Zero);

        SetFont(o1, InstallerTheme.FontBody);
        InstallerTheme.StyleCheckBox(_hChkDesktop);
        InstallerTheme.StyleCheckBox(_hChkStartMenu);
        InstallerTheme.StyleCheckBox(_hChkAppPath);

        _step2Controls.AddRange([o1, _hChkDesktop, _hChkStartMenu, _hChkAppPath]);

        // --- 第 3 步：安装中 ---
        _hStaticStatus = CreateWindowControl("STATIC", "准备开始安装...",
            NativeMethods.WS_CHILD | NativeMethods.SS_LEFT, 24, 108, 512, 22, ID_STATIC_STATUS);
        _hProgress = CreateWindowControl("msctls_progress32", "",
            NativeMethods.WS_CHILD, 24, 140, 512, 10, ID_PROGRESS);
        _hStaticDetail = CreateWindowControl("STATIC", "",
            NativeMethods.WS_CHILD | NativeMethods.SS_LEFT, 24, 166, 512, 60, ID_STATIC_DETAIL);

        NativeMethods.SendMessage(_hProgress, NativeMethods.PBM_SETRANGE32, IntPtr.Zero, (IntPtr)100);
        InstallerTheme.StyleProgress(_hProgress);

        SetFont(_hStaticStatus, InstallerTheme.FontStrong);
        SetFont(_hStaticDetail, InstallerTheme.FontSmall);

        _step3Controls.AddRange([_hStaticStatus, _hProgress, _hStaticDetail]);

        // --- 第 4 步：完成 ---
        var f1 = CreateWindowControl("STATIC",
            "向导已完成 CKAN UI 的安装与桌面环境配置。\r\n\r\n" +
            "安装位置: " + _targetDir + "\r\n\r\n" +
            "开始菜单与桌面快捷方式已创建（若你在上一步取消了勾选，则跳过）。\r\n" +
            "以后需要卸载时，可在 Windows「设置 → 应用」中卸载，\r\n" +
            "也可运行安装目录下的 CkanUISetup.exe -Uninstall。\r\n\r\n" +
            "感谢使用 CKAN UI！点击「完成」退出。",
            NativeMethods.WS_CHILD | NativeMethods.SS_LEFT, 24, 96, 512, 238, 0);
        _hChkLaunch = CreateWindowControl("BUTTON", "安装完成后立即启动 CKAN UI",
            NativeMethods.WS_CHILD | NativeMethods.BS_AUTOCHECKBOX | NativeMethods.WS_TABSTOP, 24, 340, 400, 24, ID_CHK_LAUNCH);
        NativeMethods.SendMessage(_hChkLaunch, NativeMethods.BM_SETCHECK, NativeMethods.BST_CHECKED, IntPtr.Zero);

        SetFont(f1, InstallerTheme.FontBody);
        InstallerTheme.StyleCheckBox(_hChkLaunch);

        _hStaticFinished = f1;

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
                NativeMethods.SetWindowText(_hBtnNext, "下一步");
                NativeMethods.EnableWindow(_hBtnCancel, true);
                break;

            case 1:
                NativeMethods.EnableWindow(_hBtnBack, true);
                NativeMethods.EnableWindow(_hBtnNext, true);
                NativeMethods.SetWindowText(_hBtnNext, "下一步");
                NativeMethods.EnableWindow(_hBtnCancel, true);
                UpdateSpaceInfo();
                break;

            case 2:
                NativeMethods.EnableWindow(_hBtnBack, true);
                NativeMethods.EnableWindow(_hBtnNext, true);
                NativeMethods.SetWindowText(_hBtnNext, "安装");
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
                NativeMethods.SetWindowText(_hBtnNext, "完成");
                break;
        }

        // 标题/步骤指示器跟着步骤走，必须整窗重绘（以前这里是对屏幕 DC 刷了个空刷子，等于没画）。
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

    private static string GetFinishedText(InstallerWizard wizard)
    {
        // 有降级告警时不能再写"已创建"——那句话会变成假话，用户的开始菜单里可能空空如也。
        var hasWarnings = wizard._installOptions?.Warnings is { Count: > 0 };
        var shortcutLine = hasWarnings
            ? "快捷方式或系统集成项存在未完成项，详见下方提示。\r\n"
            : "开始菜单与桌面快捷方式已按你的选择创建。\r\n";

        return "向导已完成 CKAN UI 的安装与桌面环境配置。\r\n\r\n" +
               "安装位置: " + wizard._targetDir + "\r\n\r\n" +
               shortcutLine +
               "以后需要卸载时，可在 Windows「设置 → 应用」中卸载，\r\n" +
               "也可运行安装目录下的 CkanUISetup.exe -Uninstall。" +
               (hasWarnings ? "" : "\r\n\r\n感谢使用 CKAN UI！点击「完成」退出。");
    }

    private void StartInstallation()
    {
        var opt = new InstallerOptions
        {
            InstallDir = _targetDir,
            // 逐项传：以前只传 NoShortcuts，导致只勾一个目标时两个快捷方式都会被建出来。
            NoDesktopShortcut = !_createDesktop,
            NoStartMenuShortcut = !_createStartMenu,
            NoAppPath = !_registerAppPath,
            NoRegistry = _options.NoRegistry,
            ShortcutRoot = _options.ShortcutRoot,
            // 关掉引擎自带的"装完就启动"：向导自己会在「完成」时按勾选启动，
            // 两边都启动会弹两次 SmartScreen，还会把进度页卡在 100% 等它返回。
            NoLaunch = true,
            Quiet = true, // 核心逻辑执行静默操作
            SourceDir = _options.SourceDir
        };

        // 降级失败的提示要能回到完成页，否则"装好了但开始菜单没图标"这种事用户永远查不出来。
        _installOptions = opt;

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

                    var warnings = inst._installOptions?.Warnings;
                    if (warnings is { Count: > 0 } && inst._hStaticFinished != IntPtr.Zero)
                    {
                        var extra = new System.Text.StringBuilder();
                        extra.Append("\r\n\r\n注意：部分系统集成项未能完成（程序本身已可用）：\r\n");
                        foreach (var w in warnings.Take(4))
                        {
                            extra.Append("  · ").Append(w).Append("\r\n");
                        }

                        if (warnings.Count > 4)
                        {
                            extra.Append("  · 另有 ").Append(warnings.Count - 4).Append(" 项，详见安装日志\r\n");
                        }

                        NativeMethods.SetWindowText(inst._hStaticFinished, GetFinishedText(inst) + extra);
                    }
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
                // 自绘按钮：主操作强调色实心，其余卡片底 + 发丝边。
                var dis = Marshal.PtrToStructure<NativeMethods.DRAWITEMSTRUCT>(lParam);
                InstallerTheme.DrawButton(in dis, dis.hwndItem == inst._hBtnNext);
                return (IntPtr)1;
            }

            case NativeMethods.WM_CTLCOLORSTATIC:
            case NativeMethods.WM_CTLCOLORBTN:
            {
                var hdc = wParam;
                var control = lParam;
                NativeMethods.SetBkMode(hdc, NativeMethods.TRANSPARENT);
                var secondary = control == inst._hStaticDetail
                    || control == inst._hStaticSpaceAvail
                    || control == inst._hStaticSpaceReq;
                NativeMethods.SetTextColor(hdc, secondary ? InstallerTheme.TextSecondary : InstallerTheme.TextPrimary);
                return InstallerTheme.BrushCanvas;
            }

            case NativeMethods.WM_CTLCOLOREDIT:
            {
                // 输入框是"凹陷"的：比画布更暗的底，配合 DarkMode_CFD 的边框。
                var hdc = wParam;
                NativeMethods.SetBkMode(hdc, NativeMethods.OPAQUE);
                NativeMethods.SetBkColor(hdc, InstallerTheme.Recessed);
                NativeMethods.SetTextColor(hdc, InstallerTheme.TextPrimary);
                return InstallerTheme.BrushRecessed;
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

        // 顶部 3px 强调色：主程序窗口的同一处也有这条线，视觉锚点对齐。
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
            0 => ("欢迎使用 CKAN UI", "此向导将指引你在本机安装 CKAN UI。"),
            1 => ("选择安装位置", "CKAN UI 的核心文件与资源将存放在这里。"),
            2 => ("选择附加选项", "自定义快捷方式与 Windows 集成。"),
            3 => ("正在安装", "正在解压程序文件并配置桌面环境，请稍候。"),
            4 => ("安装完成", "CKAN UI 已安装到你的计算机。"),
            _ => ("CKAN UI 安装程序", "")
        };

        NativeMethods.SetBkMode(hdc, NativeMethods.TRANSPARENT);

        NativeMethods.SelectObject(hdc, _hFontTitle);
        NativeMethods.SetTextColor(hdc, InstallerTheme.TextPrimary);
        TextOut(hdc, 24, 20, title);

        NativeMethods.SelectObject(hdc, InstallerTheme.FontSubtitle);
        NativeMethods.SetTextColor(hdc, InstallerTheme.TextSecondary);
        TextOut(hdc, 24, 48, subtitle);

        if (_currentStep <= 2)
        {
            DrawStepDots(hdc);
        }
    }

    /// <summary>右上角三步指示器：当前步强调色，其余为抬升面。</summary>
    private void DrawStepDots(IntPtr hdc)
    {
        const int dot = 8;
        const int gap = 10;
        const int steps = 3;

        var x = WindowWidth - 24 - (steps * dot + (steps - 1) * gap);
        const int y = 32;

        for (var i = 0; i < steps; i++)
        {
            var active = i == _currentStep;
            var brush = NativeMethods.CreateSolidBrush(active ? InstallerTheme.Accent : InstallerTheme.Elevated);
            var pen = NativeMethods.CreatePen(NativeMethods.PS_SOLID, 1, active ? InstallerTheme.Accent : InstallerTheme.Border);
            var oldBrush = NativeMethods.SelectObject(hdc, brush);
            var oldPen = NativeMethods.SelectObject(hdc, pen);

            NativeMethods.RoundRect(hdc, x, y, x + dot, y + dot, dot, dot);

            NativeMethods.SelectObject(hdc, oldBrush);
            NativeMethods.SelectObject(hdc, oldPen);
            NativeMethods.DeleteObject(brush);
            NativeMethods.DeleteObject(pen);

            x += dot + gap;
        }
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
