#nullable enable
using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;

namespace Coclico.Services;

public class TrayService : IDisposable
{
    private readonly IntPtr _iconHandle = IntPtr.Zero;
    private HwndSource? _hwndSource;
    private uint _id;
    private bool _initialized;
    private IntPtr _hMenu = IntPtr.Zero;

    public bool IsInitialized => _initialized;

    private const int ID_RESTORE = 1000;
    private const int ID_EXIT = 1001;
    private const int WM_LBUTTONDBLCLK = 0x0203;
    private const int WM_RBUTTONUP = 0x0205;

    public void Initialize(Window mainWindow)
    {
        if (_initialized) return;
        var helper = new WindowInteropHelper(mainWindow);
        if (helper.Handle == IntPtr.Zero)
            throw new InvalidOperationException("Window must be shown before initializing tray icon.");

        _hwndSource = HwndSource.FromHwnd(helper.Handle);
        if (_hwndSource == null) throw new InvalidOperationException("Failed to get HwndSource.");
        _hwndSource.AddHook(WndProc);

        _id = (uint)helper.Handle.ToInt64();

        var nid = new NOTIFYICONDATAW();
        nid.cbSize = (uint)Marshal.SizeOf(nid);
        nid.hWnd = helper.Handle;
        nid.uID = _id;
        nid.uFlags = NIF_MESSAGE | NIF_TIP | NIF_ICON;
        nid.uCallbackMessage = WM_TRAYMESSAGE;
        nid.hIcon = IntPtr.Zero;
        SetTip(ref nid, "Coclico");

        Shell_NotifyIcon(NIM_ADD, ref nid);
        _hMenu = CreatePopupMenu();
        var restoreLabel = ServiceContainer.GetRequired<LocalizationService>().Get("Tray_Restore");
        var exitLabel = ServiceContainer.GetRequired<LocalizationService>().Get("Tray_Exit");
        AppendMenuW(_hMenu, MF_STRING, (UIntPtr)ID_RESTORE, restoreLabel);
        AppendMenuW(_hMenu, MF_STRING, (UIntPtr)ID_EXIT, exitLabel);
        _initialized = true;
    }

    public void ShowBalloon(string title, string text)
    {
        if (!_initialized || _hwndSource == null) return;
        var nid = new NOTIFYICONDATAW();
        nid.cbSize = (uint)Marshal.SizeOf(nid);
        nid.hWnd = _hwndSource.Handle;
        nid.uID = _id;
        SetTip(ref nid, text);
        nid.uFlags = NIF_INFO;
        SetBalloon(ref nid, title, text);
        Shell_NotifyIcon(NIM_MODIFY, ref nid);
    }

    private void SetTip(ref NOTIFYICONDATAW nid, string tip)
    {
        var bytes = Encoding.Unicode.GetBytes(tip);
        if (bytes.Length >= 128 * 2) tip = tip.Substring(0, 127);
        nid.szTip = new string(tip);
    }

    private void SetBalloon(ref NOTIFYICONDATAW nid, string title, string text)
    {
        nid.szInfo = new string(text);
        nid.szInfoTitle = new string(title.Length > 63 ? title.Substring(0, 63) : title);
        nid.dwInfoFlags = 0;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_TRAYMESSAGE)
        {
            int ev = lParam.ToInt32();
            if (ev == WM_LBUTTONDBLCLK)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    var win = Application.Current.MainWindow;
                    if (win != null)
                    {
                        win.Show();
                        win.WindowState = WindowState.Normal;
                        win.Activate();
                    }
                });
            }
            else if (ev == WM_RBUTTONUP)
            {
                if (_hMenu != IntPtr.Zero)
                {
                    SetForegroundWindow(hwnd);
                    GetCursorPos(out POINT p);
                    uint cmd = TrackPopupMenuEx(_hMenu, TPM_RETURNCMD | TPM_LEFTALIGN, p.X, p.Y, hwnd, IntPtr.Zero);
                    if (cmd != 0)
                    {
                        if (cmd == ID_RESTORE)
                        {
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                var win = Application.Current.MainWindow;
                                if (win != null)
                                {
                                    win.Show(); win.WindowState = WindowState.Normal; win.Activate();
                                }
                            });
                        }
                        else if (cmd == ID_EXIT)
                        {
                            Application.Current.Dispatcher.Invoke(() => Application.Current.Shutdown());
                        }
                    }
                    PostMessage(hwnd, WM_NULL, IntPtr.Zero, IntPtr.Zero);
                }
            }
            handled = true;
        }
        else if (msg == WM_COMMAND)
        {
            int id = wParam.ToInt32() & 0xffff;
            if (id == ID_RESTORE)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    var win = Application.Current.MainWindow;
                    if (win != null) { win.Show(); win.WindowState = WindowState.Normal; win.Activate(); }
                });
                handled = true;
            }
            else if (id == ID_EXIT)
            {
                Application.Current.Dispatcher.Invoke(() => Application.Current.Shutdown());
                handled = true;
            }
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        try
        {
            if (!_initialized) return;
            var nid = new NOTIFYICONDATAW();
            nid.cbSize = (uint)Marshal.SizeOf(nid);
            nid.hWnd = _hwndSource!.Handle;
            nid.uID = _id;
            Shell_NotifyIcon(NIM_DELETE, ref nid);
            if (_hwndSource != null)
            {
                _hwndSource.RemoveHook(WndProc);
                _hwndSource = null;
            }
            if (_hMenu != IntPtr.Zero)
            {
                DestroyMenu(_hMenu);
                _hMenu = IntPtr.Zero;
            }
        }
        catch { }
    }

    #region Win32
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

    private const uint MF_STRING = 0x00000000;
    private const uint TPM_LEFTALIGN = 0x0000;
    private const uint TPM_RETURNCMD = 0x0100;
    private const int WM_COMMAND = 0x0111;
    private const int WM_NULL = 0x0000;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }
    #endregion
}
