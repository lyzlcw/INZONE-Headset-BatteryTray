using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace InzoneHeadsetBatteryTray;

public class MainForm
{
    private const uint NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2, NIM_SETVERSION = 4;
    private const uint NIF_MESSAGE = 1, NIF_ICON = 2, NIF_TIP = 4, NIF_INFO = 0x10;
    private const uint NIIF_ERROR = 3, NIIF_INFO = 1;
    private const uint NOTIFYICON_VERSION_4 = 4;
    private const uint WM_CREATE = 1, WM_DESTROY = 2, WM_COMMAND = 0x0111, WM_TIMER = 0x0113;
    private const uint WM_APP_BASE = 0x8000;
    private const uint WM_RBUTTONDOWN = 0x0204, WM_LBUTTONDBLCLK = 0x0203;
    private const uint TPM_RETURNCMD = 0x0100, TPM_RIGHTBUTTON = 2;
    private const uint MF_STRING = 0, MF_SEPARATOR = 0x0800, MF_POPUP = 0x0010, MF_CHECKED = 0x0008;
    private const uint TRAY_ID = 1;
    private const int PollTimerId = 1, BlinkTimerId = 2;
    private const uint IdmRefreshNow = 100, IdmScanComPort = 101, IdmAutoStart = 102;
    private const uint IdmSchemeStart = 200, IdmFreqStart = 300, IdmExit = 999;
    private const uint WS_DISABLED = 0x08000000;
    private const string AutoStartRegKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AutoStartValueName = "InzoneBatteryTray";
    private const uint WmTrayCallback = WM_APP_BASE + 1, WmHeadsetResult = WM_APP_BASE + 2, WmMouseResult = WM_APP_BASE + 3, WmMenuCommand = WM_APP_BASE + 4;

    // GDI constants
    private const int FW_BOLD = 700;
    private const uint DEFAULT_CHARSET = 1, OUT_DEFAULT_PRECIS = 0, CLIP_DEFAULT_PRECIS = 0, PROOF_QUALITY = 2;
    private const uint DEFAULT_PITCH = 0, FF_DONTCARE = 0;
    private const int TRANSPARENT = 1;
    private const uint DT_CENTER = 1, DT_VCENTER = 4, DT_SINGLELINE = 32;
    private const int LOGPIXELSY = 90;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASS
    {
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string lpszMenuName;
        public string lpszClassName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int x; public int y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam; public IntPtr lParam; public uint time; public int ptX; public int ptY; }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int left; public int top; public int right; public int bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct ICONINFO { public int fIcon; public int xHotspot; public int yHotspot; public IntPtr hbmMask; public IntPtr hbmColor; }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIconW(uint msg, ref NOTIFYICONDATA data);

    private static bool ShellNotify(uint msg, ref NOTIFYICONDATA nid, string desc)
    {
        bool ok = Shell_NotifyIconW(msg, ref nid);
        if (!ok) Log($"ShellNotify({desc}) failed, msg={msg}");
        return ok;
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassW(ref WNDCLASS wc);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowExW(uint exStyle, string cls, string title, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);
    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hWnd);
    [DllImport("user32.dll")]
    private static extern int GetMessageW(out MSG msg, IntPtr hWnd, uint filterMin, uint filterMax);
    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG msg);
    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessageW(ref MSG msg);
    [DllImport("user32.dll")]
    private static extern void PostQuitMessage(int code);
    [DllImport("user32.dll")]
    private static extern IntPtr SetTimer(IntPtr hWnd, IntPtr id, uint ms, IntPtr proc);
    [DllImport("user32.dll")]
    private static extern bool KillTimer(IntPtr hWnd, IntPtr id);
    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenuW(IntPtr hMenu, uint uFlags, IntPtr uIDNewItem, string lpNewString);
    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr hMenu);
    [DllImport("user32.dll")]
    private static extern uint TrackPopupMenu(IntPtr hMenu, uint uFlags, int x, int y, int nReserved, IntPtr hWnd, IntPtr prcRect);
    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT pt);
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string? name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool PostMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    // GDI
    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int cx, int cy);
    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateSolidBrush(int color);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateFontW(int nHeight, int nWidth, int nEscapement, int nOrientation,
        int fnWeight, uint fdwItalic, uint fdwUnderline, uint fdwStrikeOut, uint fdwCharSet,
        uint fdwOutputPrecision, uint fdwClipPrecision, uint fdwQuality,
        uint fdwPitchAndFamily, string lpszFace);
    [DllImport("gdi32.dll")]
    private static extern int SetBkMode(IntPtr hdc, int mode);
    [DllImport("gdi32.dll")]
    private static extern uint SetTextColor(IntPtr hdc, int color);
    [DllImport("gdi32.dll")]
    private static extern int GetDeviceCaps(IntPtr hdc, int index);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int DrawTextW(IntPtr hdc, string lpchText, int cchText, ref RECT lprc, uint format);
    [DllImport("user32.dll")]
    private static extern IntPtr CreateIconIndirect(ref ICONINFO iconInfo);
    [DllImport("user32.dll")]
    private static extern int FillRect(IntPtr hdc, ref RECT lprc, IntPtr hbr);
    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateBitmap(int nWidth, int nHeight, uint nPlanes, uint nBitCount, IntPtr lpBits);
    [DllImport("gdi32.dll")]
    private static extern int SetBitmapBits(IntPtr hbm, int cb, byte[] lpBits);
    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFOHEADER pbmi, uint usage, out IntPtr ppvBits, IntPtr hSection, uint dwOffset);
    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    private const int MaxLogSize = 64 * 1024;
    private static readonly string LogFilePath = Path.Combine(Path.GetDirectoryName(Environment.ProcessPath) ?? ".", "traylog.log");
    private static FileStream? _logStream;
    private static long _logPosition;
    private static readonly object LogLock = new();
    private static readonly string[] SchemeNames = ["InzoneBatteryTray（ClrMD）", "InzoneUsbTray（USB CDC）"];

    private IntPtr _hwnd;
    private IntPtr _hCurrentIcon;
    private int _headsetBattery = -1, _mouseBattery = -1;
    private bool _headsetCharging, _showDimmed, _blinkActive, _reading;
    private int _pendingResults;
    private int _lastHeadsetNotifyBattery = -1, _lastMouseNotifyBattery = -1;
    private IBatteryReader? _headsetReader;
    private IBatteryReader? _mouseReader;
    private Config _config;
    private WndProcDelegate? _wndProcDelegate;
    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    public MainForm(Config config)
    {
        _config = config;
        InitLog();
        TelemetrySender.Init(config.TelemetryEnabled, config.TelemetryPort);
        HttpServer.Start(config.HttpPort);
        Log($"App started, scheme={_config.SchemeIndex}, freq={Config.IntervalValues[_config.RefreshIntervalIndex]}ms");
    }

    public void Run()
    {
        _wndProcDelegate = WndProc;
        var wc = new WNDCLASS
        {
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate),
            hInstance = GetModuleHandleW(null),
            lpszClassName = "InzoneBatteryTrayWnd",
        };
        if (RegisterClassW(ref wc) == 0)
        { Log("RegisterClassW failed"); return; }
        _hwnd = CreateWindowExW(0, "InzoneBatteryTrayWnd", "", WS_DISABLED, 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, wc.hInstance, IntPtr.Zero);
        if (_hwnd == IntPtr.Zero)
        { Log("CreateWindowExW failed"); return; }

        while (GetMessageW(out var msg, IntPtr.Zero, 0, 0) != 0)
        {
            TranslateMessage(ref msg);
            DispatchMessageW(ref msg);
        }
        Cleanup();
    }

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case WM_CREATE:
                _hwnd = hWnd;
                _hCurrentIcon = MakeIcon(-1);
                var addNid = MakeNID(NIM_ADD);
                addNid.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP;
                addNid.hIcon = _hCurrentIcon;
                addNid.szTip = "INZONE H9: initializing...";
                ShellNotify(NIM_ADD, ref addNid, "NIM_ADD");
                SwitchReader(_config.SchemeIndex);
                _mouseReader = new RapooHidReader();
                SetTimer(hWnd, (IntPtr)PollTimerId, (uint)Config.IntervalValues[_config.RefreshIntervalIndex], IntPtr.Zero);
                return IntPtr.Zero;

            case WM_TIMER:
                if ((int)wParam == PollTimerId) StartBatteryRead();
                else if ((int)wParam == BlinkTimerId)
                { _showDimmed = !_showDimmed; UpdateIcon(_headsetBattery, _headsetCharging, _showDimmed); }
                return IntPtr.Zero;

            case WmHeadsetResult:
                _headsetBattery = (int)wParam;
                _headsetCharging = (int)lParam == 1;
                _pendingResults--;
                if (_pendingResults == 0) OnBothResults();
                return IntPtr.Zero;

            case WmMouseResult:
                _mouseBattery = (int)wParam;
                _pendingResults--;
                if (_pendingResults == 0) OnBothResults();
                return IntPtr.Zero;

            case WmMenuCommand:
                ExecuteCommand((uint)wParam);
                return IntPtr.Zero;

            case WmTrayCallback:
                if ((uint)lParam == WM_RBUTTONDOWN || (uint)lParam == 0x007B) ShowContextMenu();
                else if ((uint)lParam == WM_LBUTTONDBLCLK) StartBatteryRead();
                return IntPtr.Zero;

            case WM_COMMAND:
                ExecuteCommand((uint)wParam & 0xFFFF);
                return IntPtr.Zero;

            case WM_DESTROY:
                PostQuitMessage(0);
                return IntPtr.Zero;
        }
        return DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    private void StartBatteryRead()
    {
        if (_reading) return;
        _reading = true;
        _pendingResults = 2;
        Task.Run(() =>
        {
            try
            {
                var (hb, hc) = _headsetReader?.Read() ?? (-1, false);
                PostMessageW(_hwnd, WmHeadsetResult, (IntPtr)hb, (IntPtr)(hc ? 1 : 0));
            }
            catch { PostMessageW(_hwnd, WmHeadsetResult, (IntPtr)(-1), IntPtr.Zero); }

            try
            {
                var (mb, _) = _mouseReader?.Read() ?? (-1, false);
                PostMessageW(_hwnd, WmMouseResult, (IntPtr)mb, IntPtr.Zero);
            }
            catch { PostMessageW(_hwnd, WmMouseResult, (IntPtr)(-1), IntPtr.Zero); }
        });
    }

    private void OnBothResults()
    {
        _reading = false;
        string chg = _headsetCharging ? " CHG" : "";
        Log($"headset={_headsetBattery}{chg} mouse={_mouseBattery} method={_headsetReader?.Method ?? "none"}");
        TelemetrySender.Send(_headsetBattery, _headsetCharging, _mouseBattery);
        HttpServer.UpdateData(_headsetBattery, _headsetCharging, _mouseBattery);
        CheckLowBattery();
        UpdatePollInterval();
        UpdateTray();
        if (_headsetCharging && !_blinkActive)
        { _blinkActive = true; _showDimmed = false; SetTimer(_hwnd, (IntPtr)BlinkTimerId, 600, IntPtr.Zero); }
        else if (!_headsetCharging && _blinkActive)
        { _blinkActive = false; KillTimer(_hwnd, (IntPtr)BlinkTimerId); UpdateIcon(_headsetBattery, false, false); }
    }

    private void CheckLowBattery()
    {
        if (_headsetBattery >= 0 && _headsetBattery < 20 && _headsetBattery != _lastHeadsetNotifyBattery)
        {
            _lastHeadsetNotifyBattery = _headsetBattery;
            ShowBalloon("INZONE H9 电量低", $"剩余 {_headsetBattery}%", NIIF_ERROR);
        }
        else if (_headsetBattery >= 25)
            _lastHeadsetNotifyBattery = -1;

        if (_mouseBattery >= 0 && _mouseBattery < 20 && _mouseBattery != _lastMouseNotifyBattery)
        {
            _lastMouseNotifyBattery = _mouseBattery;
            ShowBalloon("Rapoo VT3S 电量低", $"剩余 {_mouseBattery}%", NIIF_ERROR);
        }
        else if (_mouseBattery >= 25)
            _lastMouseNotifyBattery = -1;
    }

    private void ShowBalloon(string title, string text, uint iconType)
    {
        var nid = MakeNID(NIM_MODIFY);
        nid.uFlags = NIF_INFO;
        nid.szInfo = text.Length > 256 ? text[..256] : text;
        nid.szInfoTitle = title.Length > 64 ? title[..64] : title;
        nid.dwInfoFlags = iconType;
        nid.uTimeoutOrVersion = 5000;
        ShellNotify(NIM_MODIFY, ref nid, "NIM_MODIFY_BALLOON");
    }

    private void UpdatePollInterval()
    {
        uint interval = (_headsetBattery < 0 && _mouseBattery < 0) ? 2000u : (uint)Config.IntervalValues[_config.RefreshIntervalIndex];
        KillTimer(_hwnd, (IntPtr)PollTimerId);
        SetTimer(_hwnd, (IntPtr)PollTimerId, interval, IntPtr.Zero);
    }

    private void SwitchReader(int schemeIndex)
    {
        _config.SchemeIndex = schemeIndex;
        _config.Save();
        _headsetReader?.Dispose();
        _headsetReader = schemeIndex == 0 ? new ClrMdReader() : new UsbCdcReader();
        Log($"Switched to scheme {schemeIndex} ({SchemeNames[schemeIndex]})");
        StartBatteryRead();
    }

    private void SetFrequency(int index)
    {
        _config.RefreshIntervalIndex = index;
        _config.Save();
        UpdatePollInterval();
        Log($"Frequency changed to {Config.IntervalLabels[index]}");
    }

    private static bool IsAutoStartEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(AutoStartRegKey, false);
            return key?.GetValue(AutoStartValueName) != null;
        }
        catch { return false; }
    }

    private static void ToggleAutoStart()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(AutoStartRegKey, true);
            if (key == null) return;
            if (key.GetValue(AutoStartValueName) != null)
                key.DeleteValue(AutoStartValueName, false);
            else
                key.SetValue(AutoStartValueName, $"\"{Environment.ProcessPath}\"");
        }
        catch { }
    }

    private void ExecuteCommand(uint cmd)
    {
        if (cmd == IdmRefreshNow) StartBatteryRead();
        else if (cmd == IdmScanComPort) { UsbCdcReader.ClearCache(); StartBatteryRead(); }
        else if (cmd == IdmAutoStart) { ToggleAutoStart(); }
        else if (cmd == IdmExit) PostQuitMessage(0);
        else if (cmd >= IdmSchemeStart && cmd < IdmSchemeStart + SchemeNames.Length) SwitchReader((int)(cmd - IdmSchemeStart));
        else if (cmd >= IdmFreqStart && cmd < IdmFreqStart + Config.IntervalLabels.Length) SetFrequency((int)(cmd - IdmFreqStart));
    }

    private void UpdateTray()
    {
        string method = _headsetReader?.Method ?? "none";
        string chg = _headsetCharging ? " CHG" : "";
        string hText = _headsetBattery >= 0 ? $"INZONE H9: {_headsetBattery}%{chg}" : "INZONE H9: N/A";
        string mText = _mouseBattery >= 0 ? $"Rapoo VT3S: {_mouseBattery}%" : "Rapoo VT3S: N/A";
        string text = $"{hText} | {mText}";
        var nid = MakeNID(NIM_MODIFY);
        nid.uFlags = NIF_TIP;
        nid.szTip = text.Length > 128 ? text[..128] : text;
        ShellNotify(NIM_MODIFY, ref nid, "NIM_MODIFY_TIP");
        UpdateIcon(_headsetBattery, _headsetCharging, false);
    }

    private void UpdateIcon(int battery, bool charging, bool dimmed)
    {
        var hNew = MakeIcon(battery, charging, dimmed);
        var nid = MakeNID(NIM_MODIFY);
        nid.uFlags = NIF_ICON;
        nid.hIcon = hNew;
        ShellNotify(NIM_MODIFY, ref nid, "NIM_MODIFY_ICON");
        if (_hCurrentIcon != IntPtr.Zero) DestroyIcon(_hCurrentIcon);
        _hCurrentIcon = hNew;
    }

    private NOTIFYICONDATA MakeNID(uint op) => new()
    {
        cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
        hWnd = _hwnd,
        uID = TRAY_ID,
        uCallbackMessage = WmTrayCallback,
    };

    private void ShowContextMenu()
    {
        var hMenu = CreatePopupMenu();
        AppendMenuW(hMenu, MF_STRING, (IntPtr)IdmRefreshNow, "Refresh Now");
        AppendMenuW(hMenu, MF_STRING, (IntPtr)IdmScanComPort, "Scan COM Port");
        AppendMenuW(hMenu, MF_STRING | (IsAutoStartEnabled() ? MF_CHECKED : 0), (IntPtr)IdmAutoStart, "开机自启");
        AppendMenuW(hMenu, MF_SEPARATOR, IntPtr.Zero, "");

        var hScheme = CreatePopupMenu();
        for (int i = 0; i < SchemeNames.Length; i++)
            AppendMenuW(hScheme, MF_STRING | (i == _config.SchemeIndex ? MF_CHECKED : 0), (IntPtr)(IdmSchemeStart + i), SchemeNames[i]);
        AppendMenuW(hMenu, MF_POPUP, hScheme, "方案切换");

        var hFreq = CreatePopupMenu();
        for (int i = 0; i < Config.IntervalLabels.Length; i++)
            AppendMenuW(hFreq, MF_STRING | (i == _config.RefreshIntervalIndex ? MF_CHECKED : 0), (IntPtr)(IdmFreqStart + i), Config.IntervalLabels[i]);
        AppendMenuW(hMenu, MF_POPUP, hFreq, "刷新频率");

        AppendMenuW(hMenu, MF_SEPARATOR, IntPtr.Zero, "");
        AppendMenuW(hMenu, MF_STRING, (IntPtr)IdmExit, "Exit");

        GetCursorPos(out var pt);
        SetForegroundWindow(_hwnd);
        uint cmd = TrackPopupMenu(hMenu, TPM_RETURNCMD | TPM_RIGHTBUTTON, pt.x, pt.y, 0, _hwnd, IntPtr.Zero);
        DestroyMenu(hFreq);
        DestroyMenu(hScheme);
        DestroyMenu(hMenu);
        if (cmd != 0) PostMessageW(_hwnd, WmMenuCommand, (IntPtr)cmd, IntPtr.Zero);
    }

    private static int ColorRef(byte r, byte g, byte b) => r | (g << 8) | (b << 16);
    private static int MulDiv(int a, int b, int c) => (int)((long)a * b / c);

    private static (byte bgR, byte bgG, byte bgB, byte fgR, byte fgG, byte fgB) IconColors(int battery) => battery switch
    {
        < 0 => (0x75, 0x75, 0x75, 0xFF, 0xFF, 0xFF),
        < 50 => (0xC6, 0x28, 0x28, 0xFF, 0xFF, 0xFF),
        < 70 => (0xA0, 0x63, 0x00, 0x1A, 0x0F, 0x00),
        _ => (0x1B, 0x5E, 0x20, 0xFF, 0xFF, 0xFF)
    };

    private static IntPtr MakeIcon(int battery, bool charging = false, bool dimmed = false)
    {
        var (bgR, bgG, bgB, fgR, fgG, fgB) = IconColors(battery);
        if (charging && dimmed)
        {
            bgR = (byte)Math.Min(bgR + 80, 255);
            bgG = (byte)Math.Min(bgG + 80, 255);
            bgB = (byte)Math.Min(bgB + 80, 255);
        }
        string text = battery >= 0 ? battery.ToString() : "--";
        int ptSize = text.Length >= 3 ? 10 : 16;

        var bmi = new BITMAPINFOHEADER();
        bmi.biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>();
        bmi.biWidth = 32;
        bmi.biHeight = -32;
        bmi.biPlanes = 1;
        bmi.biBitCount = 32;
        bmi.biCompression = 0;

        IntPtr hdcScreen = GetDC(IntPtr.Zero);
        IntPtr hdcMem = CreateCompatibleDC(hdcScreen);
        IntPtr hbm = CreateDIBSection(hdcScreen, ref bmi, 0, out _, IntPtr.Zero, 0);
        ReleaseDC(IntPtr.Zero, hdcScreen);
        if (hbm == IntPtr.Zero) { DeleteDC(hdcMem); return IntPtr.Zero; }
        SelectObject(hdcMem, hbm);

        IntPtr brush = CreateSolidBrush(ColorRef(bgR, bgG, bgB));
        var rc = new RECT { left = 0, top = 0, right = 32, bottom = 32 };
        FillRect(hdcMem, ref rc, brush);
        DeleteObject(brush);

        int logSize = -MulDiv(ptSize, GetDeviceCaps(hdcMem, LOGPIXELSY), 72);
        int maxPx = text.Length >= 3 ? 16 : 24;
        if (-logSize > maxPx) logSize = -maxPx;
        IntPtr hFont = CreateFontW(logSize, 0, 0, 0, FW_BOLD, 0, 0, 0,
            DEFAULT_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS, PROOF_QUALITY,
            DEFAULT_PITCH | FF_DONTCARE, "Segoe UI");
        if (hFont != IntPtr.Zero) SelectObject(hdcMem, hFont);

        SetBkMode(hdcMem, TRANSPARENT);
        SetTextColor(hdcMem, ColorRef(fgR, fgG, fgB));
        DrawTextW(hdcMem, text, text.Length, ref rc, DT_CENTER | DT_VCENTER | DT_SINGLELINE);
        SelectObject(hdcMem, hFont); // deselect before delete

        IntPtr hbmMask = CreateBitmap(32, 32, 1, 1, IntPtr.Zero);
        SetBitmapBits(hbmMask, 128, new byte[128]);
        var ii = new ICONINFO { fIcon = 1, hbmColor = hbm, hbmMask = hbmMask };
        IntPtr hIcon = CreateIconIndirect(ref ii);

        DeleteObject(hbmMask);
        if (hFont != IntPtr.Zero) DeleteObject(hFont);
        DeleteObject(hbm);
        DeleteDC(hdcMem);
        return hIcon;
    }

    private static void InitLog()
    {
        try
        {
            var dir = Path.GetDirectoryName(LogFilePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            _logStream = new FileStream(LogFilePath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read);
            _logPosition = _logStream.Length;
            if (_logPosition > MaxLogSize)
            { _logPosition = 0; _logStream.SetLength(MaxLogSize); _logStream.Seek(0, SeekOrigin.Begin); }
        }
        catch { }
    }

    internal static void Log(string message)
    {
        if (_logStream == null) return;
        lock (LogLock)
        {
            try
            {
                string text = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\n";
                byte[] bytes = Encoding.UTF8.GetBytes(text);
                if (_logPosition + bytes.Length > MaxLogSize)
                { _logPosition = 0; _logStream.Seek(0, SeekOrigin.Begin); }
                _logStream.Write(bytes, 0, bytes.Length);
                _logPosition += bytes.Length;
                _logStream.Flush();
            }
            catch { }
        }
    }

    private void Cleanup()
    {
        KillTimer(_hwnd, (IntPtr)PollTimerId);
        if (_blinkActive) KillTimer(_hwnd, (IntPtr)BlinkTimerId);
        _headsetReader?.Dispose();
        _mouseReader?.Dispose();
        var del = MakeNID(NIM_DELETE);
        ShellNotify(NIM_DELETE, ref del, "NIM_DELETE");
        if (_hCurrentIcon != IntPtr.Zero) DestroyIcon(_hCurrentIcon);
        DestroyWindow(_hwnd);
        Log("App closed");
        _logStream?.Flush();
        _logStream?.Dispose();
    }
}
