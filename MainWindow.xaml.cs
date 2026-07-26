using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Ntfy.Windows.Models;
using Ntfy.Windows.Services;
using Ntfy.Windows.Views;
using Windows.Graphics;
using Windows.UI;
using WinRT.Interop;

namespace Ntfy.Windows;

public sealed partial class MainWindow : Window
{
    private const int WM_TRAYICON = 0x8001;
    private const int WM_COMMAND = 0x0111;
    private const int WM_CLOSE = 0x0010;
    private const int WM_SETICON = 0x0080;
    private const int WM_GETMINMAXINFO = 0x0024;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_LBUTTONDBLCLK = 0x0203;
    private const int WM_RBUTTONUP = 0x0205;

    private const uint NIM_ADD = 0x00000000;
    private const uint NIM_MODIFY = 0x00000001;
    private const uint NIM_DELETE = 0x00000002;

    private const uint NIF_MESSAGE = 0x00000001;
    private const uint NIF_ICON = 0x00000002;
    private const uint NIF_TIP = 0x00000004;

    private const uint IMAGE_ICON = 1;
    private const uint LR_LOADFROMFILE = 0x00000010;
    private const uint LR_DEFAULTSIZE = 0x00000040;

    private const uint TPM_RIGHTBUTTON = 0x0002;
    private const int SW_HIDE = 0;
    private const int SW_SHOW = 5;

    private const uint MF_STRING = 0x00000000;
    private const uint MF_SEPARATOR = 0x00000800;

    private const int CMD_OPEN = 1001;
    private const int CMD_EXIT = 1002;

    private readonly AppSettingsService _settingsService = new();

    private AppWindow? _appWindow;
    private IntPtr _hwnd;
    private IntPtr _hIcon;
    private bool _trayAdded;
    private bool _allowClose;
    private System.Windows.Forms.NotifyIcon? _notifyIcon;
    private System.Windows.Forms.ContextMenuStrip? _trayMenu;

    private WndProcDelegate? _wndProcDelegate;
    private IntPtr _oldWndProc;

    public MainWindow(AppSettings? initialSettings = null)
    {
        InitializeComponent();
        if (initialSettings is not null)
        {
            RuntimePreferences.Set(initialSettings.ThemeMode, initialSettings.LanguageCode, initialSettings.DateTimeFormat, initialSettings.CloseToTray);
            DesktopNotificationService.Configure(initialSettings.StickyNotifications, initialSettings.TimedNotificationSeconds);
        }

        ContentFrame.Navigate(typeof(InboxPage));
        _ = ApplyAppearanceAsync();

        TryApplyWin11Backdrop();
        TryUseCustomTitleBar();
        InitTray();

        Closed += (_, _) => CleanupTray();
        RuntimePreferences.Changed += ApplyRuntimePreferences;
    }

    public void HideToTray()
    {
        try { _appWindow?.Hide(); } catch { }
        try { ShowWindow(_hwnd, SW_HIDE); } catch { }
    }

    public void ShowWindowFromApp()
    {
        try { _appWindow?.Show(); } catch { }
        try { ShowWindow(_hwnd, SW_SHOW); } catch { }
        Activate();
        _ = CheckForUpdatesOnStartupAsync();
    }

    public bool IsTrayAvailable => _notifyIcon?.Visible == true || _trayAdded;

    public void ShowWindowOverlay(FrameworkElement content)
    {
        HideWindowOverlay();

        var overlay = new Grid
        {
            Name = "WindowOverlay",
            Background = new SolidColorBrush(Color.FromArgb(140, 0, 0, 0)),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Children = { content }
        };
        Canvas.SetZIndex(overlay, 100);
        RootGrid.Children.Add(overlay);
    }

    public void HideWindowOverlay()
    {
        var overlays = RootGrid.Children
            .OfType<FrameworkElement>()
            .Where(x => x.Name == "WindowOverlay")
            .ToList();

        foreach (var overlay in overlays)
            RootGrid.Children.Remove(overlay);
    }

    private void TryApplyWin11Backdrop()
    {
        try { SystemBackdrop = new MicaBackdrop(); }
        catch { }
    }

    private void TryUseCustomTitleBar()
    {
        try
        {
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(null);

            _hwnd = WindowNative.GetWindowHandle(this);
            var windowId = Win32Interop.GetWindowIdFromWindow(_hwnd);
            _appWindow = AppWindow.GetFromWindowId(windowId);
            if (_appWindow?.TitleBar is { } tb)
            {
                tb.ExtendsContentIntoTitleBar = true;
                tb.ButtonBackgroundColor = Colors.Transparent;
                tb.ButtonInactiveBackgroundColor = Colors.Transparent;
            }

            if (_appWindow is not null)
            {
                _appWindow.Closing += AppWindow_Closing;
                if (_appWindow.Size.Width < 1180 || _appWindow.Size.Height < 680)
                    _appWindow.Resize(new SizeInt32(1280, 760));
            }

            HookWindowProc();
        }
        catch { }
    }

    private void InitTray()
    {
        try
        {
            if (_hwnd == IntPtr.Zero) return;

            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Icons", "ntfy.ico");
            if (TryInitNotifyIcon(iconPath))
                return;

            _hIcon = LoadImage(IntPtr.Zero, iconPath, IMAGE_ICON, 0, 0, LR_LOADFROMFILE | LR_DEFAULTSIZE);

            var data = NewNotifyData();
            data.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP;
            data.uCallbackMessage = WM_TRAYICON;
            data.hIcon = _hIcon;
            data.szTip = "ntfy Client";

            _trayAdded = Shell_NotifyIcon(NIM_ADD, ref data);
            if (_trayAdded)
                Shell_NotifyIcon(NIM_MODIFY, ref data);

            if (_hIcon != IntPtr.Zero)
            {
                SendMessage(_hwnd, WM_SETICON, (IntPtr)1, _hIcon); // big
                SendMessage(_hwnd, WM_SETICON, IntPtr.Zero, _hIcon); // small
            }
        }
        catch { }
    }

    private bool TryInitNotifyIcon(string iconPath)
    {
        try
        {
            if (!File.Exists(iconPath)) return false;

            _trayMenu = new System.Windows.Forms.ContextMenuStrip();
            _trayMenu.Items.Add(Localizer.T("Open"), null, (_, _) => DispatcherQueue.TryEnqueue(ShowFromTray));
            _trayMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            _trayMenu.Items.Add(Localizer.T("Exit"), null, (_, _) => DispatcherQueue.TryEnqueue(ExitFromTray));

            _notifyIcon = new System.Windows.Forms.NotifyIcon
            {
                Icon = new System.Drawing.Icon(iconPath),
                Text = Localizer.T("AppName"),
                ContextMenuStrip = _trayMenu,
                Visible = true
            };

            _notifyIcon.MouseUp += (_, e) =>
            {
                if (e.Button == System.Windows.Forms.MouseButtons.Left)
                    DispatcherQueue.TryEnqueue(ShowFromTray);
            };

            _notifyIcon.DoubleClick += (_, _) => DispatcherQueue.TryEnqueue(ShowFromTray);
            return true;
        }
        catch
        {
            _notifyIcon?.Dispose();
            _notifyIcon = null;
            _trayMenu?.Dispose();
            _trayMenu = null;
            return false;
        }
    }

    private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_allowClose) return;
        if (!RuntimePreferences.CloseToTray || !IsTrayAvailable)
        {
            _allowClose = true;
            CleanupTray();
            return;
        }

        args.Cancel = true;
        HideToTray();
    }

    private void ShowFromTray()
    {
        try
        {
            ShowWindowFromApp();
        }
        catch { }
    }

    private void ExitFromTray()
    {
        _allowClose = true;
        CleanupTray();
        try { PostMessage(_hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero); } catch { }
    }

    private void ShowTrayMenu()
    {
        try
        {
            var menu = CreatePopupMenu();
            AppendMenu(menu, MF_STRING, CMD_OPEN, Localizer.T("Open"));
            AppendMenu(menu, MF_SEPARATOR, 0, string.Empty);
            AppendMenu(menu, MF_STRING, CMD_EXIT, Localizer.T("Exit"));

            GetCursorPos(out var pt);
            SetForegroundWindow(_hwnd);
            TrackPopupMenu(menu, TPM_RIGHTBUTTON, pt.X, pt.Y, 0, _hwnd, IntPtr.Zero);
            DestroyMenu(menu);
        }
        catch { }
    }

    private async Task ApplyAppearanceAsync()
    {
        var settings = await _settingsService.LoadAsync();
        RuntimePreferences.Set(settings.ThemeMode, settings.LanguageCode, settings.DateTimeFormat, settings.CloseToTray);
        DesktopNotificationService.Configure(settings.StickyNotifications, settings.TimedNotificationSeconds);
        ApplyRuntimePreferences();
    }

    private void ApplyRuntimePreferences()
    {
        Localizer.Reload();

        var theme = RuntimePreferences.ThemeMode switch
        {
            "Light" => ElementTheme.Light,
            "Dark" => ElementTheme.Dark,
            _ => ElementTheme.Default
        };

        RootNav.RequestedTheme = theme;
        if (Content is FrameworkElement fe)
            fe.RequestedTheme = theme;

        ChatNavItem.Content = Localizer.T("TopicNav");
        UpdateTrayText();
    }

    private void UpdateTrayText()
    {
        if (_notifyIcon is not null)
            _notifyIcon.Text = Localizer.T("AppName");

        if (_trayMenu is { Items.Count: >= 3 })
        {
            _trayMenu.Items[0].Text = Localizer.T("Open");
            _trayMenu.Items[2].Text = Localizer.T("Exit");
        }
    }

    private void RootNav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected)
        {
            ContentFrame.Navigate(typeof(SettingsPage));
            return;
        }

        if (args.SelectedItemContainer?.Tag is "inbox")
            ContentFrame.Navigate(typeof(InboxPage));
    }

    private void HookWindowProc()
    {
        if (_hwnd == IntPtr.Zero || _wndProcDelegate is not null) return;

        _wndProcDelegate = WndProc;
        var newProcPtr = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate);
        _oldWndProc = SetWindowLongPtr(_hwnd, -4, newProcPtr);
    }

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_GETMINMAXINFO)
        {
            var info = Marshal.PtrToStructure<MINMAXINFO>(lParam);
            info.ptMinTrackSize.X = 1180;
            info.ptMinTrackSize.Y = 680;
            Marshal.StructureToPtr(info, lParam, true);
            return IntPtr.Zero;
        }
        if (msg == WM_TRAYICON)
        {
            var eventId = lParam.ToInt32();
            if (eventId == WM_LBUTTONUP || eventId == WM_LBUTTONDBLCLK)
                DispatcherQueue.TryEnqueue(ShowFromTray);
            else if (eventId == WM_RBUTTONUP)
                DispatcherQueue.TryEnqueue(ShowTrayMenu);
            return IntPtr.Zero;
        }

        if (msg == WM_COMMAND)
        {
            var cmd = wParam.ToInt32() & 0xFFFF;
            if (cmd == CMD_OPEN)
            {
                DispatcherQueue.TryEnqueue(ShowFromTray);
                return IntPtr.Zero;
            }

            if (cmd == CMD_EXIT)
            {
                DispatcherQueue.TryEnqueue(ExitFromTray);
                return IntPtr.Zero;
            }
        }

        return CallWindowProc(_oldWndProc, hWnd, msg, wParam, lParam);
    }

    private void CleanupTray()
    {
        try
        {
            if (_notifyIcon is not null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
                _notifyIcon = null;
            }

            _trayMenu?.Dispose();
            _trayMenu = null;

            if (_trayAdded)
            {
                var data = NewNotifyData();
                Shell_NotifyIcon(NIM_DELETE, ref data);
                _trayAdded = false;
            }

            if (_hIcon != IntPtr.Zero)
            {
                DestroyIcon(_hIcon);
                _hIcon = IntPtr.Zero;
            }

            if (_oldWndProc != IntPtr.Zero && _hwnd != IntPtr.Zero)
            {
                SetWindowLongPtr(_hwnd, -4, _oldWndProc);
                _oldWndProc = IntPtr.Zero;
            }
        }
        catch { }
    }

    private NOTIFYICONDATA NewNotifyData() => new()
    {
        cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
        hWnd = _hwnd,
        uID = 1
    };

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
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

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA lpData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadImage(IntPtr hInst, string lpszName, uint uType, int cxDesired, int cyDesired, uint fuLoad);

    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll")]
    private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenu(IntPtr hMenu, uint uFlags, int uIDNewItem, string lpNewItem);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenu(IntPtr hMenu, uint uFlags, int x, int y, int nReserved, IntPtr hWnd, IntPtr prcRect);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
}
