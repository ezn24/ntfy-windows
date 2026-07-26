using System;
using System.IO;
using Microsoft.Windows.AppNotifications;
using Ntfy.Windows.Models;

namespace Ntfy.Windows.Services;

public static class DesktopNotificationService
{
    private static bool _isReady;
    private static bool _stickyNotifications = true;
    private static int _timedNotificationSeconds = 8;

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

    public static void Configure(bool stickyNotifications, int timedNotificationSeconds)
    {
        _stickyNotifications = stickyNotifications;
        _timedNotificationSeconds = Math.Max(1, timedNotificationSeconds);
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

            var toastAttributes = _stickyNotifications
                ? "scenario='reminder' duration='long'"
                : "duration='short'";

            var payload = $"<toast {toastAttributes}><visual><binding template='ToastGeneric'>{imageNode}<text>{title}</text><text>{body}</text></binding></visual></toast>";
            var notification = new AppNotification(payload)
            {
                Expiration = DateTimeOffset.Now.AddSeconds(_timedNotificationSeconds)
            };

            AppNotificationManager.Default.Show(notification);
        }
        catch
        {
            // ignore
        }
    }

    private static string Escape(string s)
        => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;").Replace("'", "&apos;");
}
