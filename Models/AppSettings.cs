using System.Collections.Generic;

namespace Ntfy.Windows.Models;

public sealed class AppSettings
{
    public ServerProfile Server { get; set; } = new();
    public List<string> Topics { get; set; } = new();
    public string ActiveTopic { get; set; } = string.Empty;
    public List<string> MutedTopics { get; set; } = new();
    public Dictionary<string, string> TopicDisplayNames { get; set; } = new();
    public Dictionary<string, string> LastMessageIds { get; set; } = new();

    public string DateTimeFormat { get; set; } = "yyyy-MM-dd HH:mm:ss";
    public bool StickyNotifications { get; set; } = true;
    public int TimedNotificationSeconds { get; set; } = 8;

    public bool CloseToTray { get; set; } = true;
    public bool StartMinimizedToTray { get; set; } = false;

    public string ThemeMode { get; set; } = "System"; // System/Light/Dark
    public string LanguageCode { get; set; } = "en"; // en/zh_Hant, with legacy en-US/zh-Hant aliases
}
