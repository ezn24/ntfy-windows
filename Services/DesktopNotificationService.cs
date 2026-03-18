using System;
using System.IO;
using Microsoft.Windows.AppNotifications;
using Ntfy.Windows.Models;

namespace Ntfy.Windows.Services;

public static class DesktopNotificationService
{
    private static bool _isReady;

    public static void Initialize()
    {
        try
        {
            AppNotificationManager.Default.Register();
            _isReady = true;
        }
        catch
        {
            _isReady = false;
        }
    }

    public static void ShowMessage(NtfyMessage m)
    {
        if (!_isReady) return;

        try
        {
            var title = Escape(string.IsNullOrWhiteSpace(m.Title) ? $"[{m.Topic}]" : $"[{m.Topic}] {m.Title}");
            var body = Escape(string.IsNullOrWhiteSpace(m.Message) ? "(empty)" : m.Message);

            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Icons", "ntfy-toast.png");
            var iconUri = File.Exists(iconPath)
                ? $"file:///{iconPath.Replace("\\", "/")}" 
                : string.Empty;

            var imageNode = string.IsNullOrWhiteSpace(iconUri)
                ? string.Empty
                : $"<image placement='appLogoOverride' hint-crop='none' src='{Escape(iconUri)}'/>";

            var payload = $"<toast><visual><binding template='ToastGeneric'>{imageNode}<text>{title}</text><text>{body}</text></binding></visual></toast>";
            AppNotificationManager.Default.Show(new AppNotification(payload));
        }
        catch
        {
            // ignore
        }
    }

    private static string Escape(string s)
        => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;").Replace("'", "&apos;");
}
