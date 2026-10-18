using System;
using System.IO;
using System.Runtime.InteropServices;

namespace LemonAID.Services;

public sealed class TrayIconService : IDisposable
{
    private const int WM_APP = 0x8000;
    private const uint WM_TRAYICON = WM_APP + 42;

    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_LBUTTONDBLCLK = 0x0203;
    private const int WM_RBUTTONUP = 0x0205;
    private const int WM_CONTEXTMENU = 0x007B;

    private const uint NIM_ADD = 0x00000000;
    private const uint NIM_MODIFY = 0x00000001;
    private const uint NIM_DELETE = 0x00000002;

    private const uint NIF_MESSAGE = 0x00000001;
    private const uint NIF_ICON = 0x00000002;
    private const uint NIF_TIP = 0x00000004;
    private const uint NIF_INFO = 0x00000010;

    private const uint IMAGE_ICON = 1;
    private const uint LR_LOADFROMFILE = 0x00000010;

    private const uint MF_STRING = 0x00000000;
    private const uint MF_SEPARATOR = 0x00000800;
    private const uint MF_CHECKED = 0x00000008;
    private const uint MF_UNCHECKED = 0x00000000;

    private const uint TPM_RETURNCMD = 0x0100;
    private const uint TPM_NONOTIFY = 0x0080;
    private const uint TPM_RIGHTBUTTON = 0x0002;

    private const int CmdOpenDashboard = 1001;
    private const int CmdSummonWheel = 1002;
    private const int CmdToggleHook = 1003;
    private const int CmdToggleStartup = 1004;
    private const int CmdExit = 1005;

    private delegate IntPtr SUBCLASSPROC(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, UIntPtr uIdSubclass, IntPtr dwRefData);

    private readonly IntPtr _hwnd;
    private readonly SUBCLASSPROC _subclassDelegate;
    private IntPtr _hIcon = IntPtr.Zero;
    private bool _isAdded;

    public event Action? OpenDashboardRequested;
    public event Action<int, int>? SummonWheelHereRequested;
    public event Action? ToggleHookRequested;
    public event Action? ToggleStartupRequested;
    public event Action? ExitApplicationRequested;

    public Func<bool>? IsHookEnabledProvider { get; set; }
    public Func<bool>? IsStartupEnabledProvider { get; set; }

    public TrayIconService(IntPtr hwnd)
    {
        _hwnd = hwnd;
        _subclassDelegate = WindowSubclassProc;
    }

    public void Initialize()
    {
        if (_isAdded) return;

        SetWindowSubclass(_hwnd, _subclassDelegate, new UIntPtr(1), IntPtr.Zero);

        string icoPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        if (File.Exists(icoPath))
        {
            _hIcon = LoadImageW(IntPtr.Zero, icoPath, IMAGE_ICON, 32, 32, LR_LOADFROMFILE);
        }
        if (_hIcon == IntPtr.Zero)
        {
            _hIcon = LoadIconW(IntPtr.Zero, new IntPtr(32512)); // IDI_APPLICATION fallback
        }

        var data = new NOTIFYICONDATAW
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(),
            hWnd = _hwnd,
            uID = 1,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
            uCallbackMessage = WM_TRAYICON,
            hIcon = _hIcon,
            szTip = "LemonAID · Background Radial Utility (Hold Click + Shift/Alt/Ctrl)"
        };

        Shell_NotifyIconW(NIM_ADD, ref data);
        _isAdded = true;
    }

    public void ShowBalloonNotification(string title, string message)
    {
        if (!_isAdded) return;

        var data = new NOTIFYICONDATAW
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(),
            hWnd = _hwnd,
            uID = 1,
            uFlags = NIF_INFO,
            szInfoTitle = title,
            szInfo = message,
            dwInfoFlags = 1 // NIIF_INFO
        };

        Shell_NotifyIconW(NIM_MODIFY, ref data);
    }

    private IntPtr WindowSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, UIntPtr uIdSubclass, IntPtr dwRefData)
    {
        if (uMsg == WM_TRAYICON)
        {
            int mouseMsg = (int)(lParam.ToInt64() & 0xFFFF);
            if (mouseMsg is WM_LBUTTONUP or WM_LBUTTONDBLCLK)
            {
                OpenDashboardRequested?.Invoke();
                return IntPtr.Zero;
            }
            if (mouseMsg is WM_RBUTTONUP or WM_CONTEXTMENU)
            {
                ShowContextMenu();
                return IntPtr.Zero;
            }
        }

        return DefSubclassProc(hWnd, uMsg, wParam, lParam);
    }

    private void ShowContextMenu()
    {
        IntPtr hMenu = CreatePopupMenu();
        if (hMenu == IntPtr.Zero) return;

        bool hookOn = IsHookEnabledProvider?.Invoke() ?? true;
        bool startupOn = IsStartupEnabledProvider?.Invoke() ?? false;

        AppendMenuW(hMenu, MF_STRING, new UIntPtr(CmdOpenDashboard), "Open LemonAID Dashboard");
        AppendMenuW(hMenu, MF_STRING, new UIntPtr(CmdSummonWheel), "Summon Radial Wheel Here");
        AppendMenuW(hMenu, MF_SEPARATOR, UIntPtr.Zero, null);
        AppendMenuW(hMenu, MF_STRING | (hookOn ? MF_CHECKED : MF_UNCHECKED), new UIntPtr(CmdToggleHook), "Global Hook Active (Shift / Alt / Ctrl)");
        AppendMenuW(hMenu, MF_STRING | (startupOn ? MF_CHECKED : MF_UNCHECKED), new UIntPtr(CmdToggleStartup), "Launch Headless at Windows Sign-In");
        AppendMenuW(hMenu, MF_SEPARATOR, UIntPtr.Zero, null);
        AppendMenuW(hMenu, MF_STRING, new UIntPtr(CmdExit), "Exit LemonAID");

        GetCursorPos(out POINT pt);
        SetForegroundWindow(_hwnd);

        int selectedCmd = TrackPopupMenuEx(
            hMenu,
            TPM_RETURNCMD | TPM_NONOTIFY | TPM_RIGHTBUTTON,
            pt.X,
            pt.Y,
            _hwnd,
            IntPtr.Zero);

        DestroyMenu(hMenu);

        switch (selectedCmd)
        {
            case CmdOpenDashboard:
                OpenDashboardRequested?.Invoke();
                break;
            case CmdSummonWheel:
                SummonWheelHereRequested?.Invoke(pt.X, pt.Y);
                break;
            case CmdToggleHook:
                ToggleHookRequested?.Invoke();
                break;
            case CmdToggleStartup:
                ToggleStartupRequested?.Invoke();
                break;
            case CmdExit:
                ExitApplicationRequested?.Invoke();
                break;
        }
    }

    public void Dispose()
    {
        if (_isAdded)
        {
            var data = new NOTIFYICONDATAW
            {
                cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(),
                hWnd = _hwnd,
                uID = 1
            };
            Shell_NotifyIconW(NIM_DELETE, ref data);
            RemoveWindowSubclass(_hwnd, _subclassDelegate, new UIntPtr(1));
            _isAdded = false;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATAW
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint dwInfoFlags;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Shell_NotifyIconW(uint dwMessage, ref NOTIFYICONDATAW lpData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadImageW(IntPtr hInst, string lpszName, uint uType, int cxDesired, int cyDesired, uint fuLoad);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadIconW(IntPtr hInstance, IntPtr lpIconName);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AppendMenuW(IntPtr hMenu, uint uFlags, UIntPtr uIDNewItem, string? lpNewItem);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenuEx(IntPtr hmenu, uint fuFlags, int x, int y, IntPtr hwnd, IntPtr lptpm);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowSubclass(IntPtr hWnd, SUBCLASSPROC pfnSubclass, UIntPtr uIdSubclass, IntPtr dwRefData);

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveWindowSubclass(IntPtr hWnd, SUBCLASSPROC pfnSubclass, UIntPtr uIdSubclass);

    [DllImport("comctl32.dll")]
    private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);
}
