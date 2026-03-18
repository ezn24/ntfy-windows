using System;

namespace Ntfy.Windows.Services;

public static class RuntimePreferences
{
    public static event Action? Changed;

    public static string ThemeMode { get; private set; } = "System";
    public static string LanguageCode { get; private set; } = "en-US";

    public static void Set(string themeMode, string languageCode)
    {
        ThemeMode = string.IsNullOrWhiteSpace(themeMode) ? "System" : themeMode;
        LanguageCode = string.IsNullOrWhiteSpace(languageCode) ? "en-US" : languageCode;
        Changed?.Invoke();
    }
}
