using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Ntfy.Windows.Services;

namespace Ntfy.Windows;

public partial class App : Application
{
    private Window? _window;
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

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            DesktopNotificationService.Initialize();
            _window = new MainWindow();
            _window.Activate();
            Log("Launch", "MainWindow activated");
        }
        catch (Exception ex)
        {
            Log("LaunchError", ex.ToString());
            throw;
        }
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
}
