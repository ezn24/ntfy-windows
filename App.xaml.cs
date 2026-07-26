using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Ntfy.Windows.Services;

namespace Ntfy.Windows;

public partial class App : Application
{
    private Window? _window;
    public static MainWindow? MainWindow { get; private set; }
    private static System.Threading.Mutex? _singleInstanceMutex;
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NtfyWindows",
        "startup.log");

    public App()
    {
        InitializeComponent();
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log("UnhandledException", e.ExceptionObject?.ToString());
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log("UnobservedTaskException", e.Exception?.ToString());
            e.SetObserved();
        };
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            _singleInstanceMutex = new System.Threading.Mutex(true, "Ntfy.Windows.SingleInstance", out var isFirstInstance);
            if (!isFirstInstance)
            {
                TryShowExistingInstance();
                Log("ExistingInstance", "Another instance is already running");
                Exit();
                return;
            }

            if (TryShowExistingInstance())
            {
                Log("ExistingInstance", "Showed existing window");
                Exit();
                return;
            }

            var settings = await new AppSettingsService().LoadAsync();
            DesktopNotificationService.Initialize();
            DesktopNotificationService.Configure(settings.StickyNotifications, settings.TimedNotificationSeconds);
            MainWindow = new MainWindow(settings);
            _window = MainWindow;
            if (settings.StartMinimizedToTray)
            {
                if (MainWindow.IsTrayAvailable)
                {
                    MainWindow.HideToTray();
                    Log("Launch", "Started hidden in tray");
                    return;
                }
                else
                    Log("StartMinimizedToTraySkipped", "Tray icon was not available");
            }

            _window.Activate();
            Log("Launch", "MainWindow activated");
        }
        catch (Exception ex)
        {
            Log("LaunchError", ex.ToString());
            throw;
        }
    }

    private static bool TryShowExistingInstance()
    {
        try
        {
            var current = Environment.ProcessId;
            foreach (var process in System.Diagnostics.Process.GetProcessesByName("Ntfy.Windows"))
            {
                if (process.Id == current) continue;
                var shown = false;
                EnumWindows((hWnd, _) =>
                {
                    GetWindowThreadProcessId(hWnd, out var pid);
                    if (pid != process.Id) return true;
                    var titleLength = GetWindowTextLength(hWnd);
                    if (titleLength == 0) return true;

                    var title = new System.Text.StringBuilder(titleLength + 1);
                    GetWindowText(hWnd, title, title.Capacity);
                    if (!title.ToString().Equals("ntfy-windows", StringComparison.Ordinal)) return true;

                    ShowWindow(hWnd, 9);
                    SetForegroundWindow(hWnd);
                    shown = true;
                    return true;
                }, IntPtr.Zero);

                if (shown) return true;
            }
        }
        catch
        {
            // Continue normal launch if instance detection fails.
        }

        return false;
    }

    private static void Log(string phase, string? text)
    {
        try
        {
            var dir = Path.GetDirectoryName(LogPath)!;
            Directory.CreateDirectory(dir);
            File.AppendAllText(LogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {phase}: {text}{Environment.NewLine}");
        }
        catch
        {
            // ignore logging failures
        }
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out int processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder text, int maxCount);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}
