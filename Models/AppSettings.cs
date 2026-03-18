using System.Collections.Generic;

namespace Ntfy.Windows.Models;

public sealed class AppSettings
{
    public ServerProfile Server { get; set; } = new();
    public List<string> Topics { get; set; } = new();
    public string ActiveTopic { get; set; } = string.Empty;

    public int PollIntervalSeconds { get; set; } = 30;
    public string DateTimeFormat { get; set; } = "yyyy-MM-dd HH:mm:ss";
    public bool StickyNotifications { get; set; } = true;
    public int TimedNotificationSeconds { get; set; } = 8;

    public bool QuitOnClose { get; set; } = false;
    public bool StartHidden { get; set; } = false;
    public bool EnableHotkeys { get; set; } = false;

    public string ThemeMode { get; set; } = "System"; // System/Light/Dark
    public string LanguageCode { get; set; } = "en-US"; // en-US/zh-Hant
}
