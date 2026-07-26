using System;

namespace Ntfy.Windows.Services;

public static class RuntimePreferences
{
    public static event Action? Changed;

    public static string ThemeMode { get; private set; } = "System";
    public static string LanguageCode { get; private set; } = "en";
    public static string DateTimeFormat { get; private set; } = "yyyy-MM-dd HH:mm:ss";
    public static bool CloseToTray { get; private set; } = true;

    public static void Set(string themeMode, string languageCode, string dateTimeFormat, bool closeToTray)
    {
        ThemeMode = string.IsNullOrWhiteSpace(themeMode) ? "System" : themeMode;
        LanguageCode = string.IsNullOrWhiteSpace(languageCode) ? "en" : languageCode;
        DateTimeFormat = string.IsNullOrWhiteSpace(dateTimeFormat) ? "yyyy-MM-dd HH:mm:ss" : dateTimeFormat;
        CloseToTray = closeToTray;
        Changed?.Invoke();
    }
}
