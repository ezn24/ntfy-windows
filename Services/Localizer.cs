using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Ntfy.WinUI.Client.Services;

public static class Localizer
{
    private static Dictionary<string, string> _map = new(StringComparer.OrdinalIgnoreCase);

    public static void Reload()
    {
        var lang = RuntimePreferences.LanguageCode;
        var baseDir = AppContext.BaseDirectory;
        var i18nDir = Path.Combine(baseDir, "i18n");
        var path = Path.Combine(i18nDir, $"{lang}.json");

        if (!File.Exists(path))
            path = Path.Combine(i18nDir, "en-US.json");

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
}
