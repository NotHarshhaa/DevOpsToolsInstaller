using System;
using System.Runtime.InteropServices;

namespace DevOpsToolsInstaller.Services;

/// <summary>
/// A native Windows system tray icon (Shell_NotifyIcon) with a context menu.
/// Owns a hidden Win32 message-only window that receives the tray callbacks;
/// actions are surfaced to the app through events (marshal to the UI thread
/// in the subscriber).
/// </summary>
public static class TrayIconService
{
    // ── Events (raised on the thread that owns the hidden window) ────────
    public static event Action? OpenRequested;
    public static event Action? CatalogRequested;
    public static event Action? CheckUpdatesRequested;
    public static event Action? ExitRequested;

    private const uint WM_APP = 0x8000;
    private static readonly uint WM_TRAYICON = WM_APP + 1;

    private const uint WM_LBUTTONUP = 0x0202;
    private const uint WM_LBUTTONDBLCLK = 0x0203;
    private const uint WM_RBUTTONUP = 0x0205;
    private const uint WM_CONTEXTMENU = 0x007B;

    private const uint NIM_ADD = 0x0000;
    private const uint NIM_MODIFY = 0x0001;
    private const uint NIM_DELETE = 0x0002;
    private const uint NIF_MESSAGE = 0x0001;
    private const uint NIF_ICON = 0x0002;
    private const uint NIF_TIP = 0x0004;

    private const uint TPM_RIGHTBUTTON = 0x0002;
    private const uint TPM_NONOTIFY = 0x0080;
    private const uint TPM_RETURNCMD = 0x0100;

    private const int IDM_OPEN = 1;
    private const int IDM_CATALOG = 2;
    private const int IDM_CHECK_UPDATES = 3;
    private const int IDM_EXIT = 4;

    private static IntPtr _hwnd = IntPtr.Zero;
    private static IntPtr _hIcon = IntPtr.Zero;
    private static bool _added;
    private static string _tooltip = "DevOps Tools Installer";
    private static readonly WndProcDelegate WndProcStub = WndProc;

    public static bool IsAvailable => _added;

    // ── Public API ────────────────────────────────────────────────────────

    /// <summary>
    /// Creates the tray icon. Safe to call repeatedly; returns false when the
    /// icon could not be created (the app continues normally without it).
    /// </summary>
    public static bool Initialize(string iconPath, string tooltip)
    {
        if (_added) return true;

        try
        {
            _tooltip = tooltip;

            _hwnd = CreateMessageWindow();
            if (_hwnd == IntPtr.Zero) return false;

            _hIcon = LoadImage(
                IntPtr.Zero, iconPath, IMAGE_ICON, 0, 0,
                LR_LOADFROMFILE | LR_DEFAULTSIZE);

            var addData = BuildIconData();
            _added = Shell_NotifyIcon(NIM_ADD, ref addData);
            return _added;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Updates the tooltip text shown on hover.</summary>
    public static void SetTooltip(string tooltip)
    {
        if (!_added) return;
        try
        {
            _tooltip = tooltip;
            var modData = BuildIconData();
            Shell_NotifyIcon(NIM_MODIFY, ref modData);
        }
        catch
        {
        }
    }

    /// <summary>Removes the tray icon (call before application exit).</summary>
    public static void Shutdown()
    {
        try
        {
            if (_added)
            {
                var delData = BuildIconData();
                Shell_NotifyIcon(NIM_DELETE, ref delData);
                _added = false;
            }
            if (_hwnd != IntPtr.Zero)
            {
                DestroyWindow(_hwnd);
                _hwnd = IntPtr.Zero;
            }
            if (_hIcon != IntPtr.Zero)
            {
                DestroyIcon(_hIcon);
                _hIcon = IntPtr.Zero;
            }
        }
        catch
        {
        }
    }

    private static NOTIFYICONDATA BuildIconData()
    {
        return new NOTIFYICONDATA
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _hwnd,
            uID = 1,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
            uCallbackMessage = WM_TRAYICON,
            hIcon = _hIcon,
            szTip = _tooltip
        };
    }

    // ── Message-only window ───────────────────────────────────────────────

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public uint cbSize;
        public uint style;
        public WndProcDelegate lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
        public IntPtr hIconSm;
    }

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
        public uint uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA lpData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassEx(ref WNDCLASSEX lpWndClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowExW(
        uint dwExStyle, string lpClassName, string lpWindowName,
        uint dwStyle, int x, int y, int nWidth, int nHeight,
        IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadImage(
        IntPtr hInstance, string lpFileName, uint ulType,
        int cxDesired, int cyDesired, uint fuLoad);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AppendMenu(IntPtr hMenu, uint uFlags, uint uIDNewItem, string? lpNewItem);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenu(
        IntPtr hMenu, uint uFlags, int x, int y, int nReserved, IntPtr hWnd, IntPtr prcRect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(IntPtr hMenu);

    private const uint IMAGE_ICON = 1;
    private const uint LR_LOADFROMFILE = 0x00000010;
    private const uint LR_DEFAULTSIZE = 0x00000040;
    private const uint MF_STRING = 0x00000000;
    private const uint MF_SEPARATOR = 0x00000800;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    private static IntPtr CreateMessageWindow()
    {
        const uint HWND_MESSAGE = unchecked((uint)-3);
        var hInstance = GetModuleHandleW(null);

        var wc = new WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = WndProcStub,
            hInstance = hInstance,
            lpszClassName = "DevOpsToolsInstaller_TrayWnd"
        };

        if (RegisterClassEx(ref wc) == 0)
        {
            // Class may already be registered from a previous Initialize — that's fine.
        }

        return CreateWindowExW(
            0, wc.lpszClassName, "DevOpsToolsInstallerTray", 0,
            0, 0, 0, 0, new IntPtr(HWND_MESSAGE), IntPtr.Zero, hInstance, IntPtr.Zero);
    }

    private static IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_TRAYICON)
        {
            var mouseMsg = (uint)(lParam.ToInt64() & 0xFFFF);
            if (mouseMsg is WM_LBUTTONUP or WM_LBUTTONDBLCLK)
            {
                OpenRequested?.Invoke();
            }
            else if (mouseMsg is WM_RBUTTONUP or WM_CONTEXTMENU)
            {
                ShowContextMenu();
            }
            return IntPtr.Zero;
        }

        return DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    private static void ShowContextMenu()
    {
        try
        {
            GetCursorPos(out var pt);
            SetForegroundWindow(_hwnd);

            var menu = CreatePopupMenu();
            AppendMenu(menu, MF_STRING, IDM_OPEN, "Open DevOps Tools Installer");
            AppendMenu(menu, MF_STRING, IDM_CATALOG, "Open Tool Catalog");
            AppendMenu(menu, MF_STRING, IDM_CHECK_UPDATES, "Check for tool updates");
            AppendMenu(menu, MF_SEPARATOR, 0, null);
            AppendMenu(menu, MF_STRING, IDM_EXIT, "Exit");

            var choice = (uint)TrackPopupMenu(
                menu, TPM_RIGHTBUTTON | TPM_NONOTIFY | TPM_RETURNCMD,
                pt.X, pt.Y, 0, _hwnd, IntPtr.Zero);
            DestroyMenu(menu);

            switch (choice)
            {
                case IDM_OPEN:
                    OpenRequested?.Invoke();
                    break;
                case IDM_CATALOG:
                    CatalogRequested?.Invoke();
                    break;
                case IDM_CHECK_UPDATES:
                    CheckUpdatesRequested?.Invoke();
                    break;
                case IDM_EXIT:
                    ExitRequested?.Invoke();
                    break;
            }
        }
        catch
        {
            // Menu is a convenience — never crash from it.
        }
    }
}
