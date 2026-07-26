using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Ntfy.Windows.Services;

public static class Localizer
{
    private static Dictionary<string, string> _map = new(StringComparer.OrdinalIgnoreCase);

    public static void Reload()
    {
        var lang = RuntimePreferences.LanguageCode;
        var baseDir = AppContext.BaseDirectory;
        var i18nDir = Path.Combine(baseDir, "i18n");
        var path = FindLanguageFile(i18nDir, lang);

        if (path is null)
            path = FindLanguageFile(i18nDir, "en") ?? Path.Combine(i18nDir, "en-US.json");

        try
        {
            var json = File.ReadAllText(path);
            _map = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            _map = new(StringComparer.OrdinalIgnoreCase);
        }
    }

    public static string T(string key)
    {
        if (_map.Count == 0) Reload();
        return _map.TryGetValue(key, out var v) ? v : key;
    }

    private static string? FindLanguageFile(string i18nDir, string lang)
    {
        var candidates = new[]
        {
            lang,
            lang.Replace('-', '_'),
            lang.Replace('_', '-'),
            lang.Equals("en", StringComparison.OrdinalIgnoreCase) ? "en-US" : string.Empty,
            lang.Equals("en-US", StringComparison.OrdinalIgnoreCase) ? "en" : string.Empty,
            lang.Equals("zh_Hant", StringComparison.OrdinalIgnoreCase) ? "zh-Hant" : string.Empty,
            lang.Equals("zh-Hant", StringComparison.OrdinalIgnoreCase) ? "zh_Hant" : string.Empty
        };

        foreach (var candidate in candidates.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var path = Path.Combine(i18nDir, $"{candidate}.json");
            if (File.Exists(path)) return path;
        }

        return null;
    }
}
