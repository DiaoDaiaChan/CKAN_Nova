using System.Text;

namespace CkanUI.Tools.Installer;

/// <summary>
/// 安装/卸载向导的设计语言，与主程序 src/CkanUI.App/Resources/Tokens.axaml 对齐：
/// 同一套深色表面、同一支强调色、同样的圆角与字号层级。安装程序是用户看到的第一屏，
/// 第一屏就长得像另一个产品，后面的"现代模组管理器"叙事就白讲了。
/// </summary>
internal static class InstallerTheme
{
    // GDI 的 COLORREF 是 0x00BBGGRR，跟 #RRGGBB 的字节序正好相反，写反了就是"蓝色变橙"。
    public const uint Canvas = 0x00140E0B;      // #0B0E14
    public const uint Surface = 0x00191310;     // #101319
    public const uint Card = 0x002B2320;        // #20232B
    public const uint Recessed = 0x00120D0A;    // #0A0D12
    public const uint Elevated = 0x003D312A;    // #2A313D
    public const uint Border = 0x00403129;      // #293140

    public const uint Accent = 0x00FFA772;      // #72A7FF
    public const uint AccentHover = 0x00FFB68A; // #8AB6FF
    public const uint AccentPressed = 0x00F0935B; // #5B93F0
    public const uint AccentSubtle = 0x00482B14;  // #142B48

    public const uint TextPrimary = 0x00F2ECE8;   // #E8ECF2
    public const uint TextSecondary = 0x00B2A49A; // #9AA4B2
    public const uint TextTertiary = 0x0081766E;  // #6E7681
    public const uint OnAccent = 0x0020120B;      // #0B1220
    public const uint Success = 0x007AD04F;       // #4FD07A
    public const uint Danger = 0x005E6BFF;        // #FF6B5E

    private const string DisplayFace = "Segoe UI Variable Display";
    private const string TextFace = "Segoe UI Variable Text";

    private static IntPtr _fontTitle;
    private static IntPtr _fontSubtitle;
    private static IntPtr _fontBody;
    private static IntPtr _fontStrong;
    private static IntPtr _fontSmall;
    private static IntPtr _brCanvas;
    private static IntPtr _brSurface;
    private static IntPtr _brRecessed;
    private static bool _ready;

    public static IntPtr FontTitle => _fontTitle;
    public static IntPtr FontSubtitle => _fontSubtitle;
    public static IntPtr FontBody => _fontBody;
    public static IntPtr FontStrong => _fontStrong;
    public static IntPtr FontSmall => _fontSmall;
    public static IntPtr BrushCanvas => _brCanvas;
    public static IntPtr BrushSurface => _brSurface;
    public static IntPtr BrushRecessed => _brRecessed;

    public static void Init()
    {
        if (_ready)
        {
            return;
        }

        _fontTitle = NativeMethods.CreateFont(-22, 0, 0, 0, 600, 0, 0, 0, 1, 0, 0, 5, 0, DisplayFace);
        _fontSubtitle = NativeMethods.CreateFont(-13, 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 5, 0, TextFace);
        _fontBody = NativeMethods.CreateFont(-14, 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 5, 0, TextFace);
        _fontStrong = NativeMethods.CreateFont(-14, 0, 0, 0, 600, 0, 0, 0, 1, 0, 0, 5, 0, TextFace);
        _fontSmall = NativeMethods.CreateFont(-12, 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 5, 0, TextFace);

        _brCanvas = NativeMethods.CreateSolidBrush(Canvas);
        _brSurface = NativeMethods.CreateSolidBrush(Surface);
        _brRecessed = NativeMethods.CreateSolidBrush(Recessed);

        _ready = true;
    }

    public static void Dispose()
    {
        if (!_ready)
        {
            return;
        }

        foreach (var h in new[] { _fontTitle, _fontSubtitle, _fontBody, _fontStrong, _fontSmall, _brCanvas, _brSurface, _brRecessed })
        {
            if (h != IntPtr.Zero)
            {
                NativeMethods.DeleteObject(h);
            }
        }

        _ready = false;
    }

    /// <summary>深色标题栏 + Win11 圆角，与主程序同一套窗口外观。</summary>
    public static void ApplyChrome(IntPtr hWnd)
    {
        var dark = 1;
        NativeMethods.DwmSetWindowAttribute(hWnd, 20, ref dark, sizeof(int));

        var round = 2; // DWMWCP_ROUND
        NativeMethods.DwmSetWindowAttribute(hWnd, 33, ref round, sizeof(int));

        // Win11 才认这两个：把系统标题栏染成我们的画布色，避免深色窗体配白标题栏的割裂感。
        var caption = Canvas;
        NativeMethods.DwmSetWindowAttribute(hWnd, 35, ref caption, sizeof(uint));

        var captionText = TextPrimary;
        NativeMethods.DwmSetWindowAttribute(hWnd, 36, ref captionText, sizeof(uint));
    }

    public static void StyleProgress(IntPtr hProgress)
    {
        // 先摘掉视觉样式，否则 PBM_SETBARCOLOR 会被主题吞掉。
        NativeMethods.SetWindowTheme(hProgress, "", "");

        NativeMethods.SendMessage(hProgress, NativeMethods.CCM_SETBKCOLOR, IntPtr.Zero, (IntPtr)(int)Recessed);
        NativeMethods.SendMessage(hProgress, NativeMethods.PBM_SETBARCOLOR, IntPtr.Zero, (IntPtr)(int)Accent);
    }

    public static void StyleEdit(IntPtr hEdit)
    {
        NativeMethods.SetWindowTheme(hEdit, "DarkMode_CFD", null);
        NativeMethods.SendMessage(hEdit, NativeMethods.WM_SETFONT, _fontBody, (IntPtr)1);
    }

    public static void StyleCheckBox(IntPtr hCheckBox)
    {
        // DarkMode_Explorer 让复选框勾子跟着深色走；不设就会得到一块刺眼的白底方块。
        NativeMethods.SetWindowTheme(hCheckBox, "DarkMode_Explorer", null);
        NativeMethods.SendMessage(hCheckBox, NativeMethods.WM_SETFONT, _fontBody, (IntPtr)1);
    }

    public static void StyleButton(IntPtr hButton, bool primary)
    {
        NativeMethods.SendMessage(hButton, NativeMethods.WM_SETFONT, primary ? _fontStrong : _fontBody, (IntPtr)1);
    }

    /// <summary>自绘按钮：主按钮是强调色实心，次级按钮是卡片底 + 发丝边。</summary>
    public static void DrawButton(in NativeMethods.DRAWITEMSTRUCT dis, bool primary)
    {
        var hdc = dis.hDC;
        var r = dis.rcItem;

        var disabled = (dis.itemState & NativeMethods.ODS_DISABLED) != 0;
        var pressed = (dis.itemState & NativeMethods.ODS_SELECTED) != 0;

        uint fill;
        uint border;
        uint text;

        if (primary)
        {
            fill = disabled ? Elevated : pressed ? AccentPressed : Accent;
            border = fill;
            text = disabled ? TextTertiary : OnAccent;
        }
        else
        {
            fill = pressed ? Elevated : Card;
            border = Border;
            text = disabled ? TextTertiary : TextPrimary;
        }

        var hbr = NativeMethods.CreateSolidBrush(fill);
        var hpen = NativeMethods.CreatePen(NativeMethods.PS_SOLID, 1, border);
        var oldBr = NativeMethods.SelectObject(hdc, hbr);
        var oldPen = NativeMethods.SelectObject(hdc, hpen);

        NativeMethods.RoundRect(hdc, r.Left, r.Top, r.Right, r.Bottom, 12, 12);

        NativeMethods.SelectObject(hdc, oldBr);
        NativeMethods.SelectObject(hdc, oldPen);
        NativeMethods.DeleteObject(hbr);
        NativeMethods.DeleteObject(hpen);

        // 主按钮的强调色实心底已经足够醒目，再加一圈内描边会变成难看的双层边框，
        // 键盘焦点对它没有额外价值；次级按钮才需要焦点环。
        if (!primary && (dis.itemState & NativeMethods.ODS_FOCUS) != 0 && !disabled)
        {
            var focusPen = NativeMethods.CreatePen(NativeMethods.PS_SOLID, 1, Accent);
            var oldFocus = NativeMethods.SelectObject(hdc, focusPen);
            var oldFocusBr = NativeMethods.SelectObject(hdc, NativeMethods.GetStockObject(NativeMethods.NULL_BRUSH));
            NativeMethods.RoundRect(hdc, r.Left + 2, r.Top + 2, r.Right - 2, r.Bottom - 2, 9, 9);
            NativeMethods.SelectObject(hdc, oldFocusBr);
            NativeMethods.SelectObject(hdc, oldFocus);
            NativeMethods.DeleteObject(focusPen);
        }

        var label = GetText(dis.hwndItem);
        NativeMethods.SetBkMode(hdc, NativeMethods.TRANSPARENT);
        NativeMethods.SetTextColor(hdc, text);
        var oldFont = NativeMethods.SelectObject(hdc, primary ? _fontStrong : _fontBody);

        var textRect = r;
        textRect.Left += 6;
        textRect.Right -= 6;
        NativeMethods.DrawText(hdc, label, label.Length, ref textRect,
            NativeMethods.DT_CENTER | NativeMethods.DT_VCENTER | NativeMethods.DT_SINGLELINE | NativeMethods.DT_NOPREFIX);

        NativeMethods.SelectObject(hdc, oldFont);
    }

    private static string GetText(IntPtr hWnd)
    {
        var length = NativeMethods.GetWindowTextLength(hWnd);
        if (length <= 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder(length + 1);
        NativeMethods.GetWindowText(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }
}
