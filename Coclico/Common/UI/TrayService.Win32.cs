using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;

namespace Coclico.Services;

public partial class TrayService : IDisposable
{
    private const int ID_RESTORE = 1000;
    private const int ID_EXIT = 1001;
    private const int WM_LBUTTONDBLCLK = 0x0203;
    private const int WM_RBUTTONUP = 0x0205;

    private HwndSource? _hwndSource;
    private uint _id;
    private IntPtr _hMenu = IntPtr.Zero;

    public bool IsInitialized { get; private set; }

    public void Initialize(Window mainWindow)
    {
        if (IsInitialized)
        {
            return;
        }

        var helper = new WindowInteropHelper(mainWindow);
        if (helper.Handle == IntPtr.Zero)
        {
            throw new InvalidOperationException("Window must be shown before initializing tray icon.");
        }

        _hwndSource = HwndSource.FromHwnd(helper.Handle);
        if (_hwndSource == null)
        {
            throw new InvalidOperationException("Failed to get HwndSource.");
        }

        _hwndSource.AddHook(WndProc);

        _id = (uint)helper.Handle.ToInt64();

        var nid = new NOTIFYICONDATAW();
        nid.cbSize = (uint)Marshal.SizeOf(nid);
        nid.hWnd = helper.Handle;
        nid.uID = _id;
        nid.uFlags = NIF_MESSAGE | NIF_TIP | NIF_ICON;
        nid.uCallbackMessage = WM_TRAYMESSAGE;
        IntPtr hIcon = SendMessageW(helper.Handle, WM_GETICON, ICON_SMALL, IntPtr.Zero);
        if (hIcon == IntPtr.Zero)
        {
            hIcon = SendMessageW(helper.Handle, WM_GETICON, ICON_BIG, IntPtr.Zero);
        }

        if (hIcon == IntPtr.Zero)
        {
            hIcon = LoadIconW(IntPtr.Zero, IDI_APPLICATION);
        }

        nid.hIcon = hIcon;
        SetTip(ref nid, "Coclico");

        _ = Shell_NotifyIcon(NIM_ADD, ref nid);
        _hMenu = CreatePopupMenu();
        string restoreLabel = ServiceContainer.GetRequired<LocalizationService>().Get("Tray_Restore");
        string exitLabel = ServiceContainer.GetRequired<LocalizationService>().Get("Tray_Exit");
        _ = AppendMenuW(_hMenu, MF_STRING, ID_RESTORE, restoreLabel);
        _ = AppendMenuW(_hMenu, MF_STRING, ID_EXIT, exitLabel);
        IsInitialized = true;
    }

    public void ShowBalloon(string title, string text)
    {
        if (!IsInitialized || _hwndSource == null)
        {
            return;
        }

        var nid = new NOTIFYICONDATAW();
        nid.cbSize = (uint)Marshal.SizeOf(nid);
        nid.hWnd = _hwndSource.Handle;
        nid.uID = _id;
        SetTip(ref nid, text);
        nid.uFlags = NIF_INFO;
        SetBalloon(ref nid, title, text);
        _ = Shell_NotifyIcon(NIM_MODIFY, ref nid);
    }

    public void UpdateTooltip(string tip)
    {
        if (!IsInitialized || _hwndSource == null)
        {
            return;
        }

        var nid = new NOTIFYICONDATAW();
        nid.cbSize = (uint)Marshal.SizeOf(nid);
        nid.hWnd = _hwndSource.Handle;
        nid.uID = _id;
        nid.uFlags = NIF_TIP;
        SetTip(ref nid, tip);
        _ = Shell_NotifyIcon(NIM_MODIFY, ref nid);
    }

    public void Dispose()
    {
        try
        {
            if (!IsInitialized)
            {
                return;
            }

            var nid = new NOTIFYICONDATAW();
            nid.cbSize = (uint)Marshal.SizeOf(nid);
            nid.hWnd = _hwndSource?.Handle ?? IntPtr.Zero;
            nid.uID = _id;
            _ = Shell_NotifyIcon(NIM_DELETE, ref nid);
            _hwndSource?.RemoveHook(WndProc);
            _hwndSource = null;
            if (_hMenu != IntPtr.Zero)
            {
                _ = DestroyMenu(_hMenu);
                _hMenu = IntPtr.Zero;
            }
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "TrayService.Dispose");
        }
    }

    private static void SetTip(ref NOTIFYICONDATAW nid, string tip)
    {
        if (tip.Length >= 128)
        {
            tip = tip[..127];
        }

        nid.szTip = new string(tip);
    }

    private static void SetBalloon(ref NOTIFYICONDATAW nid, string title, string text)
    {
        nid.szInfo = new string(text);
        nid.szInfoTitle = new string(title.Length > 63 ? title[..63] : title);
        nid.dwInfoFlags = 0;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_TRAYMESSAGE)
        {
            int ev = lParam.ToInt32();
            if (ev == WM_LBUTTONDBLCLK)
            {
                RestoreMainWindow();
            }
            else if (ev == WM_RBUTTONUP)
            {
                ShowContextMenu(hwnd);
            }
            handled = true;
        }
        return IntPtr.Zero;
    }

    private void ShowContextMenu(IntPtr hwnd)
    {
        if (_hMenu == IntPtr.Zero)
        {
            return;
        }

        _ = SetForegroundWindow(hwnd);
        _ = GetCursorPos(out POINT p);
        uint cmd = TrackPopupMenuEx(_hMenu, TPM_RETURNCMD | TPM_LEFTALIGN, p.X, p.Y, hwnd, IntPtr.Zero);
        if (cmd == ID_RESTORE)
        {
            RestoreMainWindow();
        }
        else if (cmd == ID_EXIT)
        {
            Application.Current.Dispatcher.Invoke(Application.Current.Shutdown);
        }

        _ = PostMessage(hwnd, WM_NULL, IntPtr.Zero, IntPtr.Zero);
    }

    private static void RestoreMainWindow()
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            Window? win = Application.Current.MainWindow;
            if (win != null)
            {
                win.Show();
                win.WindowState = WindowState.Normal;
                _ = win.Activate();
            }
        });
    }

    private const int NIF_MESSAGE = 0x00000001;
    private const int NIF_ICON = 0x00000002;
    private const int NIF_TIP = 0x00000004;
    private const int NIF_INFO = 0x00000010;
    private const int NIM_ADD = 0x00000000;
    private const int NIM_MODIFY = 0x00000001;
    private const int NIM_DELETE = 0x00000002;
    private const int WM_TRAYMESSAGE = 0x8000 + 1;

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
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(int dwMessage, ref NOTIFYICONDATAW lpData);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool AppendMenuW(IntPtr hMenu, uint uFlags, UIntPtr uIDNewItem, string lpNewItem);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint TrackPopupMenuEx(IntPtr hMenu, uint uFlags, int x, int y, IntPtr hwnd, IntPtr lptpm);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessageW(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadIconW(IntPtr hInstance, IntPtr lpIconName);

    private const uint WM_GETICON = 0x007F;
    private const int ICON_SMALL = 0;
    private const int ICON_BIG = 1;
    private static readonly IntPtr IDI_APPLICATION = 32512;

    private const uint MF_STRING = 0x00000000;
    private const uint TPM_LEFTALIGN = 0x0000;
    private const uint TPM_RETURNCMD = 0x0100;
    private const int WM_NULL = 0x0000;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }
}